using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// The in-game admin panel: <c>teams panel</c> in the console, or the panel key (config, default F6). Tabs: Players, Teams, Ward, Server.
/// An admin pressing Shift+E on a Team Ward opens the same window on the Ward tab for that ward. Everything in it is also a console
/// command, and the server checks admin rights on every change.
/// </summary>
public sealed class AdminPanel : MonoBehaviour
{
    const int WindowId = 0x7EA5;
    static readonly List<(string title, Action<PrivateArea> draw)> Sections = new();
    static PanelTab _tab = PanelTab.Players;
    static PrivateArea? _ward;
    Rect _window = new(40f, 40f, 780f, 640f);
    Vector2 _scroll;
    GUIStyle? _title;
    GUIStyle? _solid;
    GUIStyle? _tabOn;

    internal static GUIStyle MakeWindowStyle()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, new Color(0.09f, 0.1f, 0.12f, 0.97f));
        tex.Apply();
        var style = new GUIStyle(GUI.skin.window) { padding = new RectOffset(10, 10, 24, 10) };
        style.normal.background = style.onNormal.background = tex;
        style.normal.textColor = style.onNormal.textColor = new Color(0.94f, 0.71f, 0.35f);
        return style;
    }

    public static bool IsOpen { get; private set; }

    /// <summary>The tab showing (only meaningful while open).</summary>
    public static PanelTab Tab => _tab;

    /// <summary>The ward the Ward tab is editing (null = the nearest one).</summary>
    internal static PrivateArea? WardTarget => _ward;

    public static void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
            _ward = null;
    }

    /// <summary>Open on the Ward tab for this specific ward (Shift+E).</summary>
    internal static void OpenWard(PrivateArea ward)
    {
        _ward = ward;
        _tab = PanelTab.Ward;
        IsOpen = true;
    }

    internal static void Close()
    {
        IsOpen = false;
        if (_ward != null)
            _tab = PanelTab.Players; // it was opened for one ward (Shift+E); the next F6 starts from the Players tab
        _ward = null;
    }

    /// <summary>Extra Ward-tab sections from other mods (see <see cref="TeamsAPI.RegisterTeamWardSection"/>).</summary>
    internal static void AddSection(string title, Action<PrivateArea> draw) => Sections.Add((title, draw));

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
        if (IsOpen && _ward != null && (_ward.Equals(null) || Player.m_localPlayer == null
                                         || Vector3.Distance(_ward.transform.position, Player.m_localPlayer.transform.position) > 16f))
            _ward = null; // walked away from (or lost) the ward: the Ward tab falls back to the nearest one
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
            Close();
        if (IsOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
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

    void OnGUI()
    {
        if (!IsOpen)
            return;
        _window.width = Mathf.Clamp(_window.width, 560f, Screen.width - 10f);
        _window.height = Mathf.Clamp(_window.height, 300f, Screen.height - 10f);
        _solid ??= MakeWindowStyle();
        var oldContent = GUI.contentColor;
        GUI.contentColor = Color.white;
        _window = GUILayout.Window(WindowId, _window, Draw, "Server Teams - admin", _solid, GUILayout.MinWidth(560f), GUILayout.MinHeight(300f));
        GUI.contentColor = oldContent;
    }

    void Draw(int id)
    {
        _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold, fontSize = 14 };
        _tabOn ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
        GUILayout.BeginHorizontal();
        foreach (PanelTab t in Enum.GetValues(typeof(PanelTab)))
        {
            if (GUILayout.Button(t.ToString(), t == _tab ? _tabOn : GUI.skin.button, GUILayout.Height(26f)))
            {
                _tab = t;
                _scroll = Vector2.zero;
                if (t != PanelTab.Ward)
                    _ward = null;
            }
        }
        GUILayout.EndHorizontal();
        _scroll = GUILayout.BeginScrollView(_scroll);
        try
        {
            TeamsPanelUi.Draw(
                h => { GUILayout.Space(8f); GUILayout.Label(h, _title); },
                t => GUILayout.Label($"<color=#8f939c>{t}</color>", new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 11 }),
                lab: false, only: _tab, wardTarget: _ward);
        }
        catch (ArgumentException)
        {
            // IMGUI layout/repaint mismatch for one frame: harmless
        }
        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        GUILayout.Label("<color=#8f939c>Changes apply at once and are saved.</color>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11 });
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", GUILayout.Width(80f)))
            Close();
        GUILayout.EndHorizontal();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }
}

/// <summary>True while one of our windows owns the mouse and keyboard: the game must not read movement, look or attack input.</summary>
static class PanelInput
{
    public static bool Blocking => AdminPanel.IsOpen || TeamPicker.IsOpen;
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
