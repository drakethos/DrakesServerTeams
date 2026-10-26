#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using DrakesServerTeams.Testing;

namespace DrakesServerTeams.Spikes;

/// <summary>
/// SPIKE stage 2 (debug only): ward shield + crumbling structures, prototyped on vanilla wards that carry a team stamp.
/// Everything is off until the "spike.enable" hook switches it on, so debug builds behave like release builds by default.
/// Rules:
///  - A piece inside an enabled, team-stamped ward hit by a player of ANOTHER team: the ward's shield absorbs the damage while it is up.
///  - Shield at 0 = siege: damage lands on pieces. A piece that would be destroyed crumbles instead: it stays, shows its broken
///    state, loses its collider (walkable), and ignores further damage.
///  - Only a player on the ward's team can repair a crumbled piece, only after a cooldown, and only to a fraction of its health.
/// </summary>
internal static class WarCrumble
{
    public static bool Crumble;
    public static bool Shield;
    public static float ShieldMax = 300f;
    public static float ShieldFactor = 1f;
    public static float DownSeconds = 60f;
    public static float RepairCooldown = 20f;
    public static float RepairFraction = 0.3f;

    public static readonly int CrumbledKey = "DrakesWar_crumbled".GetStableHashCode();
    public static readonly int CrumbledAtKey = "DrakesWar_crumbledAt".GetStableHashCode();
    public static readonly int ShieldKey = "DrakesWar_shield".GetStableHashCode();
    public static readonly int DownAtKey = "DrakesWar_shieldDownAt".GetStableHashCode();

    static readonly FieldInfo? AllAreas = AccessTools.Field(typeof(PrivateArea), "m_allAreas");
    static readonly FieldInfo? NviewField = AccessTools.Field(typeof(WearNTear), "m_nview");
    static readonly FieldInfo? MaxHealth = AccessTools.Field(typeof(WearNTear), "m_health");
    static readonly FieldInfo? Colliders = AccessTools.Field(typeof(WearNTear), "m_colliders");
    static readonly MethodInfo? IsEnabledM = AccessTools.Method(typeof(PrivateArea), "IsEnabled");
    static readonly MethodInfo? IsInsideM = AccessTools.Method(typeof(PrivateArea), "IsInside", new[] { typeof(Vector3), typeof(float) });
    static readonly FieldInfo? AreaView = AccessTools.Field(typeof(PrivateArea), "m_nview");

    public static ZNetView? View(WearNTear w) => NviewField?.GetValue(w) as ZNetView;
    public static float Max(WearNTear w) => MaxHealth != null ? (float)MaxHealth.GetValue(w) : 0f;
    public static Collider[] ColliderList(WearNTear w) => Colliders?.GetValue(w) as Collider[] ?? Array.Empty<Collider>();

    /// <summary>The enabled, team-stamped ward covering a point, and its team.</summary>
    public static bool FindWard(Vector3 point, out PrivateArea? ward, out string team)
    {
        ward = null;
        team = "";
        if (AllAreas?.GetValue(null) is not List<PrivateArea> areas)
            return false;
        foreach (var a in areas)
        {
            if (a == null || IsEnabledM?.Invoke(a, null) is not true || IsInsideM?.Invoke(a, new object[] { point, 0f }) is not true)
                continue;
            var zdo = (AreaView?.GetValue(a) as ZNetView)?.GetZDO();
            var t = PieceTeams.ReadTeam(zdo);
            if (t == null)
                continue;
            ward = a;
            team = t;
            return true;
        }
        return false;
    }

    public static ZDO? WardZdo(PrivateArea ward) => (AreaView?.GetValue(ward) as ZNetView)?.GetZDO();

    /// <summary>Is this hit an enemy attack on the ward's team? (a player of another team; null team never counts)</summary>
    public static bool IsEnemyHit(HitData? hit, string wardTeam)
    {
        if (hit == null)
            return false;
        if (hit.GetAttacker() is not Player attacker)
            return false;
        var team = TeamsAPI.GetTeamId(attacker);
        return team != null && !string.Equals(team, wardTeam, StringComparison.OrdinalIgnoreCase);
    }

    static double Now => ZNet.instance != null ? ZNet.instance.GetTime().Ticks / 1e7 : Time.timeAsDouble;

    public static float ShieldValue(ZDO zdo)
    {
        var down = zdo.GetFloat(DownAtKey, 0f);
        if (down > 0f && Now >= down + DownSeconds)
        {
            zdo.Set(ShieldKey, ShieldMax);
            zdo.Set(DownAtKey, 0f);
        }
        return zdo.GetFloat(ShieldKey, ShieldMax);
    }

