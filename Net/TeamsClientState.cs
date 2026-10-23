using System;
using System.Collections.Generic;

namespace DrakesServerTeams;

/// <summary>
/// What this client knows: its own team and the team display data. No character IDs, no other players.
/// </summary>
public static class TeamsClientState
{
    public static bool Ready { get; private set; }
    /// <summary>The local character the state belongs to (our own id; never another player's).</summary>
    public static long PlayerId { get; private set; }
    public static string? OwnTeamId { get; private set; }
    public static AssignmentMode Mode { get; private set; }
    /// <summary>True when this player may pick or switch teams right now.</summary>
    public static bool CanSwitch { get; private set; }
    public static IReadOnlyList<TeamDefinition> Teams => _teams;

    static readonly List<TeamDefinition> _teams = new();

    public static event Action<string?, string?>? OwnTeamChanged;

    public static TeamDefinition? GetTeam(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        foreach (var t in _teams)
            if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
                return t;
        return null;
    }

    public static void Reset()
    {
        Ready = false;
        PlayerId = 0;
        OwnTeamId = null;
        CanSwitch = false;
        _teams.Clear();
    }

    public static void Apply(long playerId, string? ownTeamId, AssignmentMode mode, bool canSwitch, List<TeamDefinition> teams)
    {
        var old = OwnTeamId;
        Ready = true;
        PlayerId = playerId;
        OwnTeamId = string.IsNullOrEmpty(ownTeamId) ? null : ownTeamId;
        Mode = mode;
        CanSwitch = canSwitch;
        _teams.Clear();
        _teams.AddRange(teams);
        if (!string.Equals(old, OwnTeamId, StringComparison.OrdinalIgnoreCase))
            OwnTeamChanged?.Invoke(old, OwnTeamId);
    }
}
