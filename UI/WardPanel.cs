using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// The Team Ward's own settings window: an admin presses Shift+E on the ward. Team, what it protects, and any extra sections another mod
/// (the War extension: capture, shield) adds through <see cref="TeamsAPI.RegisterTeamWardSection"/>. Players who are not admins only get a message.
/// </summary>
public sealed class WardPanel : MonoBehaviour
{
    const int WindowId = 0x7EA6;
    static readonly List<(string title, Action<PrivateArea> draw)> Sections = new();
    static PrivateArea? _ward;
    Rect _window = new(120f, 120f, 640f, 420f);
    Vector2 _scroll;
    GUIStyle? _solid;
    GUIStyle? _title;

    public static bool IsOpen => _ward != null;

    /// <summary>The ward the window is showing (null = closed).</summary>
    internal static PrivateArea? Target => _ward;

    internal static void Open(PrivateArea ward) => _ward = ward;

    internal static void Close() => _ward = null;

    internal static void AddSection(string title, Action<PrivateArea> draw) => Sections.Add((title, draw));

    void Update()
    {
        if (_ward == null)
            return;
        if (_ward.Equals(null) || Player.m_localPlayer == null || Vector3.Distance(_ward.transform.position, Player.m_localPlayer.transform.position) > 14f)
        {
            _ward = null;
            return;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (Input.GetKeyDown(KeyCode.Escape))
            _ward = null;
    }

    void OnGUI()
    {
        if (_ward == null)
            return;
        _solid ??= AdminPanel.MakeWindowStyle();
        _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold, fontSize = 14 };
        var oldContent = GUI.contentColor;
        GUI.contentColor = Color.white;
        _window = GUILayout.Window(WindowId, _window, Draw, "Team Ward", _solid, GUILayout.MinWidth(560f), GUILayout.MinHeight(260f));
        GUI.contentColor = oldContent;
    }

    void Draw(int id)
    {
        var ward = _ward;
        var me = Player.m_localPlayer;
        if (ward == null || me == null)
            return;
        _scroll = GUILayout.BeginScrollView(_scroll);
        try
        {
            TeamsPanelUi.DrawWard(ward, TeamsAPI.GetTeams().ToList(), me);
            foreach (var (title, draw) in Sections)
            {
                GUILayout.Space(8f);
                GUILayout.Label(title, _title);
                draw(ward);
            }
        }
        catch (ArgumentException)
        {
            // IMGUI layout/repaint mismatch for one frame: harmless
        }
        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        GUILayout.Label("<color=#8f939c>Changes apply at once and are saved with the ward.</color>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11 });
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", GUILayout.Width(80f)))
            _ward = null;
        GUILayout.EndHorizontal();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }
}
