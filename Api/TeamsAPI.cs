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

    /// <summary>
    /// Team id of the player, or null when unassigned (or unknown on this client). A client knows its own team and
    /// the published team of players it can see (kept honest by the server, see <see cref="GetTeamId(Player)"/>).
    /// </summary>
    public static string? GetTeamId(long playerId)
    {
        if (IsAuthority)
            return TeamRegistry.Instance.GetTeamId(playerId);
        if (playerId == 0)
            return null;
        if (IsLocalPlayerId(playerId))
            return TeamsClientState.PlayerId == playerId ? TeamsClientState.OwnTeamId : null;
        foreach (var p in Player.GetAllPlayers())
            if (p != null && p.GetPlayerID() == playerId)
                return GetTeamId(p);
        return null;
    }

    /// <summary>
    /// Team of a player object. The authority answers from its registry. A client reads the team the
    /// player published on its ZDO (so it works for any visible player). The owning client writes that value and the
    /// server overwrites it with the registry's answer every few seconds, so a faked team does not last.
    /// </summary>
    public static string? GetTeamId(Player? player)
    {
        if (player == null)
            return null;
        if (IsAuthority)
            return GetTeamId(player.GetPlayerID());
        if (player == Player.m_localPlayer)
            return TeamsClientState.PlayerId == player.GetPlayerID() ? TeamsClientState.OwnTeamId : null;
        var id = player.GetComponent<ZNetView>()?.GetZDO()?.GetString(TeamZdoKey, "");
        return string.IsNullOrEmpty(id) ? null : id;
    }

    public static TeamDefinition? GetTeam(string? teamId) =>
        IsAuthority ? TeamRegistry.Instance.GetTeam(teamId) : TeamsClientState.GetTeam(teamId);

    public static IReadOnlyCollection<TeamDefinition> GetTeams() =>
        IsAuthority ? TeamRegistry.Instance.Teams : TeamsClientState.Teams;

    /// <summary>
    /// Where this player spawns for their team (respawn without a bed), or null when they have no team or the team has
    /// no spawn point. The authority answers for anyone; a client only for itself.
    /// </summary>
    public static UnityEngine.Vector3? GetSpawnPoint(long playerId)
    {
        if (IsAuthority)
            return TeamRegistry.Instance.GetSpawnPoint(playerId);
        if (!IsLocalPlayerId(playerId) || TeamsClientState.PlayerId != playerId)
            return null;
        var team = TeamsClientState.GetTeam(TeamsClientState.OwnTeamId);
        return team != null && team.HasSpawn ? team.GetSpawnPoint() : null;
    }

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

    // ---- piece teams (wards, chests, doors) ------------------------------------------------------------

    /// <summary>ZDO key (string) holding a piece's team stamp. Read it through <see cref="GetPieceTeamId(ZNetView)"/>.</summary>
    public static readonly int PieceTeamZdoKey = PieceTeams.StampKey;

    /// <summary>
    /// Call once at startup (from a soft-dependency class) to turn on piece teams: wards, chests and doors are stamped
    /// by the server with the team they were built under. Off until some mod asks for it.
    /// </summary>
    public static void UsePieceTeams() => DrakesServerTeamsPlugin.EnablePieceTeams();

    public static bool PieceTeamsActive => PieceTeams.Active;

    /// <summary>The team a piece was built under, or null (no team, or not stamped yet).</summary>
    public static string? GetPieceTeamId(ZNetView? view) =>
        view != null && view.IsValid() ? PieceTeams.ReadTeam(view.GetZDO()) : null;

    /// <summary>True when the player is on the team the piece was built under.</summary>
    public static bool IsOnPieceTeam(long playerId, ZNetView? view)
    {
        var pieceTeam = GetPieceTeamId(view);
        return pieceTeam != null && string.Equals(pieceTeam, GetTeamId(playerId), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOnPieceTeam(Player? player, ZNetView? view)
    {
        var pieceTeam = GetPieceTeamId(view);
        return pieceTeam != null && string.Equals(pieceTeam, GetTeamId(player), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Add a section to the Team Ward settings window (Shift+E on a Team Ward, admins). The callback draws with IMGUI <c>GUILayout</c> for the ward
    /// being edited. Used by extensions such as the War mod (capture flags and times, shield).
    /// </summary>
    public static void RegisterTeamWardSection(string title, System.Action<PrivateArea> draw) => AdminPanel.AddSection(title, draw);

    /// <summary>True when the object is a Team Ward.</summary>
    public static bool IsTeamWard(PrivateArea? area) => TeamWard.IsTeamWard(area);

    /// <summary>The team a Team Ward belongs to (server-signed), or null.</summary>
    public static string? GetTeamWardTeam(PrivateArea area) => TeamWard.TeamOf(area);

    public delegate void TeamChanged(long playerId, string? oldTeamId, string? newTeamId);

    /// <summary>Raised on the authority whenever a character's team changes (including removal and purge).</summary>
    public static void RegisterOnTeamChanged(TeamChanged callback) => TeamRegistry.Instance.Changed += callback;

    /// <summary>Raised on a client when the local player's own team changes.</summary>
    public static void RegisterOnOwnTeamChanged(Action<string?, string?> callback) => TeamsClientState.OwnTeamChanged += callback;

    static bool IsLocalPlayerId(long playerId) =>
        Player.m_localPlayer != null && playerId != 0 && Player.m_localPlayer.GetPlayerID() == playerId;
}
