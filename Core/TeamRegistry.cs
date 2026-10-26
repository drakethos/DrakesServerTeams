using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;

namespace DrakesServerTeams;

/// <summary>
/// Server-side source of truth: team definitions plus (character ID -> team ID).
/// Only the authority (dedicated server or host) loads and uses this; clients never hold it.
/// Stored per player: character ID, team ID, last-seen date. Nothing else.
/// </summary>
public sealed class TeamRegistry
{
    public static readonly TeamRegistry Instance = new();

    const string DateFormat = "yyyy-MM-dd";

    readonly Dictionary<string, TeamDefinition> _teams = new(StringComparer.OrdinalIgnoreCase);
    readonly List<TeamDefinition> _order = new();
    readonly Dictionary<long, MemberEntry> _members = new();
    ManualLogSource _log = Logger.CreateLogSource("DrakesServerTeams.Registry");
    bool _loaded;
    bool _dirty;

    public event TeamsAPI.TeamChanged? Changed;

    /// <summary>A team definition changed (spawn point): connected players need the new team list.</summary>
    public event Action? DefinitionsChanged;

    public IReadOnlyCollection<TeamDefinition> Teams
    {
        get { EnsureLoaded(); return _order; }
    }

    public TeamDefinition? GetTeam(string? id)
    {
        EnsureLoaded();
        return !string.IsNullOrEmpty(id) && _teams.TryGetValue(id!, out var t) ? t : null;
    }

    public string? GetTeamId(long playerId)
    {
        EnsureLoaded();
        return _members.TryGetValue(playerId, out var m) && !string.IsNullOrEmpty(m.teamId) ? m.teamId : null;
    }

    public bool HasEntry(long playerId)
    {
        EnsureLoaded();
        return _members.ContainsKey(playerId);
    }

    /// <summary>Has this character already been shown the first-join team picker?</summary>
    public bool WasOffered(long playerId)
    {
        EnsureLoaded();
        return _members.TryGetValue(playerId, out var m) && m.offered;
    }

    /// <summary>Write the "picker shown" flag (creates a team-less entry for a brand-new character). Saved at once.</summary>
    public void SetOffered(long playerId, bool offered)
    {
        EnsureLoaded();
        if (!_members.TryGetValue(playerId, out var entry))
        {
            if (!offered)
                return;
            _members[playerId] = entry = new MemberEntry { characterId = playerId, lastSeen = Today() };
        }
        if (entry.offered == offered)
            return;
        entry.offered = offered;
        _dirty = true;
        FlushIfDirty();
    }

    public int MemberCount(string teamId)
    {
        EnsureLoaded();
        var n = 0;
        foreach (var m in _members.Values)
            if (string.Equals(m.teamId, teamId, StringComparison.OrdinalIgnoreCase))
                n++;
        return n;
    }

    public void SetLog(ManualLogSource log) => _log = log;

    public void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;

