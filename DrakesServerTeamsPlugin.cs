using BepInEx;
using HarmonyLib;
using Jotunn;
using Jotunn.Managers;
using Jotunn.Utils;

namespace DrakesServerTeams;

// GUID / ModName / Version come from the generated ModInfo.g.cs (ModInfoClass in mod.package.props),
// like the other DrakeMods plugins. No DrakeModsLibs dependency in Phase 1.
[BepInPlugin(GUID, ModName, Version)]
[BepInDependency(Main.ModGuid)]
[BepInDependency("com.drakemods.testlab", BepInDependency.DependencyFlags.SoftDependency)]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
public partial class DrakesServerTeamsPlugin : BaseUnityPlugin
{
    const float FlushIntervalSeconds = 30f;
    const float PurgeIntervalSeconds = 6 * 3600f;
    const float GuardIntervalSeconds = 5f;
    const float IdentityIntervalSeconds = 1f;

    static readonly Harmony _harmony = new("drakemods.DrakesServerTeams");
    internal static DrakesServerTeamsPlugin? Instance { get; private set; }
    float _nextFlush;
    float _nextPurge;
    float _nextGuard;
    float _nextIdentity;

    internal static void EnablePieceTeams() => PieceTeams.Activate(_harmony);

    void Awake()
    {
        Instance = this;
        TeamsConfig.Bind(Config);
        PieceTeams.Init(Logger);
        TeamWard.Register();
        TeamsNet.Init(this, Logger);
        CommandManager.Instance.AddConsoleCommand(new TeamsCommand());

        _harmony.PatchAll(typeof(PlayerSpawnPatch));
        _harmony.PatchAll(typeof(TeamSpawnPatch));
        foreach (var t in new[] { typeof(PlayerGlowPatch), typeof(NameplatePatch), typeof(GraveSetupPatch), typeof(GraveAwakePatch), typeof(GraveHoverPatch), typeof(CreateTombStonePatch), typeof(MoveToGravePatch),
            typeof(WardEnabledPatch), typeof(WardPermittedPatch), typeof(WardLocalAccessPatch), typeof(WardInteractPatch), typeof(WardHoverPatch),
            typeof(WardBuildContextPatch), typeof(WardDoorContextPatch), typeof(WardChestContextPatch), typeof(WardPlacePatch) })
            _harmony.PatchAll(t);

        gameObject.AddComponent<AdminPanel>();
        gameObject.AddComponent<WardPanel>();
        gameObject.AddComponent<TeamPicker>();
        _harmony.PatchAll(typeof(PanelTakeInputPatch));
        _harmony.PatchAll(typeof(PanelMouseCapturePatch));
        TestLabBridge.TryRegister();

        // The Team Ward needs the server-signed team on pieces.
        EnablePieceTeams();

        Logger.LogInfo($"{ModName} {Version} loaded.");
    }

    // A plain client only checks that its own character has not changed; the rest is server housekeeping.
    void Update()
    {
        var now = UnityEngine.Time.unscaledTime;
        if (now >= _nextIdentity)
        {
            _nextIdentity = now + IdentityIntervalSeconds;
            TeamsNet.CheckLocalIdentity();
        }
        if (!TeamsNet.IsAuthority)
            return;
        TeamRegistry.Instance.EnsureLoaded();
        DeathRulesState.EnsureLoadedOnServer(Logger);
        PieceTeams.Tick(this);
        SpawnStones.Tick(Logger);
        if (now >= _nextGuard)
        {
            _nextGuard = now + GuardIntervalSeconds;
            TeamsNet.GuardPublishedTeams();
        }
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
