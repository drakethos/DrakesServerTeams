using BepInEx.Configuration;
using UnityEngine;
using Jotunn.Configs;

namespace DrakesServerTeams;

/// <summary>
/// Synced settings use Jotunn's own config sync (<c>IsAdminOnly</c>): the server's value reaches clients and only
/// admins can change it. Server-side settings are plain entries; only the machine that stores the files reads them.
/// </summary>
public static class TeamsConfig
{
    static ConfigEntry<AssignmentMode> _assignmentMode = null!;
    static ConfigEntry<bool> _playersCanSwitch = null!;
    static ConfigEntry<bool> _moveToSpawn = null!;
    static ConfigEntry<bool> _stonesExperimental = null!;
    static ConfigEntry<bool> _firstJoinPicker = null!;
    static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> _panelKey = null!;
    static ConfigEntry<bool> _wardAdminOnly = null!;
    static ConfigEntry<bool> _wardProtectBuild = null!;
    static ConfigEntry<bool> _wardProtectChests = null!;
    static ConfigEntry<bool> _wardProtectDoors = null!;
    static ConfigEntry<float> _wardRadius = null!;
    static ConfigEntry<bool> _showNameColor = null!;
    static ConfigEntry<bool> _showIcon = null!;
    static ConfigEntry<bool> _showGlow = null!;
    static ConfigEntry<float> _glowRange = null!;
    static ConfigEntry<float> _glowIntensity = null!;
    static ConfigEntry<bool> _showGraveMarker = null!;
    static ConfigEntry<float> _stampSweepSeconds = null!;
    static ConfigEntry<int> _purgeAfterDays = null!;
    static ConfigEntry<bool> _logTeamChanges = null!;

    public static AssignmentMode AssignmentMode => _assignmentMode.Value;
    /// <summary>PlayerChoice only: may a player who already has a team move to another one themself?</summary>
    public static bool PlayersCanSwitchTeams => _playersCanSwitch.Value;
    /// <summary>Other players' names are drawn in their team color.</summary>
    public static bool ShowNameColor => _showNameColor.Value;
    /// <summary>The team icon floats above other players' names.</summary>
    public static bool ShowIcon => _showIcon.Value;
    /// <summary>A soft team-colored light around players.</summary>
    public static bool ShowGlow => _showGlow.Value;
    public static float GlowRange => _glowRange.Value;
    /// <summary>How bright the team glow is (light intensity).</summary>
    public static float GlowIntensity => _glowIntensity.Value;
    /// <summary>Graves show the team of the player who died (light, icon, name color, hover line).</summary>
    public static bool ShowGraveMarker => _showGraveMarker.Value;
    /// <summary>Opens the admin panel (client setting). The console command "teams panel" does the same.</summary>
    public static BepInEx.Configuration.KeyboardShortcut PanelKey => _panelKey.Value;

    /// <summary>Change who sets teams. An admin's change reaches the server through Jotunn's admin-only sync.</summary>
    public static void SetAssignmentMode(AssignmentMode mode) => _assignmentMode.Value = mode;

    /// <summary>Only admins can place a Team Ward.</summary>
    public static bool TeamWardAdminOnly => _wardAdminOnly.Value;
    /// <summary>Default for Team Wards: players outside the ward's team cannot place, remove or repair pieces inside it.</summary>
    public static bool WardProtectBuild => _wardProtectBuild.Value;
    /// <summary>Default: players outside the team cannot open chests inside the ward.</summary>
    public static bool WardProtectChests => _wardProtectChests.Value;
    /// <summary>Default: players outside the team cannot open doors inside the ward.</summary>
    public static bool WardProtectDoors => _wardProtectDoors.Value;
    public static float TeamWardRadius => _wardRadius.Value;
    /// <summary>PlayerChoice: a character that has no team is shown a picker window the first time it loads in.</summary>
    public static bool FirstJoinPicker => _firstJoinPicker.Value;
    /// <summary>Allow the experimental per-team start stones. Off by default: the game places them with a mid-game terrain edit that can leave seams.</summary>
    public static bool SpawnStonesExperimental => _stonesExperimental.Value;
    /// <summary>A character that gets its first team is moved to that team's spawn point (if it has one).</summary>
    public static bool MoveToSpawnOnFirstTeam => _moveToSpawn.Value;
    /// <summary>Server only. Minimum seconds between two passes that check every team-stamped piece.</summary>
    public static float StampSweepSeconds => _stampSweepSeconds.Value;
    /// <summary>Server only. Entries not seen for this many days are deleted. 0 keeps them forever.</summary>
    public static int PurgeAfterDays => _purgeAfterDays.Value;
    /// <summary>Server only. Log team changes (team names and character name, never IDs).</summary>
    public static bool LogTeamChanges => _logTeamChanges.Value;

