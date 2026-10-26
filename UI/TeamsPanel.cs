using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// The admin panel's contents, shared by the in-game window (<see cref="AdminPanel"/>) and the DrakesTestLab tab. IMGUI, no dependencies.
/// Every button sends the same command a console line would ("teams assign Bob red"), so the server decides and checks admin rights;
/// the panel adds no authority of its own.
/// </summary>
internal static class TeamsPanelUi
{
    static readonly string[] Palette = { "red", "orange", "gold", "green", "teal", "blue", "purple", "pink" };
    static readonly Dictionary<string, bool> Open = new();
    static GUIStyle? _rich;

    static GUIStyle Rich => _rich ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };

    /// <summary>Run an admin command line as the local player (host: directly; client: ask the server).</summary>
    public static void Cmd(string line) => TeamsNet.RunAdmin(line);

    // Dark team colors (a deep blue, a purple) are hard to read on the dark panels: text is lightened, the swatch keeps the real color.
    static string Readable(string hex)
    {
        var c = Color.Lerp(TeamIcons.ParseColor(hex), Color.white, 0.4f);
        return "#" + ColorUtility.ToHtmlStringRGB(c);
    }

    static string Colored(TeamDefinition? team, string text) =>
        team == null ? text : $"<color={team.Color}>■</color> <color={Readable(team.Color)}>{text}</color>";

    static void Button(string label, Action action, params GUILayoutOption[] options)
    {
        if (GUILayout.Button(label, options))
            action();
    }

    /// <summary>One Team Ward: its team and what it protects, as buttons. Also the body of the Shift+E panel.</summary>
    public static void DrawWard(PrivateArea ward, List<TeamDefinition> teams, Player me)
    {
        var wardTeam = TeamsAPI.GetTeam(TeamWard.TeamOf(ward));
        GUILayout.Label($"{Colored(wardTeam, wardTeam?.DisplayName ?? "No team")} ward, {Vector3.Distance(ward.transform.position, me.transform.position):0} m away.", Rich);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Team", GUILayout.Width(48f));
        foreach (var t in teams)
        {
            var team = t;
            Button($"<color={team.Color}>\u25A0</color> {team.DisplayName}", () => Cmd($"ward team {team.Id}"));
        }
        Button("None", () => Cmd("ward team none"), GUILayout.Width(52f));
        GUILayout.EndHorizontal();
        foreach (var (what, key, global) in new[]
                 {
                     ("build", TeamWard.BuildKey, TeamsConfig.WardProtectBuild),
                     ("chests", TeamWard.ChestKey, TeamsConfig.WardProtectChests),
                     ("doors", TeamWard.DoorKey, TeamsConfig.WardProtectDoors),
                 })
        {
            var w = what;
            var v = TeamWard.Zdo(ward)?.GetInt(key, 0) ?? 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Protect {w}: <b>{(v == 1 ? "yes (this ward)" : v == 2 ? "no (this ward)" : global ? "yes (server default)" : "no (server default)")}</b>", Rich, GUILayout.Width(300f));
            Button("Yes", () => Cmd($"ward {w} on"), GUILayout.Width(50f));
            Button("No", () => Cmd($"ward {w} off"), GUILayout.Width(50f));
            Button("Default", () => Cmd($"ward {w} inherit"));
            GUILayout.EndHorizontal();
        }
    }

    /// <param name="header">Starts a section (the lab tab draws a card, the window a bold line).</param>
    /// <param name="hint">A small grey note.</param>
    /// <param name="lab">Extra test-bench buttons (spread players over the teams, place a Team Ward).</param>
    public static void Draw(Action<string> header, Action<string> hint, bool lab)
    {
        var me = Player.m_localPlayer;
        if (me == null)
        {
            GUILayout.Label("No player yet.");
            return;
        }
        var teams = TeamsAPI.GetTeams().ToList();
        var admin = TeamWard.LocalIsAdmin();
        var own = TeamsAPI.GetTeam(TeamsAPI.GetTeamId(me));

        GUILayout.Label($"You: <b>{me.GetPlayerName()}</b>, team {(own != null ? Colored(own, own.DisplayName) : "none")}. "
                        + (admin ? "You are an admin." : "<color=#e0675c>You are not an admin: the server will refuse changes.</color>"), Rich);
        if (teams.Count == 0)
        {
            GUILayout.Label("The server has no teams yet (teams.json).", Rich);
            return;
        }

        // ---- spawn test (test bench) ---------------------------------------------------------------------
        if (lab)
        {
            header("Spawn test");
            foreach (var p in Player.GetAllPlayers().Where(p => p != null).OrderBy(p => p != me))
            {
                var team = TeamsAPI.GetTeam(TeamsAPI.GetTeamId(p));
                var spawn = TeamsAPI.GetSpawnPoint(p.GetPlayerID());
                GUILayout.Label($"{(p == me ? "<b>" + p.GetPlayerName() + "</b> (you)" : p.GetPlayerName())}  {(team != null ? Colored(team, team.DisplayName) : "no team")}: "
                                + (spawn != null ? $"spawns at {spawn.Value.x:0}, {spawn.Value.z:0}" : "world default (the start stones)"), Rich);
            }
            GUILayout.BeginHorizontal();
            Button("Teleport me to my team's spawn", () => TeamsLabTools.GoToSpawn());
            Button("Die and respawn (comes back at the team spawn)", () => TeamsLabTools.DieAndRespawn());
            GUILayout.EndHorizontal();
            hint("Other players' spawns come from the server's registry (they cannot be killed from here). To respawn as test player 2: Wards tab > Play as Bjornita, then assign a team below and press Die and respawn.");
        }

        // ---- players -----------------------------------------------------------------------------------
        header("Players");
        var players = Player.GetAllPlayers().Where(p => p != null).OrderBy(p => p != me).ThenBy(p => p.GetPlayerName()).ToList();
        foreach (var p in players)
        {
            var current = TeamsAPI.GetTeam(TeamsAPI.GetTeamId(p));
            var who = p == me ? "me" : p.GetPlayerName();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{(p == me ? "<b>" + p.GetPlayerName() + "</b> (you)" : p.GetPlayerName())}  {(current != null ? Colored(current, current.DisplayName) : "<color=#8f939c>no team</color>")}",
                Rich, GUILayout.Width(210f));
            foreach (var t in teams)
            {
                var team = t;
                var isOn = current != null && string.Equals(current.Id, team.Id, StringComparison.OrdinalIgnoreCase);
                GUI.enabled = !isOn;
                Button($"<color={team.Color}>■</color> {team.DisplayName}", () => Cmd($"assign {who} {team.Id}"));
                GUI.enabled = true;
            }
            GUI.enabled = current != null;
            Button("None", () => Cmd($"forget {who}"), GUILayout.Width(52f));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
        if (players.Count <= 1)
            hint("Only you are in the world. Other players (and test players) appear here once they join.");
        if (lab)
        {
            GUILayout.BeginHorizontal();
            Button("Spread everyone over the teams", () =>
            {
                for (var i = 0; i < players.Count; i++)
                    Cmd($"assign {(players[i] == me ? "me" : players[i].GetPlayerName())} {teams[i % teams.Count].Id}");
            });
            Button("Everyone: no team", () =>
            {
                foreach (var p in players)
                    Cmd($"forget {(p == me ? "me" : p.GetPlayerName())}");
            });
            GUILayout.EndHorizontal();
        }

        // ---- teams ---------------------------------------------------------------------------------------
        header("Teams");
        foreach (var t in teams)
        {
            var team = t;
            var n = players.Count(p => string.Equals(TeamsAPI.GetTeamId(p), team.Id, StringComparison.OrdinalIgnoreCase));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{Colored(team, "<b>" + team.DisplayName + "</b>")}  {n} here, spawn {(team.HasSpawn ? $"{team.SpawnX:0}, {team.SpawnZ:0}" : "default")}, icon {team.Icon}",
                Rich, GUILayout.Width(300f));
            Button("Spawn here", () => Cmd($"setspawn {team.Id}"));
            GUI.enabled = team.HasSpawn;
            Button("Clear", () => Cmd($"clearspawn {team.Id}"), GUILayout.Width(52f));
            GUI.enabled = true;
            var stones = team.SpawnStones;
            GUI.enabled = team.HasSpawn;
            Button(stones ? "Stones: on" : "Stones: off", () => Cmd($"spawnstones {team.Id} {(stones ? "off" : "on")}"));
            GUI.enabled = true;
            Open.TryGetValue(team.Id, out var edit);
            Button(edit ? "Less" : "Edit", () => Open[team.Id] = !edit, GUILayout.Width(52f));
            GUILayout.EndHorizontal();
            if (!edit)
                continue;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Color", GUILayout.Width(48f));
            foreach (var c in Palette)
            {
                var color = c;
                Button(color, () => Cmd($"setcolor {team.Id} {color}"));
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Glow", GUILayout.Width(48f));
            Button("team color", () => Cmd($"setglow {team.Id} none"));
            foreach (var c in Palette)
            {
                var color = c;
                Button(color, () => Cmd($"setglow {team.Id} {color}"));
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Icon", GUILayout.Width(48f));
            foreach (var shape in TeamIcons.Shapes)
            {
                var s = shape;
                Button(s, () => Cmd($"seticon {team.Id} {s}"));
            }
            GUILayout.EndHorizontal();
        }
        hint("\"Spawn here\" sets where this team respawns to the spot you stand on. \"Stones\" also places a copy of the start stones there.");

        // ---- team ward ------------------------------------------------------------------------------------
        header("Team Ward");
        var ward = TeamWard.Nearest(me.transform.position, 12f);
        if (ward == null)
            GUILayout.Label("No Team Ward within 12 m. Build one from the Hammer's Misc tab (admins only).", Rich);
        else
            DrawWard(ward, teams, me);
        if (lab)
            Button("Place a Team Ward in front of me (test, as me)", () => TeamsLabTools.PlaceWard());

        // ---- server ---------------------------------------------------------------------------------------
        header("Server settings");
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Who sets teams: <b>{TeamsConfig.AssignmentMode}</b>", Rich, GUILayout.Width(300f));
        foreach (var mode in new[] { AssignmentMode.AdminOnly, AssignmentMode.PlayerChoice, AssignmentMode.AutoBalance })
        {
            var m = mode;
            GUI.enabled = TeamsConfig.AssignmentMode != m;
            Button(m.ToString(), () => TeamsConfig.SetAssignmentMode(m));
            GUI.enabled = true;
        }
        GUILayout.EndHorizontal();
        var rules = DeathRulesState.Current;
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Death rules: <b>{(rules.Enabled ? "on" : "off")}</b> ({rules.Scope})", Rich, GUILayout.Width(300f));
        Button(rules.Enabled ? "Turn off" : "Turn on", () => Cmd($"deathrules {(rules.Enabled ? "off" : "on")}"));
        GUILayout.EndHorizontal();
        hint("AdminOnly: only admins put players on teams (this panel or the console). PlayerChoice: players pick once with 'teams join'. AutoBalance: the server picks.");
    }
}