    public static bool IsDown(ZDO zdo) => ShieldValue(zdo) <= 0f;

    public static bool IsCrumbled(WearNTear w) => View(w)?.GetZDO()?.GetInt(CrumbledKey, 0) == 1;

    /// <summary>Apply the crumbled state locally: no collider, broken look.</summary>
    public static void ApplyState(WearNTear w)
    {
        var crumbled = IsCrumbled(w);
        foreach (var c in ColliderList(w))
            if (c != null && !c.isTrigger)
                c.enabled = !crumbled;
    }
}

[HarmonyPatch(typeof(WearNTear), "ApplyDamage")]
static class WarApplyDamagePatch
{
    static bool Prefix(WearNTear __instance, float damage, HitData hitData, ref bool __result)
    {
        if (!WarCrumble.Crumble && !WarCrumble.Shield)
            return true;
        var view = WarCrumble.View(__instance);
        var zdo = view?.GetZDO();
        if (zdo == null)
            return true;
        if (WarCrumble.IsCrumbled(__instance))
        {
            __result = false; // already crumbled: nothing left to break
            return false;
        }
        if (!WarCrumble.FindWard(__instance.transform.position, out var ward, out var team) || ward == null || !WarCrumble.IsEnemyHit(hitData, team))
            return true;
        var wardZdo = WarCrumble.WardZdo(ward);
        if (!WarCrumble.Shield || wardZdo == null)
            return true;
        var shield = WarCrumble.ShieldValue(wardZdo);
        if (shield <= 0f)
            return true; // siege: the piece takes the damage
        shield = Mathf.Max(0f, shield - damage * WarCrumble.ShieldFactor);
        if (!wardZdo.IsOwner())
            wardZdo.SetOwner(ZDOMan.GetSessionID());
        wardZdo.Set(WarCrumble.ShieldKey, shield);
        if (shield <= 0f)
            wardZdo.Set(WarCrumble.DownAtKey, (float)(ZNet.instance.GetTime().Ticks / 1e7));
        __result = false; // the shield took it, the piece is untouched
        return false;
    }
}

[HarmonyPatch(typeof(WearNTear), "Destroy")]
static class WarDestroyPatch
{
    static bool Prefix(WearNTear __instance, HitData hitData)
    {
        if (!WarCrumble.Crumble || hitData == null)
            return true;
        if (!WarCrumble.FindWard(__instance.transform.position, out _, out var team) || !WarCrumble.IsEnemyHit(hitData, team))
            return true;
        var zdo = WarCrumble.View(__instance)?.GetZDO();
        if (zdo == null)
            return true;
        // crumble instead of being deleted
        zdo.Set(WarCrumble.CrumbledKey, 1);
        zdo.Set(WarCrumble.CrumbledAtKey, (float)(ZNet.instance.GetTime().Ticks / 1e7));
        zdo.Set("health", 1f);
        WarCrumble.ApplyState(__instance);
        AccessTools.Method(typeof(WearNTear), "UpdateVisual", new[] { typeof(bool) })?.Invoke(__instance, new object[] { true });
        return false;
    }
}

[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Repair))]
static class WarRepairPatch
{
    static bool Prefix(WearNTear __instance, ref bool __result)
    {
        if (!WarCrumble.Crumble || !WarCrumble.IsCrumbled(__instance))
            return true;
        var zdo = WarCrumble.View(__instance)?.GetZDO();
        var me = Player.m_localPlayer;
        if (zdo == null || me == null || !WarCrumble.FindWard(__instance.transform.position, out _, out var team))
            return true;
        var mine = TeamsAPI.GetTeamId(me);
        var since = (float)(ZNet.instance.GetTime().Ticks / 1e7) - zdo.GetFloat(WarCrumble.CrumbledAtKey, 0f);
        if (!string.Equals(mine, team, StringComparison.OrdinalIgnoreCase))
        {
            WarSpikeMessages.Last = "not your team's ward";
            __result = false;
            return false;
        }
        if (since < WarCrumble.RepairCooldown)
        {
            WarSpikeMessages.Last = $"too soon ({WarCrumble.RepairCooldown - since:0}s)";
            __result = false;
            return false;
        }
        zdo.Set(WarCrumble.CrumbledKey, 0);
        zdo.Set("health", WarCrumble.Max(__instance) * WarCrumble.RepairFraction);
        WarCrumble.ApplyState(__instance);
        AccessTools.Method(typeof(WearNTear), "UpdateVisual", new[] { typeof(bool) })?.Invoke(__instance, new object[] { true });
        WarSpikeMessages.Last = "repaired";
        __result = true;
        return false;
    }
}

