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

        _log.LogInfo($"Loaded {_order.Count} team(s) and {_members.Count} stored player(s).");
        Purge();
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
