using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>What a ward access check is being asked for. The caller sets it for the duration of its own check.</summary>
public enum WardKind
{
    /// <summary>Anything else (stations, beds, item stands ...): protected like a normal ward.</summary>
    Other,
    Build,
    Chest,
    Door,
}

/// <summary>
/// The Team Ward: a ward built at runtime from vanilla guard_stone (no art shipped). Obsidian look, glows in its team's color,
/// shows the team above it. Always on. Members of its team have access; everyone else is blocked from building and chests by
/// default, doors are a switch. Settings come from the server config, per ward overrides on the ward itself.
/// </summary>
public static class TeamWard
{
    public const string PrefabName = "TeamWard";
    public const string Display = "Team Ward";

    // Per ward overrides, stored on the ward's ZDO. 0 = use the global setting, 1 = on, 2 = off.
    public static readonly int BuildKey = "DrakesServerTeams_wardBuild".GetStableHashCode();
    public static readonly int ChestKey = "DrakesServerTeams_wardChests".GetStableHashCode();
    public static readonly int DoorKey = "DrakesServerTeams_wardDoors".GetStableHashCode();

    static readonly FieldInfo? AllAreas = AccessTools.Field(typeof(PrivateArea), "m_allAreas");
    static readonly FieldInfo? AreaView = AccessTools.Field(typeof(PrivateArea), "m_nview");

    /// <summary>Test tooling: pretend the local player is (not) an admin. Null = ask the game.</summary>
    internal static bool? AdminOverride;

    /// <summary>What the ward check running right now is for. Set by the patches around building, chests and doors.</summary>
    [ThreadStatic] internal static WardKind Kind;
    [ThreadStatic] static int _depth;

    internal static void Enter(WardKind kind)
    {
        if (_depth++ == 0)
            Kind = kind;
    }

    internal static void Leave()
    {
        if (_depth > 0 && --_depth == 0)
            Kind = WardKind.Other;
    }

    public static bool IsTeamWard(PrivateArea? area) =>
        area != null && area.gameObject.name.StartsWith(PrefabName, StringComparison.Ordinal);

    public static ZDO? Zdo(PrivateArea area) => (AreaView?.GetValue(area) as ZNetView)?.GetZDO();

    /// <summary>The team the ward was built under (server-signed stamp), or null for a neutral ward.</summary>
    public static string? TeamOf(PrivateArea area) => PieceTeams.ReadTeam(Zdo(area));

    public static bool LocalIsAdmin()
    {
        if (AdminOverride != null)
            return AdminOverride.Value;
        if (ZNet.instance == null)
            return false;
        if (ZNet.instance.IsServer() && !ZNet.instance.IsDedicated())
            return true; // the host
        return SynchronizationManager.Instance != null && SynchronizationManager.Instance.PlayerIsAdmin;
    }

    /// <summary>Is the protection for this kind on? Per ward value first, then the server's global setting.</summary>
    public static bool Protects(PrivateArea area, WardKind kind)
    {
        var zdo = Zdo(area);
        switch (kind)
        {
            case WardKind.Build:
                return Resolve(zdo, BuildKey, TeamsConfig.WardProtectBuild);
            case WardKind.Chest:
                return Resolve(zdo, ChestKey, TeamsConfig.WardProtectChests);
            case WardKind.Door:
                return Resolve(zdo, DoorKey, TeamsConfig.WardProtectDoors);
            default:
                return true;
        }
    }

    static bool Resolve(ZDO? zdo, int key, bool global)
    {
        var v = zdo?.GetInt(key, 0) ?? 0;
        return v == 1 ? true : v == 2 ? false : global;
    }

    /// <summary>
    /// Does the player get through this ward for the check in progress? Team members always; everyone else only for kinds the ward
    /// does not protect. (The vanilla creator and permitted-list rules are applied by the game before this.)
    /// </summary>
    public static bool Permits(PrivateArea area, long playerId)
    {
        var local = Player.m_localPlayer;
        if (local != null && local.GetPlayerID() == playerId && LocalIsAdmin())
            return true;
        var team = TeamOf(area);
        if (team != null && string.Equals(TeamsAPI.GetTeamId(playerId), team, StringComparison.OrdinalIgnoreCase))
            return true;
        return !Protects(area, Kind);
    }

    /// <summary>One line on what the ward protects: "build, chests" (doors are open) ...</summary>
    public static string Summary(PrivateArea area)
    {
        var parts = new List<string>();
        if (Protects(area, WardKind.Build)) parts.Add("build");
        if (Protects(area, WardKind.Chest)) parts.Add("chests");
        if (Protects(area, WardKind.Door)) parts.Add("doors");
        return parts.Count == 0 ? "nothing is protected from outsiders" : "outsiders are kept out of " + string.Join(", ", parts);
    }

    /// <summary>Every Team Ward currently loaded in the world.</summary>
    public static IEnumerable<PrivateArea> Loaded()
    {
        if (AllAreas?.GetValue(null) is not List<PrivateArea> areas)
            yield break;
        foreach (var a in areas)
            if (IsTeamWard(a))
                yield return a;
    }

    public static PrivateArea? Nearest(Vector3 point, float within)
    {
        PrivateArea? best = null;
        var bestD = within;
        foreach (var a in Loaded())
        {
            var d = Vector3.Distance(a.transform.position, point);
            if (d <= bestD)
            {
                bestD = d;
                best = a;
            }
        }
        return best;
    }

    // ---- the piece ---------------------------------------------------------------------------------------

    static bool _registered;

    /// <summary>Register the piece with Jotunn once the vanilla prefabs exist.</summary>
    internal static void Register()
    {
        PrefabManager.OnVanillaPrefabsAvailable += RegisterPiece;
    }

    static void RegisterPiece()
    {
        if (_registered)
            return;
        _registered = true;
        PrefabManager.OnVanillaPrefabsAvailable -= RegisterPiece;
        try
        {
            var clone = PrefabManager.Instance.CreateClonedPrefab(PrefabName, "guard_stone");
            var piece = clone.GetComponent<Piece>();
            piece.m_name = "$piece_drakesteamward";
            piece.m_description = "$piece_drakesteamward_desc";
            var area = clone.GetComponent<PrivateArea>();
            area.m_name = Display;
            area.m_enabledByDefault = true;
            area.m_radius = TeamsConfig.TeamWardRadius;
            clone.AddComponent<TeamWardVisual>();

            LocalizationManager.Instance.GetLocalization().AddTranslation("English", new Dictionary<string, string>
            {
                ["piece_drakesteamward"] = Display,
                ["piece_drakesteamward_desc"] = "A ward that belongs to a team. Its team is free to build and use what is inside; others are kept out.",
            });

            PieceManager.Instance.AddPiece(new CustomPiece(clone, false, new PieceConfig
            {
                PieceTable = "Hammer",
                Category = "Misc",
                Requirements = new RequirementConfig[0],
            }));
        }
        catch (Exception ex)
        {
            TeamsNet.Warn($"Team Ward could not be registered: {ex.Message}");
        }
    }
}
