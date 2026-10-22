using BepInEx.Configuration;
using BepInEx.Logging;
using DrakeModsLibs.API;
using DrakeModsLibs.Sync;

namespace DrakesServerTeams;

/// <summary>
/// Synced settings go through DrakeConfigSync (Libs owns ServerSync). Server-side settings are bound
/// without sync: only the machine that stores the files reads them.
/// </summary>
public static class TeamsConfig
{
    const int SyncedEntryCount = 2;

    static DrakeConfigSync _sync = null!;
    static ConfigEntry<bool> _lockSyncedConfig = null!;
    static ConfigEntry<AssignmentMode> _assignmentMode = null!;
    static ConfigEntry<int> _purgeAfterDays = null!;
    static ConfigEntry<bool> _logTeamChanges = null!;

    public static bool LockSyncedConfig => _lockSyncedConfig.Value;
    public static AssignmentMode AssignmentMode => _assignmentMode.Value;
    /// <summary>Server only. Entries not seen for this many days are deleted. 0 keeps them forever.</summary>
    public static int PurgeAfterDays => _purgeAfterDays.Value;
    /// <summary>Server only. Log team changes (team names and character name, never IDs).</summary>
    public static bool LogTeamChanges => _logTeamChanges.Value;

    public static void Bind(ConfigFile config, string modId, string displayName, string version, ManualLogSource log)
    {
        _sync = CustomizeLibsAPI.CreateConfigSync(modId, displayName, version);

        _lockSyncedConfig = _sync.BindSynced(config, "1 General", "General", "LockSyncedConfig", true,
            "When true, only Valheim server admins can edit synced settings. Keep enabled on public servers.");
        _sync.AddLockingConfigEntry(_lockSyncedConfig);

        _assignmentMode = _sync.BindSynced(config, "2 Assignment", "Assignment", "AssignmentMode", AssignmentMode.AdminOnly,
            "How a character with no team gets one. AdminOnly: admin command or server API only. "
            + "PlayerChoice: the player picks a joinable team. AutoBalance: the server picks the joinable team with the fewest members.");

        _purgeAfterDays = _sync.BindClientOnly(config, "3 Privacy (server)", "Privacy", "PurgeAfterDays", 90,
            "Server only. Delete a stored player entry when that character has not joined for this many days. 0 = never.");
        _logTeamChanges = _sync.BindClientOnly(config, "3 Privacy (server)", "Privacy", "LogTeamChanges", true,
            "Server only. Write team changes to the log (team names and character name, never IDs).");

        _sync.FinalizeBinding(log, SyncedEntryCount, () => _lockSyncedConfig.Value);
    }
}
