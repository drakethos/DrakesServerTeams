using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>Team Wards are always on; nobody can switch them off.</summary>
[HarmonyPatch(typeof(PrivateArea), "IsEnabled")]
static class WardEnabledPatch
{
    static void Postfix(PrivateArea __instance, ref bool __result)
    {
        if (!__result && TeamWard.IsTeamWard(__instance))
            __result = true;
    }
}

/// <summary>
/// Access: team members get through, everyone else is stopped for the kinds the ward protects. The game's own creator and
/// permitted-list rules still run first, so the player who built the ward is never locked out.
/// </summary>
[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.IsPermitted))]
static class WardPermittedPatch
{
    static void Postfix(PrivateArea __instance, long playerID, ref bool __result)
    {
        if (!__result && TeamWard.IsTeamWard(__instance))
            __result = TeamWard.Permits(__instance, playerID);
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.HaveLocalAccess))]
static class WardLocalAccessPatch
{
    static void Postfix(PrivateArea __instance, ref bool __result)
    {
        if (__result || !TeamWard.IsTeamWard(__instance) || Player.m_localPlayer == null)
            return;
        __result = TeamWard.Permits(__instance, Player.m_localPlayer.GetPlayerID());
    }
}

/// <summary>
/// E on a Team Ward shows who it belongs to and what it protects. Shift+E opens its settings window (admins; anyone else is told why not).
/// The vanilla ward would toggle itself or ask for a permitted list, which does not apply here.
/// </summary>
[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.Interact))]
static class WardInteractPatch
{
    static bool Prefix(PrivateArea __instance, bool __1, bool __2, ref bool __result)
    {
        if (!TeamWard.IsTeamWard(__instance))
            return true;
        __result = true;
        if (__1)
            return false; // holding the key repeats; act once
        if (__2)
        {
            if (TeamWard.LocalIsAdmin())
                WardPanel.Open(__instance);
            else
                TeamsNet.Say("Only admins can change a Team Ward.");
            return false;
        }
        var team = TeamsAPI.GetTeam(TeamWard.TeamOf(__instance));
        TeamsNet.Say(team != null ? $"{team.DisplayName} Team Ward: {TeamWard.Summary(__instance)}" : $"Team Ward with no team: {TeamWard.Summary(__instance)}");
        return false;
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.GetHoverText))]
static class WardHoverPatch
{
    static void Postfix(PrivateArea __instance, ref string __result)
    {
        if (!TeamWard.IsTeamWard(__instance))
            return;
        var team = TeamsAPI.GetTeam(TeamWard.TeamOf(__instance));
        var line = team != null ? $"<color={team.Color}>{team.DisplayName}</color>" : "no team";
        var own = Player.m_localPlayer != null && team != null
                  && string.Equals(TeamsAPI.GetTeamId(Player.m_localPlayer), team.Id, StringComparison.OrdinalIgnoreCase);
        __result = $"{TeamWard.Display}\n{line}\n{(own || TeamWard.LocalIsAdmin() ? "your team's ward" : "protected")}"
                   + (TeamWard.LocalIsAdmin() ? "\n[<color=yellow><b>Shift+E</b></color>] settings" : "");
    }
}

// ---- which kind of check is running ----------------------------------------------------------------------------------

/// <summary>Building, removing and repairing: set the context while the game checks the ward.</summary>
[HarmonyPatch]
static class WardBuildContextPatch
{
    static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in new[] { "PlacePiece", "RemovePiece", "Repair", "UpdatePlacementGhost" })
        {
            var m = AccessTools.Method(typeof(Player), name);
            if (m != null)
                yield return m;
        }
    }

    static void Prefix() => TeamWard.Enter(WardKind.Build);

    static Exception? Finalizer(Exception? __exception)
    {
        TeamWard.Leave();
        return __exception;
    }
}

[HarmonyPatch]
static class WardDoorContextPatch
{
    static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in new[] { "Interact", "UseItem", "GetHoverText" })
        {
            var m = AccessTools.Method(typeof(Door), name);
            if (m != null)
                yield return m;
        }
    }

    static void Prefix() => TeamWard.Enter(WardKind.Door);

    static Exception? Finalizer(Exception? __exception)
    {
        TeamWard.Leave();
        return __exception;
    }
}

[HarmonyPatch]
static class WardChestContextPatch
{
    static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in new[] { "Interact", "CheckAccess", "GetHoverText", "UseItem" })
        {
            var m = AccessTools.Method(typeof(Container), name);
            if (m != null)
                yield return m;
        }
    }

    static void Prefix() => TeamWard.Enter(WardKind.Chest);

    static Exception? Finalizer(Exception? __exception)
    {
        TeamWard.Leave();
        return __exception;
    }
}

// ---- only admins place it -----------------------------------------------------------------------------------------

/// <summary>The local player tries to put a Team Ward down: refused unless an admin (when the server says admin only).</summary>
[HarmonyPatch(typeof(Player), "PlacePiece")]
static class WardPlacePatch
{
    // PlacePiece returns nothing in the live game, so a refusal is just skipping it.
    static bool Prefix(Piece piece)
    {
        if (piece == null || !piece.gameObject.name.StartsWith(TeamWard.PrefabName, StringComparison.Ordinal))
            return true;
        if (!TeamsConfig.TeamWardAdminOnly || TeamWard.LocalIsAdmin())
            return true;
        TeamsNet.Say("Only admins can place a Team Ward.");
        return false;
    }
}
