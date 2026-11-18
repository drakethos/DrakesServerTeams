using System;
using System.Collections.Generic;
using System.Linq;
using DrakeModsLibs.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace DrakesServerTeams;

/// <summary>
/// The admin window, in the DrakeMods look (Libs <see cref="UkWindow"/>): Players, Teams, Logo, Ward and Server tabs. Bigger than the old
/// IMGUI panel. Every button sends the same command a console line would, so the server decides and checks admin rights; the window
/// adds no authority of its own. It refreshes itself twice a second while open (not the Logo tab's drafts, which are yours until applied).
/// </summary>
internal static class TeamsWindow
{
    const float WindowWidth = 1320f;
    const float ScrollHeight = 700f;

    static readonly string[] Palette = { "red", "orange", "gold", "green", "teal", "blue", "purple", "pink" };
    static readonly HashSet<string> Editing = new(StringComparer.OrdinalIgnoreCase);

    static UkWindow? _win;
    static UkSegmented? _tabs;
    static VisualElement? _content;
    static ScrollView? _scroll;
    static Label? _you;
    static VisualElement? _footerExtra;
    static Label? _footNote;
    static PanelTab _tab = PanelTab.Players;
    static PrivateArea? _ward;
    static string _key = "";

    public static bool IsOpen => _win != null && _win.IsOpen;

    public static PanelTab Tab => _tab;


    public static PrivateArea? WardTarget => _ward;

    // ---- open / close ----------------------------------------------------------------------------------------------

    public static void Open(PanelTab? tab = null, PrivateArea? ward = null)
    {
        EnsureWindow();
        if (tab != null)
            _tab = tab.Value;
        _ward = ward;
        _tabs!.SetIndex((int)_tab);
        Rebuild();
        _win!.Open();
    }

    public static void Close() => _win?.Close();

    /// <summary>Tests and tools: switch tab (the window need not be open).</summary>
    public static void SetTab(PanelTab tab)
    {
        _tab = tab;
        if (_win != null)
        {
            _tabs!.SetIndex((int)tab);
            if (_win.IsOpen)
                Rebuild();
        }
    }

    /// <summary>The ward the Ward tab was opened for is gone or too far: fall back to the nearest one.</summary>
    public static void ForgetWard()
    {
        if (_ward == null)
            return;
        _ward = null;
        if (IsOpen && _tab == PanelTab.Ward)
            Rebuild();
    }

    static void EnsureWindow()
    {
        if (_win != null)
            return;
        _win = new UkWindow("drakes_teams_admin", WindowWidth);
        _win.SetHeader("Server Teams", "Admin panel");
        _win.Closed = () =>
        {
            if (_ward != null)
                _tab = PanelTab.Players; // it was opened for one ward (Shift+E); the next F6 starts from the Players tab
            _ward = null;
        };

        _tabs = new UkSegmented(Enum.GetNames(typeof(PanelTab)));
        _tabs.Root.style.marginBottom = 10;
        _tabs.Changed = i =>
        {
            _tab = (PanelTab)i;
            if (_tab != PanelTab.Ward)
                _ward = null;
            Rebuild();
        };
        _win.Body.Add(_tabs.Root);

        _you = UkControls.Text("", 16, UkTheme.TextBody);
        _you.enableRichText = true;
        _you.style.marginBottom = 8;
        _win.Body.Add(_you);

        _scroll = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Hidden };
        _scroll.contentViewport.style.overflow = Overflow.Hidden; // without this scrolled content is drawn over the tabs above it
        _scroll.style.maxHeight = ScrollHeight;
        _scroll.style.flexShrink = 1;
        _content = _scroll.contentContainer;
        _win.Body.Add(_scroll);

        _win.ShowFooter(true);
        _footNote = UkControls.Text("Changes apply at once and are saved.", 14, UkTheme.TextMuted, wrap: false);
        _win.Footer.Add(_footNote);
        _win.Footer.Add(UkControls.Spacer());
        _footerExtra = new VisualElement().Row();
        _win.Footer.Add(_footerExtra);
        var close = UkControls.MakeButton("Close", () => _win.Close(), UkButtonKind.Secondary);
        close.style.marginLeft = 10;
        _win.Footer.Add(close);

