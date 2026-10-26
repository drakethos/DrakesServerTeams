using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// Optional copy of the game's start stones at a team's spawn point (the team's <see cref="TeamDefinition.SpawnStones"/>).
/// The original start location stays where the world generator put it and stays the true spawn for everyone without a team
/// (and for teams with no spawn point); copies are only ever added, never moved or removed.
/// The world itself is the record: a team's stones exist when a start location is already within <see cref="NearRadius"/> of its
/// spawn point, so a restart or a fresh world needs no file.
/// </summary>
public static class SpawnStones
{
    /// <summary>Name of the start location in the game's location list.</summary>
    public const string LocationName = "StartTemple";
    const float NearRadius = 12f;
    const float TickSeconds = 5f;

    /// <summary>The game's own list of placed locations (m_locationInstances): any StartTemple within a short walk of the point.</summary>
    static bool NearInstance(Vector3 point)
    {
        var zs = ZoneSystem.instance;
        if (zs == null || AccessTools.Field(typeof(ZoneSystem), "m_locationInstances")?.GetValue(zs) is not System.Collections.IDictionary dict)
            return false;
        foreach (System.Collections.DictionaryEntry e in dict)
        {
            var inst = e.Value;
            if (inst == null)
                continue;
            var loc = AccessTools.Field(inst.GetType(), "m_location")?.GetValue(inst);
            var name = loc != null ? AccessTools.Field(loc.GetType(), "m_prefabName")?.GetValue(loc) as string : null;
            var pos = AccessTools.Field(inst.GetType(), "m_position")?.GetValue(inst);
            if (name != null && pos is Vector3 p && name.IndexOf(LocationName, StringComparison.OrdinalIgnoreCase) >= 0
                && Vector2.Distance(new Vector2(p.x, p.z), new Vector2(point.x, point.z)) <= NearRadius)
                return true;
        }
        return false;
    }

    /// <summary>Where the game actually put the last copy (it may adjust the spot).</summary>
    public static Vector3 LastPlacedAt { get; private set; }

    static float _next;
    static ManualLogSource? _log;
    // Where this session already placed stones for a team. Never place a second copy for the same point, even if the world check
    // cannot see the first one (it only sees loaded zones).
    static readonly System.Collections.Generic.Dictionary<string, Vector3> Placed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Server: place stones for every team that wants them and does not have them yet. Cheap when nothing is pending.</summary>
    public static void Tick(ManualLogSource log)
    {
        if (Time.unscaledTime < _next || !TeamsNet.IsAuthority || ZoneSystem.instance == null)
            return;
        _next = Time.unscaledTime + TickSeconds;
        _log = log;
        foreach (var team in TeamRegistry.Instance.Teams)
        {
            if (team.SpawnStones && team.HasSpawn)
                Ensure(team);
        }
    }

    /// <summary>
    /// Try to make sure the team has its stones. Returns "exists", "placed", or why not: "no spawn point", "zone not loaded yet" ...
    /// A zone that is not loaded (nobody is near) is not an error: the next tick tries again.
    /// </summary>
    public static string Ensure(TeamDefinition team)
    {
        if (!team.HasSpawn)
            return "no spawn point";
        var zs = ZoneSystem.instance;
        if (zs == null)
            return "world not ready";
        if (Placed.TryGetValue(team.Id, out var done) && (done - team.GetSpawnPoint()).sqrMagnitude < 1f)
            return "exists";
        if (HasStartLocationNear(team.GetSpawnPoint()))
            return "exists";

        var pos = team.GetSpawnPoint();
        pos.y = zs.GetGroundHeight(pos);
        try
        {
            if (!zs.IsZoneLoaded(pos))
                return "zone not loaded yet";
            var args = new object?[] { LocationName, pos, null };
            var method = AccessTools.Method(typeof(ZoneSystem), "SpawnLocationMidGame");
            if (method == null)
                return "this game version cannot place locations";
            if (method.Invoke(zs, args) is true)
            {
                Placed[team.Id] = team.GetSpawnPoint();
                // the game picks the exact spot itself and reports it back
                LastPlacedAt = args[2] is Vector3 spawned ? spawned : Vector3.zero;
                // The game puts the stones on the nearest valid ground, which can be a few meters away. The team spawns where they ended up.
                if (LastPlacedAt != Vector3.zero)
                {
                    TeamRegistry.Instance.SetTeamSpawn(team.Id, LastPlacedAt);
                    Placed[team.Id] = team.GetSpawnPoint();
                }
                _log?.LogInfo($"Placed start stones for team {team.DisplayName} at {LastPlacedAt} (asked for {pos}).");
                return "placed";
            }
            return "the game would not place the stones here";
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"Start stones for team {team.DisplayName}: {ex.Message}");
            return "failed: " + ex.Message;
        }
    }

    /// <summary>
    /// Is any start location (the original or a copy) within a short walk of the point? Looks at the locations loaded in the world:
    /// ZoneSystem.FindClosestLocation only knows locations the world generator registered, not ones placed mid-game.
    /// </summary>
    public static bool HasStartLocationNear(Vector3 point)
    {
        if (NearInstance(point))
            return true;
        var all = AccessTools.Field(typeof(Location), "s_allLocations")?.GetValue(null) as System.Collections.IEnumerable;
        if (all == null)
            return false;
        foreach (var item in all)
        {
            if (item is not Location loc || loc == null)
                continue;
            if (loc.gameObject.name.IndexOf(LocationName, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var p = loc.transform.position;
            if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(point.x, point.z)) <= NearRadius)
                return true;
        }
        return false;
    }
}