internal static class WarSpikeMessages
{
    public static string Last = "";
}

/// <summary>Keeps the crumbled look (no collider) in step with the piece's ZDO on every client, including late joiners.</summary>
public sealed class WarCrumbleState : MonoBehaviour
{
    WearNTear? _wnt;
    float _next;

    void Awake() => _wnt = GetComponent<WearNTear>();

    void Update()
    {
        if (_wnt == null || Time.unscaledTime < _next)
            return;
        _next = Time.unscaledTime + 0.4f;
        WarCrumble.ApplyState(_wnt);
    }
}

[HarmonyPatch(typeof(WearNTear), "Awake")]
static class WarCrumbleAwakePatch
{
    static void Postfix(WearNTear __instance)
    {
        if (__instance.GetComponent<WarCrumbleState>() == null)
            __instance.gameObject.AddComponent<WarCrumbleState>();
    }
}

internal static class WarSpikeHooks
{
    static string S(Dictionary<string, object?> a, string key) =>
        a.TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";

    static float F(Dictionary<string, object?> a, string key, float fallback) =>
        float.TryParse(S(a, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;

    static bool B(Dictionary<string, object?> a, string key, bool fallback) =>
        a.TryGetValue(key, out var v) && v != null ? Convert.ToBoolean(v, CultureInfo.InvariantCulture) : fallback;

    static bool _patched;

    /// <summary>Switch the prototype on and set its numbers. Patches are applied once, the flags decide whether they act.</summary>
    [TestHook("spike.enable")]
    static string Enable(Dictionary<string, object?> args)
    {
        if (!_patched)
        {
            var h = new Harmony("drakemods.DrakesServerTeams.warspike");
            foreach (var t in new[] { typeof(WarApplyDamagePatch), typeof(WarDestroyPatch), typeof(WarRepairPatch), typeof(WarCrumbleAwakePatch) })
                h.CreateClassProcessor(t).Patch();
            _patched = true;
        }
        WarCrumble.Crumble = B(args, "crumble", true);
        WarCrumble.Shield = B(args, "shield", true);
        WarCrumble.ShieldMax = F(args, "shieldMax", 300f);
        WarCrumble.ShieldFactor = F(args, "factor", 1f);
        WarCrumble.DownSeconds = F(args, "down", 60f);
        WarCrumble.RepairCooldown = F(args, "cooldown", 20f);
        WarCrumble.RepairFraction = F(args, "fraction", 0.3f);
        return $"crumble={WarCrumble.Crumble} shield={WarCrumble.Shield} max={WarCrumble.ShieldMax}";
    }

    [TestHook("spike.disable")]
    static string Disable()
    {
        WarCrumble.Crumble = false;
        WarCrumble.Shield = false;
        return "off";
    }

    static WearNTear Wnt(GameObject? go) =>
        (go != null ? go.GetComponentInParent<WearNTear>() ?? go.GetComponentInChildren<WearNTear>() : null) ?? throw new InvalidOperationException("no WearNTear");

    /// <summary>The ward's shield: "value/max" and "down" once at zero.</summary>
    [TestHook("spike.shield")]
    static string ShieldOf(GameObject? ward)
    {
        var area = ward != null ? ward.GetComponentInParent<PrivateArea>() ?? ward.GetComponentInChildren<PrivateArea>() : null;
        var zdo = area != null ? WarCrumble.WardZdo(area) : null;
        if (zdo == null)
            return "no ward";
        var v = WarCrumble.ShieldValue(zdo);
        return string.Format(CultureInfo.InvariantCulture, "{0:0}/{1:0}{2}", v, WarCrumble.ShieldMax, v <= 0f ? " DOWN" : "");
    }

    [TestHook("spike.isCrumbled")]
    static bool IsCrumbled(GameObject? piece) => WarCrumble.IsCrumbled(Wnt(piece));

    /// <summary>Is the piece solid? (any of its colliders enabled)</summary>
    [TestHook("spike.solid")]
    static bool Solid(GameObject? piece)
    {
        foreach (var c in WarCrumble.ColliderList(Wnt(piece)))
            if (c != null && !c.isTrigger && c.enabled)
                return true;
        return false;
    }

    /// <summary>Repair as the local player (play as someone first). Returns the outcome message.</summary>
    [TestHook("spike.repair")]
    static string Repair(GameObject? piece)
    {
        WarSpikeMessages.Last = "";
        var ok = Wnt(piece).Repair();
        return (ok ? "ok" : "refused") + ":" + WarSpikeMessages.Last;
    }
}
#endif