        _win.Body.schedule.Execute(Tick).Every(400);
    }

    static void Tick()
    {
        if (!IsOpen)
            return;
        var key = StateKey();
        if (key != _key)
            Rebuild();
    }

    // ---- state: what makes the window rebuild itself -------------------------------------------------------------------

    static string StateKey()
    {
        var me = Player.m_localPlayer;
        var sb = new System.Text.StringBuilder();
        sb.Append(_tab).Append('|').Append(me != null).Append('|').Append(TeamWard.LocalIsAdmin()).Append('|');
        foreach (var t in TeamsAPI.GetTeams())
            sb.Append(t.Id).Append(t.DisplayName).Append(t.Color).Append(t.GlowColor).Append(t.Icon).Append(t.Logo).Append(t.HasSpawn).Append(t.SpawnX).Append(t.SpawnZ).Append(t.SpawnStones).Append(';');
        if (_tab == PanelTab.Logo)
            return sb.ToString(); // the designer's own draft is not part of the world state
        sb.Append('|');
        foreach (var p in Player.GetAllPlayers())
            if (p != null)
                sb.Append(p.GetPlayerName()).Append(TeamsAPI.GetTeamId(p)).Append(',');
        if (_tab == PanelTab.Ward)
        {
            var ward = _ward ?? (me != null ? TeamWard.Nearest(me.transform.position, 12f) : null);
            sb.Append('|').Append(ward != null);
            var zdo = ward != null ? TeamWard.Zdo(ward) : null;
            if (zdo != null)
                sb.Append(TeamWard.TeamOf(ward!)).Append(zdo.GetInt(TeamWard.BuildKey, 0)).Append(zdo.GetInt(TeamWard.ChestKey, 0)).Append(zdo.GetInt(TeamWard.DoorKey, 0))
                    .Append(zdo.GetInt(TeamWard.PowerKey, 0));
        }
        if (_tab == PanelTab.Server)
            sb.Append('|').Append(TeamsConfig.AssignmentMode).Append(DeathRulesState.Current.Enabled).Append(DeathRulesState.Current.Scope);
        return sb.ToString();
    }

    static void Rebuild()
    {
        if (_content == null)
            return;
        var scrollY = _scroll!.scrollOffset.y;
        _content.Clear();
        _footerExtra!.Clear();
        _footNote!.text = _tab == PanelTab.Logo ? "Logo edits are a preview until you press Apply to team." : "Changes apply at once and are saved.";
        var me = Player.m_localPlayer;
        var teams = TeamsAPI.GetTeams().ToList();
        if (me == null)
        {
            _you!.text = "No player yet.";
            _key = StateKey();
            return;
        }
        var admin = TeamWard.LocalIsAdmin();
        var own = TeamsAPI.GetTeam(TeamsAPI.GetTeamId(me));
        _you!.text = $"You: <b>{me.GetPlayerName()}</b>, team {(own != null ? Colored(own, own.DisplayName) : "none")}. "
                     + (admin ? "You are an admin." : "<color=#e0675c>You are not an admin: the server will refuse changes.</color>");
        if (teams.Count == 0)
        {
            _content.Add(Note("The server has no teams yet (teams.json)."));
            _key = StateKey();
            return;
        }

        switch (_tab)
        {
            case PanelTab.Players: BuildPlayers(_content, teams, me); break;
            case PanelTab.Teams: BuildTeams(_content, teams); break;
            case PanelTab.Logo: LogoTab.Build(_content, Rebuild, _footerExtra!); break;
            case PanelTab.Ward: BuildWard(_content, teams, me); break;
            case PanelTab.Server: BuildServer(_content); break;
        }
        _key = StateKey();
        _scroll.schedule.Execute(() => _scroll.scrollOffset = new Vector2(0f, scrollY));
    }

    // ---- small helpers -------------------------------------------------------------------------------------------------

    internal static void Cmd(string line) => TeamsPanelUi.Cmd(line);

    // Dark team colors (a deep blue, a purple) are hard to read on the dark panels: text is lightened, the swatch keeps the real color.
    static string Readable(string hex)
    {
        var c = Color.Lerp(TeamIcons.ParseColor(hex), Color.white, 0.4f);
        return "#" + ColorUtility.ToHtmlStringRGB(c);
    }

    internal static string Colored(TeamDefinition? team, string text) =>
        team == null ? text : $"<color={team.Color}>■</color> <color={Readable(team.Color)}>{text}</color>";

    internal static Button Btn(string text, Action action, UkButtonKind kind = UkButtonKind.Secondary, float? width = null)
    {
        var b = UkControls.MakeButton(text, action, kind, width);
        b.enableRichText = true;
        b.style.height = 42;
        b.style.fontSize = 16;
        b.style.marginRight = 6;
        b.style.marginBottom = 6;
        return b;
    }

    internal static VisualElement WrapRow()
    {
        var r = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center } };
        return r;
    }

    internal static Label Note(string text)
    {
        var l = UkControls.Text(text, 14, UkTheme.TextMuted);
        l.enableRichText = true;
        l.style.marginTop = 6;
        l.style.marginBottom = 6;
        return l;
    }

    internal static Label Heading(string text)
    {
        var l = UkControls.Text(text, 20, UkTheme.Gold, heading: true, wrap: false);
        l.style.marginTop = 14;
        l.style.marginBottom = 8;
        return l;
    }

    internal static Label Rich(string text, int size = 16)
    {
        var l = UkControls.Text(text, size, UkTheme.Text);
        l.enableRichText = true;
        return l;
    }

    static VisualElement Card()
    {
        var c = UkControls.Card();
        c.style.paddingTop = c.style.paddingBottom = 10;
        c.style.paddingLeft = c.style.paddingRight = 14;
        c.style.marginBottom = 8;
        return c;
    }

    static string TeamButtonText(TeamDefinition team) => $"<color={team.Color}>■</color> {team.DisplayName}";

    // ---- tabs ------------------------------------------------------------------------------------------------------------

    static void BuildPlayers(VisualElement root, List<TeamDefinition> teams, Player me)
    {
        root.Add(Heading("Players"));
        var players = Player.GetAllPlayers().Where(p => p != null).OrderBy(p => p != me).ThenBy(p => p.GetPlayerName()).ToList();
        foreach (var p in players)
        {
            var current = TeamsAPI.GetTeam(TeamsAPI.GetTeamId(p));
            var who = p == me ? "me" : p.GetPlayerName();
            var card = Card();
            var head = Rich($"{(p == me ? "<b>" + p.GetPlayerName() + "</b> (you)" : p.GetPlayerName())}   {(current != null ? Colored(current, current.DisplayName) : "<color=#8f939c>no team</color>")}", 18);
            head.style.marginBottom = 6;
            card.Add(head);
            var row = WrapRow();
            foreach (var t in teams)
            {
                var team = t;
                var isOn = current != null && string.Equals(current.Id, team.Id, StringComparison.OrdinalIgnoreCase);
                var b = Btn(TeamButtonText(team), () => Cmd($"assign {who} {team.Id}"), isOn ? UkButtonKind.Selected : UkButtonKind.Secondary);
                UkControls.SetButtonEnabled(b, !isOn);
                row.Add(b);
            }
            var none = Btn("No team", () => Cmd($"forget {who}"), UkButtonKind.Ghost);
            UkControls.SetButtonEnabled(none, current != null);
            row.Add(none);
            card.Add(row);
            root.Add(card);
        }
        if (players.Count <= 1)
            root.Add(Note("Only you are in the world. Other players (and test players) appear here once they join."));
    }

    static void BuildTeams(VisualElement root, List<TeamDefinition> teams)
    {
        root.Add(Heading("Teams"));
        var players = Player.GetAllPlayers().Where(p => p != null).ToList();
        foreach (var t in teams)
        {
            var team = t;
            var n = players.Count(p => string.Equals(TeamsAPI.GetTeamId(p), team.Id, StringComparison.OrdinalIgnoreCase));
            var card = Card();
            var head = Rich($"{Colored(team, "<b>" + team.DisplayName + "</b>")}   {n} here, spawn {(team.HasSpawn ? $"{team.SpawnX:0}, {team.SpawnZ:0}" : "default")}, icon {team.Icon}", 18);
            head.style.marginBottom = 6;
            card.Add(head);
            var row = WrapRow();
            row.Add(Btn("Spawn here", () => Cmd($"setspawn {team.Id}")));
            var clear = Btn("Clear spawn", () => Cmd($"clearspawn {team.Id}"), UkButtonKind.Ghost);
            UkControls.SetButtonEnabled(clear, team.HasSpawn);
            row.Add(clear);
            var stones = team.SpawnStones;
            var stoneBtn = Btn(!TeamsConfig.SpawnStonesExperimental ? "Stones: off (experimental)" : stones ? "Stones: on" : "Stones: off",
                () => Cmd($"spawnstones {team.Id} {(stones ? "off" : "on")}"));
            UkControls.SetButtonEnabled(stoneBtn, team.HasSpawn && TeamsConfig.SpawnStonesExperimental);
            row.Add(stoneBtn);
            var edit = Editing.Contains(team.Id);
            row.Add(Btn(edit ? "Less" : "Edit look", () =>
            {
                if (!Editing.Remove(team.Id))
                    Editing.Add(team.Id);
                Rebuild();
            }, edit ? UkButtonKind.Selected : UkButtonKind.Secondary));
            card.Add(row);
            if (edit)
                card.Add(BuildTeamLook(team));
            root.Add(card);
        }
        root.Add(Note("\"Spawn here\" sets where this team respawns to the spot you stand on. Start stones are an experimental feature (off): placing them can leave seams in the terrain. Logos have their own tab."));
    }

    static VisualElement BuildTeamLook(TeamDefinition team)
    {
        var box = new VisualElement().Column();
        box.style.marginTop = 8;
        box.Add(UkControls.Divider());

        var colorRow = new VisualElement().Row(Align.FlexStart);
        colorRow.style.marginTop = 10;
        var colorPicker = new UkColorPicker();
        colorPicker.SetColor(team.Color);
        colorPicker.Picked = hex => Cmd($"setcolor {team.Id} {hex}");
        var colorCol = new VisualElement().Column();
        colorCol.Add(UkControls.Text("Team color", 16, UkTheme.Text, heading: true));
        colorCol.Add(colorPicker.Root);
        colorRow.Add(colorCol);

        var glowPicker = new UkColorPicker();
        glowPicker.SetColor(team.EffectiveGlowColor());
        glowPicker.Picked = hex => Cmd($"setglow {team.Id} {hex}");
        var glowCol = new VisualElement().Column();
        glowCol.style.marginLeft = 40;
        var glowHead = new VisualElement().Row();
        glowHead.Add(UkControls.Text("Glow color", 16, UkTheme.Text, heading: true));
        glowHead.Add(UkControls.Spacer());
        glowHead.Add(Btn("Use team color", () => Cmd($"setglow {team.Id} none"), UkButtonKind.Ghost));
        glowCol.Add(glowHead);
        glowCol.Add(glowPicker.Root);
        colorRow.Add(glowCol);
        box.Add(colorRow);

        var quick = WrapRow();
        quick.style.marginTop = 10;
        quick.Add(UkControls.Text("Quick colors", 14, UkTheme.TextMuted, wrap: false));
        foreach (var c in Palette)
        {
            var color = c;
            var swatch = new VisualElement().Size(32, 32).Round(6).Border(UkTheme.Line).Fill(TeamIcons.ParseColor(color));
            swatch.style.marginLeft = 8;
            swatch.RegisterCallback<ClickEvent>(_ => Cmd($"setcolor {team.Id} {color}"));
            quick.Add(swatch);
        }
        box.Add(quick);

        var iconRow = WrapRow();
        iconRow.style.marginTop = 10;
        iconRow.Add(UkControls.Text("Plain icon (a logo replaces it)", 14, UkTheme.TextMuted, wrap: false));
        foreach (var shape in TeamIcons.Shapes)
        {
            var s = shape;
            var b = Btn(s, () => Cmd($"seticon {team.Id} {s}"), string.Equals(team.Icon, s, StringComparison.OrdinalIgnoreCase) ? UkButtonKind.Selected : UkButtonKind.Secondary);
            b.style.marginLeft = 8;
            iconRow.Add(b);
        }
        box.Add(iconRow);

        var figureRow = WrapRow();
        figureRow.style.marginTop = 10;
        figureRow.Add(UkControls.Text("Ward figure (the middle of the team's Team Ward)", 14, UkTheme.TextMuted, wrap: false));
        foreach (var id in WardFigures.Ids)
        {
            var f = id;
            var b = Btn(WardFigures.Title(f), () => Cmd($"setfigure {team.Id} {f}"), string.Equals(team.Figure, f, StringComparison.OrdinalIgnoreCase) ? UkButtonKind.Selected : UkButtonKind.Secondary);
            b.style.marginLeft = 8;
            b.name = "figure-" + f;
            figureRow.Add(b);
        }
        box.Add(figureRow);
        return box;
    }

    static void BuildWard(VisualElement root, List<TeamDefinition> teams, Player me)
    {
        root.Add(Heading("Team Ward"));
        var ward = _ward ?? TeamWard.Nearest(me.transform.position, 12f);
        if (ward == null)
        {
            root.Add(Rich("No Team Ward within 12 m. Build one from the Hammer's Misc tab (admins only)."));
            return;
        }
        var wardTeam = TeamsAPI.GetTeam(TeamWard.TeamOf(ward));
        root.Add(Rich($"{Colored(wardTeam, wardTeam?.DisplayName ?? "No team")} ward, {Vector3.Distance(ward.transform.position, me.transform.position):0} m away.", 18));

        var card = Card();
        card.Add(UkControls.Text("Belongs to", 16, UkTheme.Text, heading: true));
        var teamRow = WrapRow();
        foreach (var t in teams)
        {
            var team = t;
            var isOn = wardTeam != null && string.Equals(wardTeam.Id, team.Id, StringComparison.OrdinalIgnoreCase);
            teamRow.Add(Btn(TeamButtonText(team), () => Cmd($"ward team {team.Id}"), isOn ? UkButtonKind.Selected : UkButtonKind.Secondary));
        }
        teamRow.Add(Btn("None", () => Cmd("ward team none"), UkButtonKind.Ghost));
        card.Add(teamRow);
        root.Add(card);

        var power = TeamWard.IsPowered(ward);
        var powerCard = Card();
        powerCard.Add(UkControls.Text("Power", 16, UkTheme.Text, heading: true));
        powerCard.Add(Note("On: the crystals stand out, the diamonds float and the runes glow. Off: everything folds in and goes dark."));
        var powerRow = WrapRow();
        powerRow.Add(Btn("On", () => Cmd("ward power on"), power ? UkButtonKind.Selected : UkButtonKind.Secondary));
        powerRow.Add(Btn("Off", () => Cmd("ward power off"), !power ? UkButtonKind.Selected : UkButtonKind.Secondary));
        powerCard.Add(powerRow);
        root.Add(powerCard);

        var protect = Card();
        protect.Add(UkControls.Text("Keeps outsiders out of", 16, UkTheme.Text, heading: true));
        foreach (var (what, key, global) in new[]
                 {
                     ("build", TeamWard.BuildKey, TeamsConfig.WardProtectBuild),
                     ("chests", TeamWard.ChestKey, TeamsConfig.WardProtectChests),
                     ("doors", TeamWard.DoorKey, TeamsConfig.WardProtectDoors),
                 })
        {
            var w = what;
            var v = TeamWard.Zdo(ward)?.GetInt(key, 0) ?? 0;
            var row = new VisualElement().Row();
            row.style.marginTop = 6;
            var label = Rich($"Protect {w}: <b>{(v == 1 ? "yes (this ward)" : v == 2 ? "no (this ward)" : global ? "yes (server default)" : "no (server default)")}</b>");
            label.style.width = 420;
            row.Add(label);
            row.Add(Btn("Yes", () => Cmd($"ward {w} on"), v == 1 ? UkButtonKind.Selected : UkButtonKind.Secondary, 80));
            row.Add(Btn("No", () => Cmd($"ward {w} off"), v == 2 ? UkButtonKind.Selected : UkButtonKind.Secondary, 80));
            row.Add(Btn("Server default", () => Cmd($"ward {w} inherit"), v == 0 ? UkButtonKind.Selected : UkButtonKind.Secondary));
            protect.Add(row);
        }
        root.Add(protect);

        // sections other mods add to this tab (TeamsAPI.RegisterTeamWardSection draws with IMGUI: hosted in a container)
        foreach (var (title, draw) in AdminPanel.SectionList)
        {
            root.Add(Heading(title));
            var host = new IMGUIContainer(() => draw(ward));
            host.style.minHeight = 40;
            root.Add(host);
        }
    }

    static void BuildServer(VisualElement root)
    {
        root.Add(Heading("Server settings"));
        var modes = new[] { AssignmentMode.AdminOnly, AssignmentMode.PlayerChoice, AssignmentMode.AutoBalance };
        var card = Card();
        card.Add(UkControls.Text("Who sets teams", 16, UkTheme.Text, heading: true));
        var seg = new UkSegmented(modes.Select(m => m.ToString()).ToArray());
        seg.SetIndex(Array.IndexOf(modes, TeamsConfig.AssignmentMode));
        seg.Root.style.marginTop = 8;
        seg.Changed = i => TeamsConfig.SetAssignmentMode(modes[i]);
        card.Add(seg.Root);
        card.Add(Note("AdminOnly: only admins put players on teams (this window or the console). PlayerChoice: players pick once with 'teams join'. AutoBalance: the server picks."));
        root.Add(card);

        var rules = DeathRulesState.Current;
        var sw = new UkSwitch("Death rules", $"Who may loot whom after a death ({rules.Scope}). Off by default; the rules live in deathrules.json.");
        sw.SetValue(rules.Enabled);
        sw.Changed = on => Cmd($"deathrules {(on ? "on" : "off")}");
        sw.Root.style.marginTop = 8;
        root.Add(sw.Root);
    }
}
