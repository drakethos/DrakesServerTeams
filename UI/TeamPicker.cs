using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// The first-join team picker: a small window listing the joinable teams. The server decides when to show it (PlayerChoice, a character with
/// no team that has not been offered it yet) and remembers that it did; "teams pick" opens it again. Joining sends the same request as
/// "teams join", so the server still applies every rule.
/// </summary>
public sealed class TeamPicker : MonoBehaviour
{
    const int WindowId = 0x7EA7;
    static bool _open;
    Rect _window;
    GUIStyle? _solid;
    GUIStyle? _title;
    GUIStyle? _text;

    public static bool IsOpen => _open;

    public static void Open() => _open = true;

    public static void Close() => _open = false;

    void Update()
    {
        if (!_open)
            return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void OnGUI()
    {
        if (!_open || Player.m_localPlayer == null)
            return;
        _solid ??= AdminPanel.MakeWindowStyle();
        _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold, fontSize = 18, alignment = TextAnchor.MiddleCenter };
        _text ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 13, alignment = TextAnchor.MiddleCenter };
        var width = 460f;
        if (_window.width < 1f)
            _window = new Rect((Screen.width - width) / 2f, Screen.height * 0.22f, width, 100f);
        var old = GUI.contentColor;
        GUI.contentColor = Color.white;
        _window = GUILayout.Window(WindowId, _window, Draw, "", _solid, GUILayout.Width(width));
        GUI.contentColor = old;
    }

    void Draw(int id)
    {
        GUILayout.Label("Choose your team", _title);
        GUILayout.Space(4f);
        GUILayout.Label("This server has teams. Pick the side you want to play on. Only an admin can move you afterwards.", _text);
        GUILayout.Space(10f);
        var any = false;
        foreach (var team in TeamsClientState.Teams)
        {
            if (!team.Joinable)
                continue;
            any = true;
            GUILayout.BeginHorizontal();
            var icon = TeamIcons.Get(team.Icon);
            var tint = TeamIcons.IsTinted(team.Icon) ? TeamIcons.ParseColor(team.Color) : Color.white;
            var rect = GUILayoutUtility.GetRect(34f, 34f, GUILayout.Width(34f), GUILayout.Height(34f));
            var oldColor = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, icon.texture, ScaleMode.ScaleToFit, true);
            GUI.color = oldColor;
            var id2 = team.Id;
            if (GUILayout.Button($"<b><color={team.Color}>\u25A0</color> {team.DisplayName}</b>", GUILayout.Height(34f)))
                TeamsNet.RequestChoose(id2);
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }
        if (!any)
            GUILayout.Label("No team can be joined right now. Ask an admin.", _text);
        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Later (type 'teams pick' to open this again)", GUILayout.Width(300f)))
            _open = false;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
    }
}
