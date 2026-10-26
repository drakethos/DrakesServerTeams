using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// The team a ward, chest or door was built under, stored on the piece's ZDO as <c>team|signature</c>.
/// <para>
/// Only the server writes it. The signature is an HMAC over (creator id, team id) with a key that never leaves the
/// server, so the server can tell its own stamps from ones a modified client wrote. ZDO ids are renumbered on every
/// world load, so they are not part of the signature; copying a stamp to another piece only verifies if the creator
/// field is copied too, which vanilla already trusts for private chests.
/// </para>
/// <para>
/// Pieces keep the team they were stamped with when their builder changes team. A piece built before the mod (or
/// while its builder had no team) is stamped with the builder's team the next time the server sweeps it.
/// </para>
/// Dormant until a mod calls <see cref="TeamsAPI.UsePieceTeams"/>.
/// </summary>
public static class PieceTeams
{
    public static readonly int StampKey = "DrakesServerTeams_pieceTeam".GetStableHashCode();
    static readonly int CreatorKey = "creator".GetStableHashCode();
    const int SweepBudgetPerFrame = 2000;
    const float RequestRetrySeconds = 10f;

    static readonly FieldInfo? ObjectsByIdField = AccessTools.Field(typeof(ZDOMan), "m_objectsByID");
    static readonly HashSet<int> StampedPrefabs = new();
    static readonly List<(ZDOID id, long characterId, float until)> Pending = new();

    static ManualLogSource _log = null!;
    static byte[]? _key;
    static bool _prefabsBuilt;
    static Coroutine? _sweep;
    static float _nextSweep;

    /// <summary>True once a mod asked for piece teams. Patches and the sweep only run while this is on.</summary>
    public static bool Active { get; private set; }

    internal static void Init(ManualLogSource log) => _log = log;

    internal static void Activate(Harmony harmony)
    {
        if (Active)
            return;
        Active = true;
        harmony.PatchAll(typeof(PieceSetCreatorPatch));
        _log.LogInfo("Piece teams on (requested by another mod).");
    }

    /// <summary>The team id stamped on the piece, or null. Any peer can read it; it does not verify the signature.</summary>
    public static string? ReadTeam(ZDO? zdo)
    {
        if (zdo == null)
            return null;
        var stamp = zdo.GetString(StampKey, "");
        var bar = stamp.LastIndexOf('|');
        return bar > 0 ? stamp.Substring(0, bar) : null;
    }

    // ---- server ---------------------------------------------------------------------------------------

    /// <summary>A client just built a piece. Stamp it with the builder's team once its ZDO reaches the server.</summary>
    internal static void OnStampRequest(ZDOID id, long characterId)
    {
        if (!Active || id == ZDOID.None)
            return;
        if (!TryStampNew(id, characterId))
            Pending.Add((id, characterId, Time.unscaledTime + RequestRetrySeconds));
    }

    /// <summary>Called every frame on the authority.</summary>
    internal static void Tick(MonoBehaviour host)
    {
        if (!Active || ZDOMan.instance == null)
            return;

        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            var (id, characterId, until) = Pending[i];
            if (TryStampNew(id, characterId) || Time.unscaledTime > until)
                Pending.RemoveAt(i);
        }

