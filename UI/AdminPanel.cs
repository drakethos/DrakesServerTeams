using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// The in-game admin panel: <c>teams panel</c> in the console, or the panel key (config, default F6). Everything in it is also a console
/// command, and the server checks admin rights on every change.
/// </summary>
public sealed class AdminPanel : MonoBehaviour
{
    const int WindowId = 0x7EA5;
    static AdminPanel? _instance;
    Rect _window = new(40f, 40f, 760f, 700f);
    Vector2 _scroll;
    GUIStyle? _title;
    GUIStyle? _solid;

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

    public static void Toggle() => IsOpen = !IsOpen;

    void Awake() => _instance = this;

    void Update()
    {
        if (TeamsConfig.PanelKey.IsDown() && !ChatOrConsoleOpen())
            Toggle();
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
        _window.width = Mathf.Clamp(_window.width, 520f, Screen.width - 10f);
        _window.height = Mathf.Clamp(_window.height, 300f, Screen.height - 10f);
        _solid ??= MakeWindowStyle();
        var oldContent = GUI.contentColor;
        GUI.contentColor = Color.white;
        _window = GUILayout.Window(WindowId, _window, Draw, "Server Teams - admin", _solid, GUILayout.MinWidth(520f), GUILayout.MinHeight(300f));
        GUI.contentColor = oldContent;
    }

    void Draw(int id)
    {
        _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold, fontSize = 14 };
        _scroll = GUILayout.BeginScrollView(_scroll);
        try
        {
            TeamsPanelUi.Draw(
                h => { GUILayout.Space(8f); GUILayout.Label(h, _title); },
                t => GUILayout.Label($"<color=#8f939c>{t}</color>", new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 11 }),
                lab: false);
        }
        catch (ArgumentException)
        {
            // IMGUI layout/repaint mismatch for one frame: harmless
        }
        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", GUILayout.Width(80f)))
            IsOpen = false;
        GUILayout.EndHorizontal();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }
}

/// <summary>While the panel is open the game must not take movement and attack input or grab the mouse.</summary>
[HarmonyPatch(typeof(Player), "TakeInput")]
static class PanelTakeInputPatch
{
    static void Postfix(ref bool __result)
    {
        if (AdminPanel.IsOpen || WardPanel.IsOpen || TeamPicker.IsOpen)
            __result = false;
    }
}

[HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
static class PanelMouseCapturePatch
{
    static bool Prefix() => !AdminPanel.IsOpen && !WardPanel.IsOpen && !TeamPicker.IsOpen;
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