    public static void Bind(ConfigFile config)
    {
        _assignmentMode = config.Bind("1 Assignment", "AssignmentMode", AssignmentMode.AdminOnly,
            new ConfigDescription(
                "How a character with no team gets one. AdminOnly: admin command or server API only. "
                + "PlayerChoice: the player picks a joinable team. AutoBalance: the server picks the joinable team with the fewest members.",
                null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
        _playersCanSwitch = config.Bind("1 Assignment", "PlayersCanSwitchTeams", false,
            new ConfigDescription(
                "PlayerChoice only. Off: a player picks a team once and after that only an admin (or the server API) moves them. "
                + "On: players may switch to another joinable team themselves (a Locked team still keeps them). "
                + "Pieces keep the team they were built under either way.",
                null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

        _firstJoinPicker = config.Bind("1 Assignment", "FirstJoinPicker", true,
            new ConfigDescription(
                "PlayerChoice only: a character with no team gets a window listing the joinable teams the first time it loads in. "
                + "The server remembers that it was shown (members.json), so it does not come back every login; 'teams join <team>' and 'teams pick' still work.",
                null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
        _stonesExperimental = config.Bind("7 Experimental", "SpawnStonesExperimental", false,
            new ConfigDescription(
                "EXPERIMENTAL, can damage a world. Allows 'teams spawnstones <team> on': a copy of the start stones at a team's spawn point. The game places it "
                + "with a mid-game terrain edit that can leave seams and height jumps between zones, and it is saved in the world. Leave this off on any world you care about.",
                null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
        _moveToSpawn = config.Bind("1 Assignment", "MoveToSpawnOnFirstTeam", true,
            new ConfigDescription(
                "On: a character that gets its first team is moved to that team's spawn point (when the team has one). "
                + "Off: it stays where it is, and the team spawn point only applies from the next respawn.",
                null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

        var admin = new ConfigurationManagerAttributes { IsAdminOnly = true };
        _showNameColor = config.Bind("4 Visuals", "TeamNameColor", true,
            new ConfigDescription("Names above other players are drawn in their team color.", null, admin));
        _showIcon = config.Bind("4 Visuals", "TeamIcon", true,
            new ConfigDescription("The team's faction icon (set per team in teams.json: shield, circle, diamond, star, cross) floats above other players' names.", null, admin));
        _showGlow = config.Bind("4 Visuals", "TeamGlow", true,
            new ConfigDescription("A soft light in the team color around every player who has a team.", null, admin));
        _glowRange = config.Bind("4 Visuals", "TeamGlowRange", 3.5f,
            new ConfigDescription("How far the team glow reaches (meters).", new AcceptableValueRange<float>(1f, 12f), admin));
        _glowIntensity = config.Bind("4 Visuals", "TeamGlowIntensity", 2f,
            new ConfigDescription("How bright the team glow is. Raise it for dark servers, lower it for a subtle tint.", new AcceptableValueRange<float>(0.2f, 6f), admin));
        _showGraveMarker = config.Bind("4 Visuals", "TeamGraveMarker", true,
            new ConfigDescription("A grave shows the team of the player who died: team-colored light, icon and name, and the team name when you look at it.", null, admin));

        _panelKey = config.Bind("6 Admin panel", "PanelKey", new BepInEx.Configuration.KeyboardShortcut(KeyCode.F6),
            "Key that opens the admin panel (also: the console command 'teams panel'). Only admins can change anything in it.");
        var wardAdmin = new ConfigurationManagerAttributes { IsAdminOnly = true };
        _wardAdminOnly = config.Bind("5 Team Ward", "TeamWardAdminOnly", true,
            new ConfigDescription("Only admins can place a Team Ward (the piece is in the Hammer's Misc tab).", null, wardAdmin));
        _wardProtectBuild = config.Bind("5 Team Ward", "ProtectBuild", true,
            new ConfigDescription("Default for every Team Ward: only its team can place, remove or repair pieces inside it. A single ward can override this (teams ward build on|off|inherit).", null, wardAdmin));
        _wardProtectChests = config.Bind("5 Team Ward", "ProtectChests", true,
            new ConfigDescription("Default: only the ward's team can open chests inside it. Per ward override: teams ward chests on|off|inherit.", null, wardAdmin));
        _wardProtectDoors = config.Bind("5 Team Ward", "ProtectDoors", false,
            new ConfigDescription("Default: only the ward's team can open doors inside it. Off by default so raiders can walk in through open doors while chests stay safe. Per ward override: teams ward doors on|off|inherit.", null, wardAdmin));
        _wardRadius = config.Bind("5 Team Ward", "TeamWardRadius", 32f,
            new ConfigDescription("How far a Team Ward reaches (meters). Read at startup; restart the game after changing it.", new AcceptableValueRange<float>(5f, 100f)));

        _stampSweepSeconds = config.Bind("3 Piece teams (server)", "StampSweepSeconds", 60f,
            new ConfigDescription(
                "Server only. Wards, chests and doors carry the team they were built under, written and signed by the server. "
                + "The server re-checks every one of them at most this often: it stamps pieces built before the mod and removes stamps it did not sign. "
                + "Only runs while a mod that uses piece teams (LockSmith) is installed.",
                new AcceptableValueRange<float>(10f, 3600f)));

        _purgeAfterDays = config.Bind("2 Privacy (server)", "PurgeAfterDays", 90,
            "Server only. Delete a stored player entry when that character has not joined for this many days. 0 = never.");
        _logTeamChanges = config.Bind("2 Privacy (server)", "LogTeamChanges", true,
            "Server only. Write team changes to the log (team names and character name, never IDs).");
    }
}
