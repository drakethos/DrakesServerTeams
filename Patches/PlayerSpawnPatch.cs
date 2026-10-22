using HarmonyLib;

namespace DrakesServerTeams;

/// <summary>The local player's character exists: tell the server, which answers with team state.</summary>
[HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
static class PlayerSpawnPatch
{
    static void Postfix(Player __instance)
    {
        if (__instance == Player.m_localPlayer)
            TeamsNet.OnLocalPlayerSpawned();
    }
}