        if (_sweep == null && Time.unscaledTime >= _nextSweep)
            _sweep = host.StartCoroutine(Sweep());
    }

    /// <summary>Run a sweep now (admin command, tests). Returns false when one is already running.</summary>
    internal static bool SweepNow(MonoBehaviour host)
    {
        if (!Active || _sweep != null)
            return false;
        _sweep = host.StartCoroutine(Sweep());
        return true;
    }

    internal static bool Sweeping => _sweep != null;

    /// <summary>Checks one piece now. Returns true when it changed the stamp.</summary>
    internal static bool CheckOne(ZDO zdo)
    {
        var creator = zdo.GetLong(CreatorKey, 0L);
        var stamp = zdo.GetString(StampKey, "");
        string want;
        if (creator == 0)
            want = "";
        else if (stamp.Length > 0 && IsSigned(stamp, creator))
            return false;
        else
        {
            var team = TeamRegistry.Instance.GetTeamId(creator);
            want = team != null ? MakeStamp(team, creator) : "";
        }
        if (want == stamp)
            return false;
        zdo.Set(StampKey, want);
        return true;
    }

    internal static bool IsStampedPrefab(int prefabHash)
    {
        BuildPrefabSet();
        return StampedPrefabs.Contains(prefabHash);
    }

    static bool TryStampNew(ZDOID id, long characterId)
    {
        var zdo = ZDOMan.instance?.GetZDO(id);
        if (zdo == null)
            return false;
        // Only the builder may ask, and only for a piece type that carries a team.
        if (zdo.GetLong(CreatorKey, 0L) != characterId || !IsStampedPrefab(zdo.GetPrefab()))
            return true;
        // A Team Ward from someone who may not place one (a modified client skipped the check): remove it.
        if (TeamsConfig.TeamWardAdminOnly && zdo.GetPrefab() == TeamWard.PrefabName.GetStableHashCode() && !IsAdminCharacter(characterId))
        {
            _log.LogWarning("A Team Ward placed by a non-admin was removed.");
            ZDOMan.instance?.DestroyZDO(zdo);
            return true;
        }
        var team = TeamRegistry.Instance.GetTeamId(characterId);
        zdo.Set(StampKey, team != null ? MakeStamp(team, characterId) : "");
        return true;
    }

    static bool IsAdminCharacter(long characterId)
    {
        var sender = PeerIdentity.FindSender(characterId);
        return sender != null && PeerIdentity.IsAdmin(sender.Value);
    }

    /// <summary>Server: set a piece's team (admin tool). Signed for the piece's creator like any stamp; null clears it. False if the piece has no creator.</summary>
    internal static bool Assign(ZDO zdo, string? teamId)
    {
        var creator = zdo.GetLong(CreatorKey, 0L);
        if (creator == 0)
            return false;
        zdo.Set(StampKey, string.IsNullOrEmpty(teamId) ? "" : MakeStamp(teamId!, creator));
        return true;
    }

    static IEnumerator Sweep()
    {
        var started = Time.unscaledTime;
        List<ZDO>? all = null;
        try
        {
            if (ObjectsByIdField?.GetValue(ZDOMan.instance) is Dictionary<ZDOID, ZDO> byId)
                all = new List<ZDO>(byId.Values);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Piece teams: cannot read the world's objects ({ex.Message}). Only new pieces get a team.");
        }

        var changed = 0;
        if (all != null)
        {
            BuildPrefabSet();
            for (var i = 0; i < all.Count; i++)
            {
                if (i > 0 && i % SweepBudgetPerFrame == 0)
                    yield return null;
                var zdo = all[i];
                // The snapshot can hold ZDOs destroyed (and pooled) since it was taken.
                if (zdo == null || !zdo.IsValid() || !StampedPrefabs.Contains(zdo.GetPrefab()))
                    continue;
                if (CheckOne(zdo))
                    changed++;
            }
        }

        if (changed > 0)
            _log.LogInfo($"Piece teams: updated {changed} piece stamp(s) in {Time.unscaledTime - started:0.0}s.");
        _nextSweep = Time.unscaledTime + TeamsConfig.StampSweepSeconds;
        _sweep = null;
    }

    /// <summary>Wards, chests and doors that players build: anything with a Piece plus PrivateArea, Container or Door.</summary>
    static void BuildPrefabSet()
    {
        if (_prefabsBuilt || ZNetScene.instance == null)
            return;
        _prefabsBuilt = true;
        foreach (var prefab in ZNetScene.instance.m_prefabs)
        {
            if (prefab == null || prefab.GetComponent<Piece>() == null)
                continue;
            if (prefab.GetComponent<PrivateArea>() != null
                || prefab.GetComponentInChildren<Container>(true) != null
                || prefab.GetComponentInChildren<Door>(true) != null)
                StampedPrefabs.Add(prefab.name.GetStableHashCode());
        }
    }

    internal static string MakeStamp(string teamId, long creator) => teamId + "|" + Sign(teamId, creator);

    static bool IsSigned(string stamp, long creator)
    {
        var bar = stamp.LastIndexOf('|');
        if (bar <= 0)
            return false;
        var team = stamp.Substring(0, bar);
        // Unknown team (removed from teams.json): treat as unsigned so the piece falls back to the builder's team.
        return TeamRegistry.Instance.GetTeam(team) != null
               && string.Equals(stamp.Substring(bar + 1), Sign(team, creator), StringComparison.Ordinal);
    }

    static HMACSHA256? _hmac;

    static string Sign(string teamId, long creator)
    {
        _hmac ??= new HMACSHA256(Key());
        var hash = _hmac.ComputeHash(Encoding.UTF8.GetBytes("v1|" + creator + "|" + teamId.ToLowerInvariant()));
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
            sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }

    /// <summary>Server-only secret in the teams folder. Deleting it makes every stamp unsigned (re-stamped on the next sweep).</summary>
    static byte[] Key()
    {
        if (_key != null)
            return _key;
        var path = Path.Combine(TeamStore.Folder, "stamp.key");
        try
        {
            if (File.Exists(path))
            {
                var stored = Convert.FromBase64String(File.ReadAllText(path).Trim());
                if (stored.Length >= 32)
                    return _key = stored;
            }
            var fresh = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(fresh);
            Directory.CreateDirectory(TeamStore.Folder);
            File.WriteAllText(path, Convert.ToBase64String(fresh));
            return _key = fresh;
        }
        catch (Exception ex)
        {
            // Without a stored key stamps are still consistent for this session, just not across restarts.
            _log.LogWarning($"Piece teams: cannot read or write {path} ({ex.Message}).");
            var fallback = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(fallback);
            return _key = fallback;
        }
    }
}

/// <summary>The local player placed a piece: ask the server to stamp it with our team.</summary>
[HarmonyPatch(typeof(Piece), "SetCreator")]
static class PieceSetCreatorPatch
{
    static void Postfix(Piece __instance)
    {
        if (Player.m_localPlayer == null || __instance == null)
            return;
        var view = __instance.GetComponent<ZNetView>();
        var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        if (zdo == null || zdo.GetLong("creator".GetStableHashCode(), 0L) != Player.m_localPlayer.GetPlayerID())
            return;
        if (__instance.GetComponent<PrivateArea>() == null
            && __instance.GetComponentInChildren<Container>(true) == null
            && __instance.GetComponentInChildren<Door>(true) == null)
            return;
        TeamsNet.RequestStamp(zdo.m_uid);
    }
}
