using System;
using System.Collections.Generic;

namespace DrakesServerTeams;

/// <summary>
/// Public surface other mods (LockSmith, RenameIt, ...) call. Keep it small and stable.
/// Keyed on the character's player ID (long), never a platform/Steam ID.
/// <para>
/// The server (dedicated server or host) is the only authority. On the authority every method answers for
/// any player. On a plain client only the local player's own team is known: other players read as
/// unassigned by ID. The <see cref="Player"/> overloads also work on clients for any visible player, using
/// the team each player publishes on its ZDO (a display hint that a modified client can fake).
/// </para>
/// </summary>
public static class TeamsAPI
{
    public static bool IsLoaded() => true;

    /// <summary>ZDO key (string team id) the owning client writes on its own player. Read it through <see cref="GetTeamId(Player)"/>.</summary>
    public static readonly int TeamZdoKey = "DrakesServerTeams_team".GetStableHashCode();

    /// <summary>True on the dedicated server or the host, where team data lives.</summary>
    public static bool IsAuthority => TeamsNet.IsAuthority;

    /// <summary>Team id of the player, or null when unassigned (or unknown on this client).</summary>
    public static string? GetTeamId(long playerId)
    {
        if (IsAuthority)
            return TeamRegistry.Instance.GetTeamId(playerId);
        return IsLocalPlayerId(playerId) ? TeamsClientState.OwnTeamId : null;
    }

    /// <summary>ZDO key (string team id) stamped on a piece when it is placed. Read it through <see cref="GetCreatorTeamId"/>.</summary>
    public static readonly int CreatorTeamZdoKey = "DrakesServerTeams_creatorTeam".GetStableHashCode();

    /// <summary>The team the piece's builder was on when they placed it, or null (none, or built before Teams).</summary>
    public static string? GetCreatorTeamId(ZNetView? nview)
    {
        if (nview == null || !nview.IsValid())
            return null;
        var id = nview.GetZDO().GetString(CreatorTeamZdoKey, "");
        return string.IsNullOrEmpty(id) ? null : id;
    }

    /// <summary>
    /// True when <paramref name="player"/> is on the team the piece was built under. Needs no character IDs and
    /// works on any client. Display/hint grade: a modified client can fake its team or a stamp on its own pieces.
    /// </summary>
    public static bool IsSameTeamAsCreator(Player? player, ZNetView? piece)
    {
        var stamp = GetCreatorTeamId(piece);
        var mine = GetTeamId(player);
        return stamp != null && mine != null && string.Equals(stamp, mine, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Team of a player object. The authority answers from its registry. A client reads the team the
    /// player published on its ZDO (so it works for any visible player), which is a display hint, not proof.
    /// </summary>
    public static string? GetTeamId(Player? player)
    {
        if (player == null)
            return null;
        if (IsAuthority)
            return GetTeamId(player.GetPlayerID());
        if (player == Player.m_localPlayer)
            return TeamsClientState.OwnTeamId;
        var id = player.GetComponent<ZNetView>()?.GetZDO()?.GetString(TeamZdoKey, "");
        return string.IsNullOrEmpty(id) ? null : id;
    }

    public static TeamDefinition? GetTeam(string? teamId) =>
        IsAuthority ? TeamRegistry.Instance.GetTeam(teamId) : TeamsClientState.GetTeam(teamId);

    public static IReadOnlyCollection<TeamDefinition> GetTeams() =>
        IsAuthority ? TeamRegistry.Instance.Teams : TeamsClientState.Teams;

    /// <summary>True only when both players are assigned and on the same team.</summary>
    public static bool AreSameTeam(long a, long b)
    {
        var ta = GetTeamId(a);
        return ta != null && string.Equals(ta, GetTeamId(b), StringComparison.OrdinalIgnoreCase);
    }

    public static bool AreSameTeam(Player? a, Player? b)
    {
        var ta = GetTeamId(a);
        return ta != null && string.Equals(ta, GetTeamId(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Server-side assignment for other mods and tools. Ignores Joinable and Locked (those limit what a
    /// player may do to themself). Returns false off the authority or when the team is unknown.
    /// </summary>
    public static bool TryAssign(long playerId, string teamId) =>
        IsAuthority && TeamRegistry.Instance.TryAssign(playerId, teamId, by: "API");

    /// <summary>Deletes the stored entry for a character (GDPR erase). Authority only.</summary>
    public static bool Forget(long playerId) =>
        IsAuthority && TeamRegistry.Instance.Forget(playerId, by: "API");

    public delegate void TeamChanged(long playerId, string? oldTeamId, string? newTeamId);

    /// <summary>Raised on the authority whenever a character's team changes (including removal and purge).</summary>
    public static void RegisterOnTeamChanged(TeamChanged callback) => TeamRegistry.Instance.Changed += callback;

    /// <summary>Raised on a client when the local player's own team changes.</summary>
    public static void RegisterOnOwnTeamChanged(Action<string?, string?> callback) => TeamsClientState.OwnTeamChanged += callback;

    static bool IsLocalPlayerId(long playerId) =>
        Player.m_localPlayer != null && playerId != 0 && Player.m_localPlayer.GetPlayerID() == playerId;
}
