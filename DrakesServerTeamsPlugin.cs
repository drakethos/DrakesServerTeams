using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Jotunn;
using Jotunn.Managers;
using Jotunn.Utils;

namespace DrakesServerTeams;

// GUID / ModName / Version come from the generated ModInfo.g.cs (ModInfoClass in mod.package.props),
// like the other DrakeMods plugins. No DrakeModsLibs dependency in Phase 1.
[BepInPlugin(GUID, ModName, Version)]
[BepInDependency(Main.ModGuid)]
// Soft: only for load order, so the check in Awake can see whether these are installed.
[BepInDependency(LockSmithGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(RenameItGuid, BepInDependency.DependencyFlags.SoftDependency)]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
public partial class DrakesServerTeamsPlugin : BaseUnityPlugin
{
    const string LockSmithGuid = "com.drakesworkshop.locksmith";
    const string RenameItGuid = "com.drakesworkshop.renameit";
    const float FlushIntervalSeconds = 30f;
    const float PurgeIntervalSeconds = 6 * 3600f;

    readonly Harmony _harmony = new("drakemods.DrakesServerTeams");
    float _nextFlush;
    float _nextPurge;

    void Awake()
    {
        TeamsConfig.Bind(Config);
        TeamsNet.Init(this, Logger);
        CommandManager.Instance.AddConsoleCommand(new TeamsCommand());

        _harmony.PatchAll(typeof(PlayerSpawnPatch));

        // Dormant until used: the piece stamp exists for LockSmith / RenameIt, so only patch when one is installed.
        if (Chainloader.PluginInfos.ContainsKey(LockSmithGuid) || Chainloader.PluginInfos.ContainsKey(RenameItGuid))
        {
            _harmony.PatchAll(typeof(PieceStampPatch));
            Logger.LogInfo("A mod that uses team permissions is installed: piece team stamp on.");
        }

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