/// <summary>Things only the test bench needs.</summary>
internal static class TeamsLabTools
{
    /// <summary>Move the local player to the spawn point of their team.</summary>
    public static void GoToSpawn()
    {
        var me = Player.m_localPlayer;
        var spawn = me != null ? TeamsAPI.GetSpawnPoint(me.GetPlayerID()) : null;
        if (me == null || spawn == null)
        {
            TeamsNet.Say("Your team has no spawn point (or you have no team).");
            return;
        }
        me.TeleportTo(spawn.Value + Vector3.up * 0.5f, me.transform.rotation, true);
    }

    /// <summary>Kill the local player the way a death does, so the game respawns them (at their team's spawn point).</summary>
    public static void DieAndRespawn()
    {
        var me = Player.m_localPlayer;
        if (me == null)
            return;
        try
        {
            HarmonyLib.AccessTools.Method(typeof(Character), "OnDeath")?.Invoke(me, null);
        }
        catch (Exception ex)
        {
            TeamsNet.Say("Could not kill you: " + ex.Message);
        }
    }

    /// <summary>Put a Team Ward down 4 m in front of the local player, built by them (so it carries their team).</summary>
    public static void PlaceWard()
    {
        var me = Player.m_localPlayer;
        var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(TeamWard.PrefabName) : null;
        if (me == null || prefab == null)
        {
            TeamsNet.Say("The Team Ward piece is not loaded.");
            return;
        }
        var pos = me.transform.position + me.transform.forward * 4f;
        pos.y = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(pos) : pos.y;
        var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
        go.GetComponent<Piece>()?.SetCreator(me.GetPlayerID());
        TeamsNet.Say("Team Ward placed in front of you.");
    }
}
