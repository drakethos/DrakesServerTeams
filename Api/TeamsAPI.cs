using System;
using System.Collections.Generic;

namespace DrakesServerTeams;

/// <summary>
/// Public surface other mods (LockSmith, RenameIt, ...) call. Keep it small and stable.
/// Keyed on the character's player ID (long), never a platform/Steam ID.
/// <para>
/// The server (dedicated server or host) is the only authority. On the authority every method answers for
/// any player. On a plain client only the local player's own team is known: other players read as
/// unassigned, and <see cref="AreSameTeam"/> is false unless both are the local player.
/// </para>
/// </summary>
public static class TeamsAPI
{
    public static bool IsLoaded() => true;

    /// <summary>True on the dedicated server or the host, where team data lives.</summary>
    public static bool IsAuthority => TeamsNet.IsAuthority;

    /// <summary>Team id of the player, or null when unassigned (or unknown on this client).</summary>
    public static string? GetTeamId(long playerId)
    {
        if (IsAuthority)
            return TeamRegistry.Instance.GetTeamId(playerId);
        return IsLocalPlayerId(playerId) ? TeamsClientState.OwnTeamId : null;
    }

    public static string? GetTeamId(Player? player) =>
        player == null ? null : GetTeamId(player.GetPlayerID());

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

    public static bool AreSameTeam(Player? a, Player? b) =>
        a != null && b != null && AreSameTeam(a.GetPlayerID(), b.GetPlayerID());

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
