using DrakeModsLibs.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace DrakesServerTeams;

/// <summary>
/// The first-join team picker: a window (Libs <see cref="UkWindow"/>, the DrakeMods look) listing the joinable teams. The server decides when to
/// show it (PlayerChoice, a character with no team that has not been offered it yet) and remembers that it did; "teams pick" opens it again.
/// Joining sends the same request as "teams join", so the server still applies every rule.
/// </summary>
public sealed class TeamPicker : MonoBehaviour
{
    static UkWindow? _win;
    static VisualElement? _list;
    static string _key = "";

    public static bool IsOpen => _win != null && _win.IsOpen;

    public static void Open()
    {
        EnsureWindow();
        Rebuild();
        _win!.Open();
    }

    public static void Close() => _win?.Close();

    static void EnsureWindow()
    {
        if (_win != null)
            return;
        _win = new UkWindow("drakes_team_picker", 720f);
        _win.SetHeader("Choose your team");
        var text = UkControls.Text("This server has teams. Pick the side you want to play on. Only an admin can move you afterwards.", 17, UkTheme.TextBody);
        text.style.marginBottom = 14;
        _win.Body.Add(text);
        _list = new VisualElement().Column();
        _win.Body.Add(_list);
        _win.ShowFooter(true);
        _win.Footer.Add(UkControls.Text("Type 'teams pick' to open this again.", 14, UkTheme.TextMuted, wrap: false));
        _win.Footer.Add(UkControls.Spacer());
        _win.Footer.Add(UkControls.MakeButton("Later", () => _win.Close(), UkButtonKind.Secondary));
        _win.Body.schedule.Execute(() =>
        {
            if (IsOpen && Key() != _key)
                Rebuild();
        }).Every(500);
    }

    static string Key()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in TeamsClientState.Teams)
            sb.Append(t.Id).Append(t.DisplayName).Append(t.Color).Append(t.Joinable).Append(t.Logo).Append(t.Icon).Append(';');
        return sb.ToString();
    }

    static void Rebuild()
    {
        _key = Key();
        _list!.Clear();
        var any = false;
        foreach (var team in TeamsClientState.Teams)
        {
            if (!team.Joinable)
                continue;
            any = true;
            var id = team.Id;
            var row = new VisualElement().Row();
            row.style.marginBottom = 10;
            var sprite = TeamLogos.GetSprite(team, out var tinted);
            var icon = new VisualElement { pickingMode = PickingMode.Ignore }.Size(60, 60).Fixed();
            icon.style.backgroundImage = new StyleBackground(sprite);
            icon.style.unityBackgroundImageTintColor = tinted ? TeamIcons.ParseColor(team.Color) : Color.white;
            icon.style.marginRight = 14;
            row.Add(icon);
            var button = UkControls.MakeButton($"<color={team.Color}>■</color> {team.DisplayName}", () => TeamsNet.RequestChoose(id), UkButtonKind.Secondary);
            button.enableRichText = true;
            button.style.height = 60;
            button.style.fontSize = 22;
            button.style.flexGrow = 1;
            row.Add(button);
            _list.Add(row);
        }
        if (!any)
            _list.Add(UkControls.Text("No team can be joined right now. Ask an admin.", 17, UkTheme.TextMuted));
    }
}
