using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// The in-game admin window: <c>teams panel</c> in the console, or the panel key (config, default F6). Tabs: Players, Teams, Logo, Ward, Server.
/// An admin pressing Shift+E on a Team Ward opens the same window on the Ward tab for that ward. Everything in it is also a console command,
/// and the server checks admin rights on every change. The window itself is <see cref="TeamsWindow"/> (the Libs DrakeMods look); this class keeps
/// the old entry points (key, open, close, extra ward sections) so callers and tests did not change.
/// </summary>
public sealed class AdminPanel : MonoBehaviour
{
    static readonly List<(string title, Action<PrivateArea> draw)> Sections = new();

    public static bool IsOpen => TeamsWindow.IsOpen;

    /// <summary>The tab showing (only meaningful while open).</summary>
    public static PanelTab Tab => TeamsWindow.Tab;

    /// <summary>The ward the Ward tab is editing (null = the nearest one).</summary>
    internal static PrivateArea? WardTarget => TeamsWindow.WardTarget;

    public static void Toggle()
    {
        if (TeamsWindow.IsOpen)
            TeamsWindow.Close();
        else
            TeamsWindow.Open();
    }

    /// <summary>Tests and tools: switch to a tab.</summary>
    internal static void SetTab(PanelTab tab) => TeamsWindow.SetTab(tab);

    /// <summary>Open on the Ward tab for this specific ward (Shift+E).</summary>
    internal static void OpenWard(PrivateArea ward) => TeamsWindow.Open(PanelTab.Ward, ward);

    internal static void Close() => TeamsWindow.Close();

    /// <summary>Extra Ward-tab sections from other mods (see <see cref="TeamsAPI.RegisterTeamWardSection"/>).</summary>
    internal static void AddSection(string title, Action<PrivateArea> draw) => Sections.Add((title, draw));

    internal static IReadOnlyList<(string title, Action<PrivateArea> draw)> SectionList => Sections;

    internal static void DrawWardSections(PrivateArea ward, Action<string> header)
    {
        foreach (var (title, draw) in Sections)
        {
            header(title);
            draw(ward);
        }
    }

    void Update()
    {
        if (TeamsConfig.PanelKey.IsDown() && !ChatOrConsoleOpen())
            Toggle();
        var ward = TeamsWindow.WardTarget;
        if (TeamsWindow.IsOpen && ward != null && (ward.Equals(null) || Player.m_localPlayer == null
                                                   || Vector3.Distance(ward.transform.position, Player.m_localPlayer.transform.position) > 16f))
            TeamsWindow.ForgetWard(); // walked away from (or lost) the ward: the Ward tab falls back to the nearest one
    }

    static bool ChatOrConsoleOpen()
    {
        try
        {
            return (Console.instance != null && Console.IsVisible()) || (Chat.instance != null && Chat.instance.HasFocus());
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>True while one of our windows owns the mouse and keyboard: the game must not read movement, look or attack input.</summary>
static class PanelInput
{
    public static bool Blocking => AdminPanel.IsOpen || TeamPicker.IsOpen || SharedWardMenu.IsOpen;
}

[HarmonyPatch(typeof(Player), "TakeInput")]
static class PanelTakeInputPatch
{
    static void Postfix(ref bool __result)
    {
        if (PanelInput.Blocking)
            __result = false;
    }
}

/// <summary>The attack, block and look keys are read through PlayerController: without this a click on a panel button also punches.</summary>
[HarmonyPatch]
static class PanelControllerTakeInputPatch
{
    static MethodBase? TargetMethod() => AccessTools.Method(typeof(PlayerController), "TakeInput");

    static bool Prepare() => TargetMethod() != null;

    static void Postfix(ref bool __result)
    {
        if (PanelInput.Blocking)
            __result = false;
    }
}

[HarmonyPatch]
static class PanelMouseCapturePatch
{
    static MethodBase? TargetMethod() => AccessTools.Method(typeof(GameCamera), "UpdateMouseCapture");

    static bool Prepare() => TargetMethod() != null;

    static void Postfix()
    {
        if (!PanelInput.Blocking)
            return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}

/// <summary>Scrolling a panel must not zoom the camera.</summary>
[HarmonyPatch]
static class PanelScrollPatch
{
    static MethodBase? TargetMethod() => AccessTools.Method(typeof(ZInput), "GetMouseScrollWheel");

    static bool Prepare() => TargetMethod() != null;

    static void Postfix(ref float __result)
    {
        if (PanelInput.Blocking)
            __result = 0f;
    }
}

/// <summary>Adds a "Teams" tab to DrakesTestLab's toolbox (F9) when that mod is installed. Found by name, so there is no reference to it.</summary>
static class TestLabBridge
{
    public static void TryRegister()
    {
        try
        {
            Type? type = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "DrakesTestLab")
                    continue;
                type = asm.GetType("DrakesTestLab.TestLabTabs");
                if (type != null)
                    break;
            }
            var register = type?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
            var header = type?.GetMethod("Header", BindingFlags.Public | BindingFlags.Static)?.CreateDelegate(typeof(Action<string>)) as Action<string>;
            var hint = type?.GetMethod("Hint", BindingFlags.Public | BindingFlags.Static)?.CreateDelegate(typeof(Action<string>)) as Action<string>;
            if (register == null || header == null || hint == null)
                return;
            register.Invoke(null, new object[] { "Teams", "Scenario", (Action)(() => TeamsPanelUi.Draw(header, hint, lab: true)) });
        }
        catch (Exception ex)
        {
            TeamsNet.Warn($"Could not add the Teams tab to DrakesTestLab: {ex.Message}");
        }
    }
}