        foreach (var t in TeamStore.LoadTeams(_log).teams)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.Id))
                continue;
            t.Id = t.Id.Trim();
            if (_teams.ContainsKey(t.Id))
            {
                _log.LogWarning($"teams.json lists team '{t.Id}' twice; the first entry wins.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(t.DisplayName))
                t.DisplayName = t.Id;
            t.Icon = TeamIcons.Normalize(t.Icon);
            if (TeamIcons.TryParseColor(t.Color, out var parsed))
                t.Color = TeamIcons.ToHex(parsed);
            if (!string.IsNullOrWhiteSpace(t.GlowColor) && TeamIcons.TryParseColor(t.GlowColor, out var glow))
                t.GlowColor = TeamIcons.ToHex(glow);
            _teams[t.Id] = t;
            _order.Add(t);
        }

        foreach (var m in TeamStore.LoadMembers(_log).members)
        {
            if (m == null || m.characterId == 0)
                continue;
            // A team removed from teams.json leaves its members unassigned instead of pointing at nothing.
            if (!string.IsNullOrEmpty(m.teamId) && !_teams.ContainsKey(m.teamId))
                m.teamId = "";
            _members[m.characterId] = m;
        }

        WarnSharedSpawns();
        _log.LogInfo($"Loaded {_order.Count} team(s) and {_members.Count} stored player(s).");
        if (_order.Count == 0)
            _log.LogWarning($"No teams defined. Add teams to {TeamStore.TeamsPath} and restart the server.");
        Purge();
    }

    /// <summary>Teams whose spawn point is within 1 m of an earlier team's. Each team should have its own.</summary>
    public List<string> SharedSpawns()
    {
        EnsureLoaded();
        var found = new List<string>();
        for (var i = 0; i < _order.Count; i++)
        for (var j = 0; j < i; j++)
            if (_order[i].HasSpawn && _order[j].HasSpawn
                && (_order[i].GetSpawnPoint() - _order[j].GetSpawnPoint()).sqrMagnitude < 1f)
                found.Add($"{_order[j].Id}+{_order[i].Id}");
        return found;
    }

    void WarnSharedSpawns()
    {
        foreach (var pair in SharedSpawns())
            _log.LogWarning($"Teams {pair} have the same spawn point. Give each team its own in teams.json.");
    }

    /// <summary>Admin: give a team a spawn point, or remove it (null). Saved to teams.json and pushed to clients.</summary>
    public bool SetTeamSpawn(string teamId, UnityEngine.Vector3? point)
    {
        var team = GetTeam(teamId);
        if (team == null)
            return false;
        if (point == null)
            team.HasSpawn = false;
        else
            team.SetSpawn(point.Value);
        SaveTeamsFile();
        WarnSharedSpawns();
        DefinitionsChanged?.Invoke();
        return true;
    }

    /// <summary>Admin: change a team's faction icon (an unknown name falls back to the default). Saved and pushed to clients.</summary>
    public bool SetTeamIcon(string teamId, string icon)
    {
        var team = GetTeam(teamId);
        if (team == null)
            return false;
        team.Icon = TeamIcons.Normalize(icon);
        SaveTeamsFile();
        DefinitionsChanged?.Invoke();
        return true;
    }

    /// <summary>Admin: turn the team's own start stones on or off. Saved to teams.json. Stones already placed in the world stay.</summary>
    public bool SetTeamSpawnStones(string teamId, bool on)
    {
        var team = GetTeam(teamId);
        if (team == null)
            return false;
        team.SpawnStones = on;
        SaveTeamsFile();
        return true;
    }

    /// <summary>Admin: set a team's color ("#RRGGBB", "#RGB" or a name). Stored as #RRGGBB. False for an unknown team or a bad color.</summary>
    public bool SetTeamColor(string teamId, string color)
    {
        var team = GetTeam(teamId);
        if (team == null || !TeamIcons.TryParseColor(color, out var c))
            return false;
        team.Color = TeamIcons.ToHex(c);
        SaveTeamsFile();
        DefinitionsChanged?.Invoke();
        return true;
    }

    /// <summary>Admin: set the glow color of a team ("#RRGGBB"), or "" to use the team color. Returns false for an unknown team or a bad color.</summary>
    public bool SetTeamGlow(string teamId, string color)
    {
        var team = GetTeam(teamId);
        if (team == null)
            return false;
        var c = (color ?? "").Trim();
        if (c.Length > 0 && !TeamIcons.TryParseColor(c, out _))
            return false;
        team.GlowColor = c;
        SaveTeamsFile();
        DefinitionsChanged?.Invoke();
        return true;
    }

    readonly HashSet<string> _runtime = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Write teams.json, leaving out teams that only exist for a test run.</summary>
    void SaveTeamsFile()
    {
        var file = new TeamsFile();
        foreach (var t in _order)
            if (!_runtime.Contains(t.Id))
                file.teams.Add(t);
        TeamStore.SaveTeams(file, _log);
    }

    /// <summary>Test tooling: add a team at runtime (not written to teams.json).</summary>
    internal TeamDefinition DefineTeam(TeamDefinition team)
    {
        EnsureLoaded();
        if (_teams.TryGetValue(team.Id, out var existing))
            _order.Remove(existing);
        _teams[team.Id] = team;
        _order.Add(team);
        _runtime.Add(team.Id);
        return team;
    }

    /// <summary>Test tooling: remove a team; its members become unassigned.</summary>
    internal bool RemoveTeam(string id)
    {
        EnsureLoaded();
        if (!_teams.TryGetValue(id, out var team))
            return false;
        _teams.Remove(id);
        _order.Remove(team);
        _runtime.Remove(id);
        foreach (var m in _members.Values)
            if (string.Equals(m.teamId, id, StringComparison.OrdinalIgnoreCase))
                m.teamId = "";
        return true;
    }

    /// <summary>Test tooling: characters that have a stored entry.</summary>
    internal List<long> MemberIds()
    {
        EnsureLoaded();
        return new List<long>(_members.Keys);
    }

    /// <summary>Spawn point of the team the character is on, or null (no team, or the team has none).</summary>
    public UnityEngine.Vector3? GetSpawnPoint(long playerId)
    {
        var team = GetTeam(GetTeamId(playerId));
        return team != null && team.HasSpawn ? team.GetSpawnPoint() : null;
    }

    /// <summary>Server-side assignment (admin command, server API). Ignores Joinable and Locked.</summary>
    public bool TryAssign(long playerId, string teamId, string? who = null, string? by = null)
    {
        EnsureLoaded();
        if (playerId == 0 || !_teams.TryGetValue(teamId, out var team))
            return false;
        Set(playerId, team.Id, who, by);
        return true;
    }

    /// <summary>A player moving themself. Returns false with a short reason for the player.</summary>
    public bool TrySelfAssign(long playerId, string teamId, string? who, out string reason)
    {
        EnsureLoaded();
        reason = "";
        if (TeamsConfig.AssignmentMode != AssignmentMode.PlayerChoice)
        {
            reason = "Players cannot pick teams on this server.";
            return false;
        }
        if (!_teams.TryGetValue(teamId, out var target))
        {
            reason = "Unknown team.";
            return false;
        }
        if (!target.Joinable)
        {
            reason = $"{target.DisplayName} cannot be joined.";
            return false;
        }
        var currentId = GetTeamId(playerId);
        if (string.Equals(currentId, target.Id, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"You are already on {target.DisplayName}.";
            return false;
        }
        if (currentId != null && !TeamsConfig.PlayersCanSwitchTeams)
        {
            reason = "Only an admin can move you to another team.";
            return false;
        }
        if (currentId != null && _teams.TryGetValue(currentId, out var current) && current.Locked)
        {
            reason = $"{current.DisplayName} is locked. Ask an admin to move you.";
            return false;
        }
        Set(playerId, target.Id, who, who);
        return true;
    }

    /// <summary>AutoBalance for a character that has no team yet. Returns the team id, or null.</summary>
    public string? TryAutoAssign(long playerId, string? who)
    {
        EnsureLoaded();
        if (GetTeamId(playerId) != null)
            return null;
        TeamDefinition? best = null;
        var bestCount = int.MaxValue;
        foreach (var t in _order)
        {
            if (!t.Joinable)
                continue;
            var n = MemberCount(t.Id);
            if (n < bestCount)
            {
                best = t;
                bestCount = n;
            }
        }
        if (best == null)
            return null;
        Set(playerId, best.Id, who, "auto-balance");
        return best.Id;
    }

    /// <summary>Delete the stored entry (admin "forget"). The player is unassigned afterwards.</summary>
    public bool Forget(long playerId, string? who = null, string? by = null)
    {
        EnsureLoaded();
        if (!_members.TryGetValue(playerId, out var entry))
            return false;
        _members.Remove(playerId);
        _dirty = true;
        Announce(playerId, entry.teamId, null, who, by);
        FlushIfDirty();
        return true;
    }

    public void Touch(long playerId)
    {
        EnsureLoaded();
        if (!_members.TryGetValue(playerId, out var entry))
            return;
        var today = Today();
        if (entry.lastSeen == today)
            return;
        entry.lastSeen = today;
        _dirty = true;
    }

    /// <summary>Delete entries whose character has not been seen for PurgeAfterDays.</summary>
    public int Purge()
    {
        var days = TeamsConfig.PurgeAfterDays;
        if (days <= 0)
            return 0;
        var cutoff = DateTime.UtcNow.Date.AddDays(-days);
        var stale = new List<long>();
        foreach (var m in _members.Values)
        {
            // Entries from before lastSeen existed count as seen today rather than being purged at once.
            if (DateTime.TryParseExact(m.lastSeen, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var seen)
                && seen < cutoff)
                stale.Add(m.characterId);
        }
        foreach (var id in stale)
        {
            var old = _members[id].teamId;
            _members.Remove(id);
            Announce(id, old, null, null, "purge");
        }
        if (stale.Count > 0)
        {
            _dirty = true;
            _log.LogInfo($"Purged {stale.Count} stored player(s) not seen for {days} days.");
            FlushIfDirty();
        }
        return stale.Count;
    }

    public void FlushIfDirty()
    {
        if (!_dirty)
            return;
        _dirty = false;
        var file = new MembersFile();
        file.members.AddRange(_members.Values);
        TeamStore.SaveMembers(file, _log);
    }

    void Set(long playerId, string teamId, string? who, string? by)
    {
        _members.TryGetValue(playerId, out var entry);
        var old = entry != null && !string.IsNullOrEmpty(entry.teamId) ? entry.teamId : null;
        if (string.Equals(old, teamId, StringComparison.OrdinalIgnoreCase))
        {
            Touch(playerId);
            return;
        }
        if (entry == null)
            _members[playerId] = entry = new MemberEntry { characterId = playerId };
        entry.teamId = teamId;
        entry.lastSeen = Today();
        _dirty = true;
        FlushIfDirty();
        Announce(playerId, old, teamId, who, by);
    }

    void Announce(long playerId, string? oldTeam, string? newTeam, string? who, string? by)
    {
        if (TeamsConfig.LogTeamChanges)
        {
            // Names only. Never the character ID.
            var from = GetTeam(oldTeam)?.DisplayName ?? "none";
            var to = GetTeam(newTeam)?.DisplayName ?? "none";
            _log.LogInfo($"Team change: {who ?? "a player"}: {from} -> {to}" + (by != null ? $" (by {by})" : ""));
        }
        try
        {
            Changed?.Invoke(playerId, string.IsNullOrEmpty(oldTeam) ? null : oldTeam, string.IsNullOrEmpty(newTeam) ? null : newTeam);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"A TeamChanged subscriber failed: {ex.Message}");
        }
    }

    static string Today() => DateTime.UtcNow.ToString(DateFormat, CultureInfo.InvariantCulture);
}
