using HarmonyLib;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// A character with a team that has a spawn point appears there instead of at the world start. A bed and the
/// logout point still win (they are the player's own choice); this only replaces the default.
/// The client knows its team after the first hello, so a brand-new character's very first spawn is still the default.
/// </summary>
[HarmonyPatch(typeof(Game), nameof(Game.FindSpawnPoint))]
static class TeamSpawnPatch
{
    static void Postfix(ref Vector3 point, ref bool usedLogoutPoint, ref bool __result)
    {
        if (usedLogoutPoint)
            return;
        var player = Game.instance?.GetPlayerProfile();
        if (player == null || player.HaveCustomSpawnPoint())
            return;
        var spawn = TeamsAPI.GetSpawnPoint(player.GetPlayerID());
        if (spawn == null)
            return;
        point = spawn.Value;
        __result = true;
    }
}
