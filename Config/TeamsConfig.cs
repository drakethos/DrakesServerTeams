using BepInEx.Configuration;
using Jotunn.Configs;

namespace DrakesServerTeams;

/// <summary>
/// Synced settings use Jotunn's own config sync (<c>IsAdminOnly</c>): the server's value reaches clients and only
/// admins can change it. Server-side settings are plain entries; only the machine that stores the files reads them.
/// </summary>
public static class TeamsConfig
{
    static ConfigEntry<AssignmentMode> _assignmentMode = null!;
    static ConfigEntry<int> _purgeAfterDays = null!;
    static ConfigEntry<bool> _logTeamChanges = null!;

    public static AssignmentMode AssignmentMode => _assignmentMode.Value;
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

        _purgeAfterDays = config.Bind("2 Privacy (server)", "PurgeAfterDays", 90,
            "Server only. Delete a stored player entry when that character has not joined for this many days. 0 = never.");
        _logTeamChanges = config.Bind("2 Privacy (server)", "LogTeamChanges", true,
            "Server only. Write team changes to the log (team names and character name, never IDs).");
    }
}
