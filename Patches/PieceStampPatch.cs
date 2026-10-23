using HarmonyLib;

namespace DrakesServerTeams;

/// <summary>
/// Stamps the placing player's team id on a new piece, next to the creator ID the game already stores.
/// Lets other mods ask "is this player on the same team as whoever built it" without any character IDs:
/// compare the player's team with the stamp. A piece keeps the team it was built under.
/// Patched only when a mod that uses it is installed (see <see cref="DrakesServerTeamsPlugin"/>).
/// </summary>
[HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
static class PieceStampPatch
{
    static void Postfix(Piece __instance, long uid)
    {
        var local = Player.m_localPlayer;
        if (local == null || uid == 0 || uid != local.GetPlayerID())
            return;
        var team = TeamsClientState.OwnTeamId;
        if (string.IsNullOrEmpty(team))
            return;
        var nview = __instance.GetComponent<ZNetView>();
        if (nview == null || !nview.IsValid() || !nview.IsOwner())
            return;
        nview.GetZDO().Set(TeamsAPI.CreatorTeamZdoKey, team);
    }
}
