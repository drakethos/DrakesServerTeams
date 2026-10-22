using BepInEx;
using DrakeModsLibs.Compat;
using HarmonyLib;
using Jotunn;
using Jotunn.Managers;
using Jotunn.Utils;

namespace DrakesServerTeams;

// GUID / ModName / Version come from the generated ModInfo.g.cs (ModInfoClass in mod.package.props),
// same as CustomizeLibsPlugin in DrakeModsLibs.
[BepInPlugin(GUID, ModName, Version)]
[BepInDependency("com.drakemods.libs")]
[BepInDependency(Main.ModGuid)]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
public partial class DrakesServerTeamsPlugin : BaseUnityPlugin
{
    const float FlushIntervalSeconds = 30f;
    const float PurgeIntervalSeconds = 6 * 3600f;

    readonly Harmony _harmony = new("drakemods.DrakesServerTeams");
    float _nextFlush;
    float _nextPurge;

    void Awake()
    {
        TeamsConfig.Bind(Config, GUID, ModName, Version, Logger);
        TeamsNet.Init(this, Logger);
        CommandManager.Instance.AddConsoleCommand(new TeamsCommand());

        _harmony.PatchAll(typeof(PlayerSpawnPatch));

        // One compat host per plugin GUID (Libs rule). Phase 2 registers the LockSmith / RenameIt modules here.
        CompatHosts.GetOrCreate(GUID, Logger).Initialize(_harmony);

        Logger.LogInfo($"{ModName} {Version} loaded.");
    }

    // Server housekeeping only: nothing runs on a plain client.
    void Update()
    {
        if (!TeamsNet.IsAuthority)
            return;
        TeamRegistry.Instance.EnsureLoaded();
        var now = UnityEngine.Time.unscaledTime;
        if (now >= _nextFlush)
        {
            _nextFlush = now + FlushIntervalSeconds;
            TeamRegistry.Instance.FlushIfDirty();
        }
        if (now >= _nextPurge)
        {
            _nextPurge = now + PurgeIntervalSeconds;
            TeamRegistry.Instance.Purge();
        }
    }

    void OnDestroy()
    {
        if (TeamsNet.IsAuthority)
            TeamRegistry.Instance.FlushIfDirty();
    }
}
