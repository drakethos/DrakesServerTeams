using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DrakesServerTeams.Testing;

/// <summary>DrakesTestLab finds methods marked with an attribute named <c>TestHookAttribute</c>. No reference to DrakesTestLab.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestHookAttribute : Attribute
{
    public TestHookAttribute(string name) => Name = name;
    public string Name { get; }
}

/// <summary>
/// Test hooks for automated Teams tests. The test lab runs as the host, so these call the same server code the RPCs
/// reach, as the current (play-as) player. Target = a tagged piece (args "target"), else the nearest ward, chest or door.
/// </summary>
internal static class TeamsTestHooks
{
    static Player P => Player.m_localPlayer ?? throw new InvalidOperationException("no local player");

    static string S(Dictionary<string, object?> a, string key) =>
        a.TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";

    static void NeedAuthority()
    {
        if (!TeamsNet.IsAuthority)
            throw new InvalidOperationException("Teams test hooks need the host (the test lab world)");
    }

    static ZNetView View(GameObject? target)
    {
        GameObject? go = target;
        if (go == null)
        {
            var best = 20f;
            foreach (var piece in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                var stamped = piece.GetComponent<PrivateArea>() != null
                              || piece.GetComponentInChildren<Container>() != null
                              || piece.GetComponentInChildren<Door>() != null;
                var d = Vector3.Distance(piece.transform.position, P.transform.position);
                if (stamped && d < best) { best = d; go = piece.gameObject; }
            }
        }

        if (go == null)
            throw new InvalidOperationException("no ward, chest or door nearby");
        var view = go.GetComponentInParent<ZNetView>() ?? go.GetComponentInChildren<ZNetView>();
        if (view == null || !view.IsValid())
            throw new InvalidOperationException("the piece has no network view");
        return view;
    }

    /// <summary>Put the current player on a team (admin / server API path). Returns the team afterwards.</summary>
    [TestHook("teams.assign")]
    static string Assign(Dictionary<string, object?> args)
    {
        NeedAuthority();
        var team = S(args, "team");
        if (!TeamsAPI.TryAssign(P.GetPlayerID(), team))
            throw new InvalidOperationException($"unknown team '{team}'");
        return TeamsAPI.GetTeamId(P.GetPlayerID()) ?? "";
    }

    /// <summary>Delete the current player's entry (admin "forget"). Returns the team afterwards ("" = none).</summary>
    [TestHook("teams.forget")]
    static string Forget()
    {
        NeedAuthority();
        TeamsAPI.Forget(P.GetPlayerID());
        return TeamsAPI.GetTeamId(P.GetPlayerID()) ?? "";
    }

    /// <summary>Join a team as the player would (teams join): "ok" or the reason it was refused.</summary>
    [TestHook("teams.selfJoin")]
    static string SelfJoin(Dictionary<string, object?> args)
    {
        NeedAuthority();
        return TeamRegistry.Instance.TrySelfAssign(P.GetPlayerID(), S(args, "team"), P.GetPlayerName(), out var reason) ? "ok" : reason;
    }

    [TestHook("teams.myTeam")]
    static string MyTeam() => TeamsAPI.GetTeamId(P) ?? "";

    /// <summary>The team written on the local player's ZDO (what other clients read).</summary>
    [TestHook("teams.publishedTeam")]
    static string PublishedTeam() => P.GetComponent<ZNetView>()?.GetZDO()?.GetString(TeamsAPI.TeamZdoKey, "") ?? "";

    /// <summary>Write a team on the local player's ZDO, as a modified client could.</summary>
    [TestHook("teams.fakePublishedTeam")]
    static string FakePublishedTeam(Dictionary<string, object?> args)
    {
        P.GetComponent<ZNetView>()?.GetZDO()?.Set(TeamsAPI.TeamZdoKey, S(args, "team"));
        return PublishedTeam();
    }

    /// <summary>Run the server's published-team check now. Returns the published team afterwards.</summary>
    [TestHook("teams.guardNow")]
    static string GuardNow()
    {
        NeedAuthority();
        TeamsNet.GuardPublishedTeams();
        return PublishedTeam();
    }

    /// <summary>Turn piece teams on (normally LockSmith does this at startup).</summary>
    [TestHook("teams.usePieceTeams")]
    static bool UsePieceTeams()
    {
        TeamsAPI.UsePieceTeams();
        return TeamsAPI.PieceTeamsActive;
    }

    /// <summary>The team stamped on the piece ("" = none).</summary>
    [TestHook("teams.pieceTeam")]
    static string PieceTeam(GameObject? target = null) => TeamsAPI.GetPieceTeamId(View(target)) ?? "";

    [TestHook("teams.isOnPieceTeam")]
    static bool IsOnPieceTeam(GameObject? target = null) => TeamsAPI.IsOnPieceTeam(P, View(target));

    /// <summary>Write an unsigned stamp on the piece, as a modified client could. Returns the piece team afterwards.</summary>
    [TestHook("teams.forgeStamp")]
    static string ForgeStamp(Dictionary<string, object?> args, GameObject? target = null)
    {
        var view = View(target);
        view.GetZDO().Set(TeamsAPI.PieceTeamZdoKey, S(args, "team") + "|0000000000000000");
        return TeamsAPI.GetPieceTeamId(view) ?? "";
    }

    /// <summary>Remove the stamp, like a piece built before the mod. Returns the piece team afterwards.</summary>
    [TestHook("teams.clearStamp")]
    static string ClearStamp(GameObject? target = null)
    {
        var view = View(target);
        view.GetZDO().Set(TeamsAPI.PieceTeamZdoKey, "");
        return TeamsAPI.GetPieceTeamId(view) ?? "";
    }

    /// <summary>Run the server's check on one piece now (what the sweep does). Returns the piece team afterwards.</summary>
    [TestHook("teams.checkPiece")]
    static string CheckPiece(GameObject? target = null)
    {
        NeedAuthority();
        var view = View(target);
        PieceTeams.CheckOne(view.GetZDO());
        return TeamsAPI.GetPieceTeamId(view) ?? "";
    }

    /// <summary>Start a full sweep. True when one started (false while one is running).</summary>
    [TestHook("teams.sweepNow")]
    static bool SweepNow()
    {
        NeedAuthority();
        var host = DrakesServerTeamsPlugin.Instance ?? throw new InvalidOperationException("plugin not loaded");
        return PieceTeams.SweepNow(host);
    }

    [TestHook("teams.sweeping")]
    static bool Sweeping() => PieceTeams.Sweeping;

    // ---- several players: Alice (play-as) plus virtual players (Bjornita, Bob, Amy ...) ----------------

    /// <summary>A player in the world by name: "" / "me" = the local player (as whoever they play as), else a connected character.</summary>
    static Player Who(Dictionary<string, object?> a, string key = "player")
    {
        var name = S(a, key);
        if (name.Length == 0 || name.Equals("me", StringComparison.OrdinalIgnoreCase))
            return P;
        foreach (var p in Player.GetAllPlayers())
            if (p != null && string.Equals(p.GetPlayerName(), name, StringComparison.OrdinalIgnoreCase))
                return p;
        throw new InvalidOperationException($"no player named '{name}' in the world (peerJoin first)");
    }

    static string Fmt(Vector3? v) =>
        v == null ? "none" : string.Format(CultureInfo.InvariantCulture, "{0:0},{1:0},{2:0}", v.Value.x, v.Value.y, v.Value.z);

    /// <summary>Server API assignment of a named player. Returns their team afterwards.</summary>
    [TestHook("teams.assignTo")]
    static string AssignTo(Dictionary<string, object?> args)
    {
        NeedAuthority();
        var who = Who(args);
        var team = S(args, "team");
        if (!TeamsAPI.TryAssign(who.GetPlayerID(), team))
            throw new InvalidOperationException($"unknown team '{team}'");
        return TeamsAPI.GetTeamId(who.GetPlayerID()) ?? "";
    }

    /// <summary>What the server (or the published value, on a client) says about a named player ("" = no team).</summary>
    [TestHook("teams.teamOf")]
    static string TeamOf(Dictionary<string, object?> args) => TeamsAPI.GetTeamId(Who(args)) ?? "";

    /// <summary>The team a named player publishes on its player ZDO (what other clients read).</summary>
    [TestHook("teams.publishedTeamOf")]
    static string PublishedTeamOf(Dictionary<string, object?> args) =>
        Who(args).GetComponent<ZNetView>()?.GetZDO()?.GetString(TeamsAPI.TeamZdoKey, "") ?? "";

    /// <summary>Are the two named players (a, b) on the same team?</summary>
    [TestHook("teams.sameTeam")]
    static bool SameTeam(Dictionary<string, object?> args) => TeamsAPI.AreSameTeam(Who(args, "a"), Who(args, "b"));

    /// <summary>Forget the named player, then let the server auto-balance them as it does for a new character.</summary>
    [TestHook("teams.autoAssign")]
    static string AutoAssign(Dictionary<string, object?> args)
    {
        NeedAuthority();
        var who = Who(args);
        TeamRegistry.Instance.Forget(who.GetPlayerID());
        return TeamRegistry.Instance.TryAutoAssign(who.GetPlayerID(), who.GetPlayerName()) ?? "";
    }

    [TestHook("teams.memberCount")]
    static int MemberCount(Dictionary<string, object?> args) => TeamRegistry.Instance.MemberCount(S(args, "team"));

    /// <summary>Delete every stored member so counts start from zero. Returns how many were removed.</summary>
    [TestHook("teams.clearMembers")]
    static int ClearMembers()
    {
        NeedAuthority();
        var ids = TeamRegistry.Instance.MemberIds();
        foreach (var id in ids)
            TeamRegistry.Instance.Forget(id);
        return ids.Count;
    }

    /// <summary>Add a team that exists for this run only. Args: team, title, color; optional x/y/z = its spawn point. Returns the team count.</summary>
    [TestHook("teams.defineTeam")]
    static int DefineTeam(Dictionary<string, object?> args)
    {
        NeedAuthority();
        var id = S(args, "team");
        var def = new TeamDefinition
        {
            Id = id,
            DisplayName = S(args, "title").Length > 0 ? S(args, "title") : id,
            Color = S(args, "color").Length > 0 ? S(args, "color") : "#27AE60",
        };
        if (args.ContainsKey("x"))
            def.SetSpawn(Pos(args));
        if (S(args, "icon").Length > 0)
            def.Icon = TeamIcons.Normalize(S(args, "icon"));
        if (S(args, "glow").Length > 0)
            def.GlowColor = S(args, "glow");
        TeamRegistry.Instance.DefineTeam(def);
        return TeamRegistry.Instance.Teams.Count;
    }

    [TestHook("teams.removeTeam")]
    static bool RemoveTeam(Dictionary<string, object?> args)
    {
        NeedAuthority();
        return TeamRegistry.Instance.RemoveTeam(S(args, "team"));
    }

    static Vector3 Pos(Dictionary<string, object?> a) => new(F(a, "x"), F(a, "y"), F(a, "z"));

    static float F(Dictionary<string, object?> a, string key) =>
        float.Parse(S(a, key).Length > 0 ? S(a, key) : "0", CultureInfo.InvariantCulture);

    // ---- spawn points ----------------------------------------------------------------------------------

    /// <summary>Give a team a spawn point (x, y, z). Returns it as "x,y,z".</summary>
    [TestHook("teams.setSpawn")]
    static string SetSpawn(Dictionary<string, object?> args)
    {
        NeedAuthority();
        var team = TeamRegistry.Instance.GetTeam(S(args, "team")) ?? throw new InvalidOperationException($"unknown team '{S(args, "team")}'");
        team.SetSpawn(Pos(args));
        return Fmt(team.GetSpawnPoint());
    }

    [TestHook("teams.clearSpawn")]
    static string ClearSpawn(Dictionary<string, object?> args)
    {
        NeedAuthority();
        var team = TeamRegistry.Instance.GetTeam(S(args, "team")) ?? throw new InvalidOperationException($"unknown team '{S(args, "team")}'");
        team.HasSpawn = false;
        return "none";
    }

    /// <summary>Where the server would spawn the named player ("x,y,z", or "none").</summary>
    [TestHook("teams.spawnOf")]
    static string SpawnOf(Dictionary<string, object?> args) => Fmt(TeamsAPI.GetSpawnPoint(Who(args).GetPlayerID()));

    /// <summary>How many pairs of teams share a spawn point (should be 0).</summary>
    [TestHook("teams.sharedSpawns")]
    static int SharedSpawns() => TeamRegistry.Instance.SharedSpawns().Count;

    /// <summary>The spawn point the local client has been told about for its own team ("none" if not).</summary>
    [TestHook("teams.clientSpawn")]
    static string ClientSpawn()
    {
        var team = TeamsClientState.GetTeam(TeamsClientState.OwnTeamId);
        return team != null && team.HasSpawn ? Fmt(team.GetSpawnPoint()) : "none";
    }

    /// <summary>The point the game's own spawn lookup returns for the local player (runs the real patch). "x,y,z" or "none".</summary>
    [TestHook("teams.findSpawn")]
    static string FindSpawn()
    {
        if (Game.instance == null)
            throw new InvalidOperationException("no game");
        // private in the real game
        var method = HarmonyLib.AccessTools.Method(typeof(Game), "FindSpawnPoint") ?? throw new InvalidOperationException("Game.FindSpawnPoint not found");
        var call = new object?[] { Vector3.zero, false, 0f };
        var found = method.Invoke(Game.instance, call) is true;
        return found && call[1] is false ? Fmt((Vector3)call[0]!) : "none";
    }

    // ---- first-team move and admin spawn commands ------------------------------------------------------

    /// <summary>The first-team move the server ordered for the named player ("x,y,z", or "none").</summary>
    [TestHook("teams.teleportOrdered")]
    static string TeleportOrdered(Dictionary<string, object?> args) =>
        TeamsNet.TeleportsSent.TryGetValue(Who(args).GetPlayerID(), out var v) ? Fmt(v) : "none";

    [TestHook("teams.clearTeleports")]
    static int ClearTeleports()
    {
        var n = TeamsNet.TeleportsSent.Count;
        TeamsNet.TeleportsSent.Clear();
        return n;
    }

    /// <summary>Where the local player stands, "x,y,z".</summary>
    [TestHook("teams.myPosition")]
    static string MyPosition() => Fmt(P.transform.position);

    /// <summary>Run an admin command line as the admin standing at the local player ("setspawn red"). Returns the reply.</summary>
    [TestHook("teams.adminCommand")]
    static string AdminCommand(Dictionary<string, object?> args)
    {
        NeedAuthority();
        return TeamsAdmin.Run(S(args, "line"), P.GetPlayerName(), P.transform.position);
    }

    /// <summary>The first-team move setting.</summary>
    [TestHook("teams.moveOnFirstTeam")]
    static bool MoveOnFirstTeam() => TeamsConfig.MoveToSpawnOnFirstTeam;

    // ---- visuals ----------------------------------------------------------------------------------------

    /// <summary>Change a team's icon (like "teams seticon"). Returns the icon afterwards.</summary>
    [TestHook("teams.setIcon")]
    static string SetIcon(Dictionary<string, object?> args)
    {
        NeedAuthority();
        TeamRegistry.Instance.SetTeamIcon(S(args, "team"), S(args, "icon"));
        return TeamRegistry.Instance.GetTeam(S(args, "team"))?.Icon ?? "";
    }

    /// <summary>The glow around the named player: "#RRGGBB" or "none". Waits are the caller's job (it refreshes twice a second).</summary>
    [TestHook("teams.glowOf")]
    static string GlowOf(Dictionary<string, object?> args) => Who(args).GetComponent<TeamGlow>()?.Describe() ?? "no glow component";

    /// <summary>Name color and icon on the nameplate the local client draws for the named player: "#RRGGBB|icon", "none", or "no plate".</summary>
    [TestHook("teams.nameplateOf")]
    static string NameplateOf(Dictionary<string, object?> args)
    {
        var who = Who(args);
        var hudRoot = GameFields.CurrentHud;
        TMPro.TMP_Text? name = null;
        if (hudRoot != null)
            GameFields.EachPlate(hudRoot, (c, t) => { if (c == who) name = t; });
        if (name == null)
            return "no plate";
        var icon = name.transform.Find("DrakesServerTeams_icon");
        var image = icon != null && icon.gameObject.activeSelf ? icon.GetComponent<UnityEngine.UI.Image>() : null;
        return TeamIcons.ToHex(name.color) + "|" + (image != null && image.sprite != null ? "icon " + TeamIcons.ToHex(image.color) : "no icon");
    }

    /// <summary>Make a grave for the named player the way a death does (Setup on a new tombstone), 3 m in front of the local player. Returns the team written on it.</summary>
    [TestHook("teams.makeGrave")]
    static string MakeGrave(Dictionary<string, object?> args)
    {
        var who = Who(args);
        var prefab = ZNetScene.instance.GetPrefab("Player_tombstone") ?? throw new InvalidOperationException("no Player_tombstone prefab");
        var go = UnityEngine.Object.Instantiate(prefab, P.transform.position + P.transform.forward * (3f + F(args, "dz")) + P.transform.right * F(args, "dx") + Vector3.up, Quaternion.identity);
        var tomb = go.GetComponent<TombStone>();
        tomb.Setup(who.GetPlayerName(), who.GetPlayerID());
        // an empty grave despawns after a moment; a screenshot needs it to stay
        var container = HarmonyLib.AccessTools.Field(typeof(TombStone), "m_container")?.GetValue(tomb) as Container;
        var wood = ZNetScene.instance.GetPrefab("Wood");
        if (container != null && wood != null && B(args, "fill", true))
            container.GetInventory().AddItem(wood, 1);
        return TeamGrave.ReadTeam(tomb);
    }

    /// <summary>The nearest grave within 10 m: "team|name color|light color|icon" (see TeamGrave.Describe).</summary>
    [TestHook("teams.graveInfo")]
    static string GraveInfo()
    {
        TeamGrave? best = null;
        var dist = 10f;
        foreach (var g in UnityEngine.Object.FindObjectsByType<TeamGrave>(FindObjectsSortMode.None))
        {
            var d = Vector3.Distance(g.transform.position, P.transform.position);
            if (d < dist) { dist = d; best = g; }
        }
        return best != null ? best.Describe() : "no grave";
    }

    [TestHook("teams.removeGraves")]
    static int RemoveGraves()
    {
        var n = 0;
        foreach (var t in UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None))
        {
            var nv = t.GetComponent<ZNetView>();
            if (nv != null && nv.IsValid()) { nv.ClaimOwnership(); nv.Destroy(); n++; }
        }
        return n;
    }

    // ---- death rules ------------------------------------------------------------------------------------

    static bool B(Dictionary<string, object?> a, string key, bool fallback) =>
        a.TryGetValue(key, out var v) && v != null ? Convert.ToBoolean(v, CultureInfo.InvariantCulture) : fallback;

    /// <summary>Set the death rules for this run only (not saved to deathrules.json). Args: enabled, appliesTo, keepEquipped, keep, lootable (comma lists), percent.</summary>
    [TestHook("teams.setDeathRules")]
    static string SetDeathRules(Dictionary<string, object?> args)
    {
        var rules = new DeathRules
        {
            Enabled = B(args, "enabled", true),
            AppliesTo = S(args, "appliesTo").Length > 0 ? S(args, "appliesTo") : "EnemyTeamKill",
            KeepEquipped = B(args, "keepEquipped", true),
            Keep = DeathRulesState.Split(S(args, "keep")),
            EnemyLootable = DeathRulesState.Split(S(args, "lootable")),
            EnemyLootPercent = S(args, "percent").Length > 0 ? (int)F(args, "percent") : 100,
        };
        DeathRulesState.Apply(rules);
        return DeathRulesState.Current.Describe();
    }

    static string ItemName(ItemDrop.ItemData i) => i.m_dropPrefab != null ? i.m_dropPrefab.name : i.m_shared.m_name;

    static int CountIn(Inventory? inv, string item)
    {
        var n = 0;
        if (inv != null)
            foreach (var i in inv.GetAllItems())
                if (string.Equals(ItemName(i), item, StringComparison.OrdinalIgnoreCase))
                    n += i.m_stack;
        return n;
    }

    /// <summary>How many of the item the local player carries.</summary>
    [TestHook("teams.invCount")]
    static int InvCount(Dictionary<string, object?> args) => CountIn(P.GetInventory(), S(args, "item"));

    [TestHook("teams.invEquipped")]
    static bool InvEquipped(Dictionary<string, object?> args)
    {
        foreach (var i in P.GetInventory().GetAllItems())
            if (i.m_equipped && string.Equals(ItemName(i), S(args, "item"), StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>How many of the item lie on the ground within 6 m of the local player.</summary>
    [TestHook("teams.groundCount")]
    static int GroundCount(Dictionary<string, object?> args)
    {
        var n = 0;
        foreach (var d in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            if (Vector3.Distance(d.transform.position, DeathLoot.LastPoint) < 6f
                && string.Equals(ItemName(d.m_itemData), S(args, "item"), StringComparison.OrdinalIgnoreCase))
                n += d.m_itemData.m_stack;
        return n;
    }

    /// <summary>How many of the item the nearest grave (within 8 m) holds.</summary>
    [TestHook("teams.graveCount")]
    static int GraveCount(Dictionary<string, object?> args)
    {
        TombStone? best = null;
        var dist = 8f;
        foreach (var t in UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None))
        {
            var d = Vector3.Distance(t.transform.position, DeathLoot.LastPoint);
            if (d < dist) { dist = d; best = t; }
        }
        var container = best != null ? HarmonyLib.AccessTools.Field(typeof(TombStone), "m_container")?.GetValue(best) as Container : null;
        return container != null ? CountIn(container.GetInventory(), S(args, "item")) : -1;
    }

    /// <summary>Remove dropped items and graves near the local player so tests do not see each other's leftovers.</summary>
    [TestHook("teams.clearGround")]
    static int ClearGround()
    {
        var n = RemoveGraves();
        foreach (var d in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
        {
            if (Vector3.Distance(d.transform.position, DeathLoot.LastPoint) > 12f && Vector3.Distance(d.transform.position, P.transform.position) > 12f)
                continue;
            var nv = d.GetComponent<ZNetView>();
            if (nv != null && nv.IsValid()) { nv.ClaimOwnership(); nv.Destroy(); n++; }
        }
        return n;
    }

    /// <summary>
    /// Run the game's own grave creation for the local player, with a chosen killer: "monster" (not a player), "teamless" (a player
    /// without a team) or a team id. Returns what the death rules did.
    /// </summary>
    [TestHook("teams.simulateDeath")]
    static string SimulateDeath(Dictionary<string, object?> args)
    {
        var killer = S(args, "killer");
        DeathLoot.KillerOverride = _ => killer.Equals("monster", StringComparison.OrdinalIgnoreCase) ? (false, null)
            : killer.Equals("teamless", StringComparison.OrdinalIgnoreCase) ? (true, null)
            : (true, killer);
        DeathLoot.LastOutcome = "not run";
        DeathLoot.LastPoint = P.transform.position;
        try
        {
            HarmonyLib.AccessTools.Method(typeof(Player), "CreateTombStone")!.Invoke(P, null);
        }
        finally
        {
            DeathLoot.KillerOverride = null;
        }
        return DeathLoot.LastOutcome;
    }

    /// <summary>Set a team's glow color ("" = team color), like "teams setglow". Returns the glow color in use.</summary>
    [TestHook("teams.setGlow")]
    static string SetGlow(Dictionary<string, object?> args)
    {
        NeedAuthority();
        TeamRegistry.Instance.SetTeamGlow(S(args, "team"), S(args, "color"));
        return TeamRegistry.Instance.GetTeam(S(args, "team"))?.EffectiveGlowColor() ?? "";
    }

    /// <summary>Parse a color setting the way team files are read: "#RRGGBB" back, or "invalid".</summary>
    [TestHook("teams.parseColor")]
    static string ParseColorHook(Dictionary<string, object?> args) =>
        TeamIcons.TryParseColor(S(args, "text"), out var c) ? TeamIcons.ToHex(c) : "invalid";

    /// <summary>"builtin", "file" or "missing" for an icon name on this machine.</summary>
    [TestHook("teams.iconKind")]
    static string IconKind(Dictionary<string, object?> args) => TeamIcons.Kind(S(args, "icon"));

    /// <summary>The icon name that would be stored for this text.</summary>
    [TestHook("teams.iconName")]
    static string IconNameHook(Dictionary<string, object?> args) => TeamIcons.Normalize(S(args, "icon"));

    /// <summary>Write a sample custom icon (a gold crest, its own colors) to the icons folder as name.png. Returns the file name.</summary>
    [TestHook("teams.writeSampleIcon")]
    static string WriteSampleIcon(Dictionary<string, object?> args)
    {
        var name = TeamIcons.Normalize(S(args, "icon"));
        var folder = System.IO.Path.Combine(TeamStore.Folder, "icons");
        System.IO.Directory.CreateDirectory(folder);
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var u = (x + 0.5f) / n * 2f - 1f;
            var v = (y + 0.5f) / n * 2f - 1f;
            var r = Mathf.Sqrt(u * u + v * v);
            var c = new Color(0f, 0f, 0f, 0f);
            if (r <= 0.95f)
                c = r > 0.78f ? new Color(0.95f, 0.78f, 0.15f) : new Color(0.12f, 0.12f, 0.16f);
            if (r <= 0.7f && Mathf.Abs(u) + Mathf.Abs(v) <= 0.6f)
                c = new Color(0.95f, 0.78f, 0.15f);
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), TeamIcons.EncodePng(tex));
        UnityEngine.Object.Destroy(tex);
        TeamIcons.ClearCache();
        return name + ".png";
    }

    [TestHook("teams.deleteSampleIcon")]
    static bool DeleteSampleIcon(Dictionary<string, object?> args)
    {
        var path = System.IO.Path.Combine(TeamStore.Folder, "icons", TeamIcons.Normalize(S(args, "icon")) + ".png");
        TeamIcons.ClearCache();
        if (!System.IO.File.Exists(path))
            return false;
        System.IO.File.Delete(path);
        return true;
    }

    /// <summary>Move a connected player's puppet and the lab view for screenshots is the caller's job; this just lists who is in the world.</summary>
    [TestHook("teams.playersInWorld")]
    static int PlayersInWorld() => Player.GetAllPlayers().Count;

    /// <summary>Set the time of day (0 = midnight, 0.5 = noon) and hold it there. For screenshots.</summary>
    [TestHook("teams.setTime")]
    static string SetTime(Dictionary<string, object?> args)
    {
        var env = EnvMan.instance ?? throw new InvalidOperationException("no EnvMan");
        HarmonyLib.AccessTools.Field(typeof(EnvMan), "m_debugTimeOfDay")?.SetValue(env, true);
        HarmonyLib.AccessTools.Field(typeof(EnvMan), "m_debugTime")?.SetValue(env, Mathf.Clamp01(F(args, "time")));
        return F(args, "time").ToString(CultureInfo.InvariantCulture);
    }

    static Vector3 _mark;
    static Vector3? _clearing;

    /// <summary>
    /// Stand on open ground facing north: the first call picks the spot 45 m north of where the player is (the test world's spawn is a ring of
    /// standing stones and 45 m north is flat); later calls go back to that same spot, so a second test does not walk further.
    /// </summary>
    [TestHook("teams.goClearing")]
    static string GoClearing()
    {
        _clearing ??= P.transform.position + Vector3.forward * 45f;
        _mark = _clearing.Value - Vector3.forward * 45f;
        return HopFromMark(new Dictionary<string, object?> { ["yaw"] = "0", ["dist"] = "45" });
    }

    /// <summary>Remember where the local player stands (for hopFromMark).</summary>
    [TestHook("teams.mark")]
    static string Mark()
    {
        _mark = P.transform.position;
        return Fmt(_mark);
    }

    /// <summary>Move the local player to a spot "dist" meters from the mark toward compass "yaw" (degrees), facing the same way. Short hops, like the lab's teleport.</summary>
    [TestHook("teams.hopFromMark")]
    static string HopFromMark(Dictionary<string, object?> args)
    {
        var yaw = F(args, "yaw");
        var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        var pos = _mark + dir * F(args, "dist");
        pos.y = ZoneSystem.instance.GetGroundHeight(pos) + 0.3f;
        P.transform.position = pos;
        P.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        HarmonyLib.AccessTools.Field(typeof(Player), "m_lookYaw")?.SetValue(P, P.transform.rotation);
        var body = P.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = pos;
            body.velocity = Vector3.zero;
        }
        Physics.SyncTransforms();
        return Fmt(pos);
    }

    /// <summary>Diagnostic: every renderer and material on the nearest grave, with colors and emission.</summary>
    [TestHook("teams.dumpGrave")]
    static string DumpGrave()
    {
        TombStone? best = null;
        var dist = 12f;
        foreach (var t in UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None))
        {
            var d = Vector3.Distance(t.transform.position, P.transform.position);
            if (d < dist) { dist = d; best = t; }
        }
        if (best == null)
            return "no grave";
        var sb = new System.Text.StringBuilder();
        foreach (var r in best.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                sb.Append(r.GetType().Name).Append(' ').Append(r.name).Append(" [").Append(m.name).Append(" / ").Append(m.shader.name).Append("]");
                if (m.HasProperty("_Color")) sb.Append(" color=").Append(TeamIcons.ToHex(m.GetColor("_Color")));
                if (m.HasProperty("_EmissionColor")) sb.Append(" emis=").Append(m.GetColor("_EmissionColor"));
                sb.Append(" ; ");
            }
        }
        foreach (var l in best.GetComponentsInChildren<Light>(true))
            sb.Append("LIGHT ").Append(l.name).Append(' ').Append(TeamIcons.ToHex(l.color)).Append(" ; ");
        foreach (var ps in best.GetComponentsInChildren<ParticleSystem>(true))
            sb.Append("PS ").Append(ps.name).Append(" start=").Append(ps.main.startColor.color).Append(" ; ");
        return sb.ToString();
    }

    // ---- spawn stones ------------------------------------------------------------------------------------

    /// <summary>Turn a team's own start stones on or off, like "teams spawnstones". Returns the setting.</summary>
    [TestHook("teams.setSpawnStones")]
    static bool SetSpawnStones(Dictionary<string, object?> args)
    {
        NeedAuthority();
        TeamRegistry.Instance.SetTeamSpawnStones(S(args, "team"), B(args, "on", true));
        return TeamRegistry.Instance.GetTeam(S(args, "team"))?.SpawnStones ?? false;
    }

    /// <summary>Try to place the team's stones right now. Returns "exists", "placed" or the reason it did not.</summary>
    [TestHook("teams.ensureStones")]
    static string EnsureStones(Dictionary<string, object?> args)
    {
        NeedAuthority();
        var team = TeamRegistry.Instance.GetTeam(S(args, "team")) ?? throw new InvalidOperationException($"unknown team '{S(args, "team")}'");
        return SpawnStones.Ensure(team);
    }

    /// <summary>Is a start location (original or copy) within 12 m of x/z?</summary>
    [TestHook("teams.stonesNear")]
    static bool StonesNear(Dictionary<string, object?> args)
    {
        var team = TeamRegistry.Instance.GetTeam(S(args, "team"));
        return SpawnStones.HasStartLocationNear(team != null ? team.GetSpawnPoint() : Pos(args));
    }

    /// <summary>The spawn point of a team as the lab sees it, "x,y,z" or "none".</summary>
    [TestHook("teams.teamSpawn")]
    static string TeamSpawn(Dictionary<string, object?> args)
    {
        var team = TeamRegistry.Instance.GetTeam(S(args, "team"));
        return team != null && team.HasSpawn ? Fmt(team.GetSpawnPoint()) : "none";
    }

    /// <summary>Whether the game has a start location named StartTemple at all (the stones need it).</summary>
    [TestHook("teams.hasStartLocation")]
    static bool HasStartLocation() =>
        ZoneSystem.instance != null
        && HarmonyLib.AccessTools.Method(typeof(ZoneSystem), "GetLocation", new[] { typeof(string) })?.Invoke(ZoneSystem.instance, new object[] { SpawnStones.LocationName }) != null;

    /// <summary>Stand "dist" meters south of a team's spawn point, looking north at it. For pictures.</summary>
    [TestHook("teams.viewTeamSpawn")]
    static string ViewTeamSpawn(Dictionary<string, object?> args)
    {
        var team = TeamRegistry.Instance.GetTeam(S(args, "team")) ?? throw new InvalidOperationException("unknown team");
        _mark = team.GetSpawnPoint() - Vector3.forward * F(args, "dist");
        return HopFromMark(new Dictionary<string, object?> { ["yaw"] = "0", ["dist"] = "0" });
    }

    /// <summary>Where the game really put the last copy of the stones, "x,y,z".</summary>
    [TestHook("teams.stonesPlacedAt")]
    static string StonesPlacedAt() => Fmt(SpawnStones.LastPlacedAt);

    /// <summary>The player's position, "x,y,z" (rounded).</summary>
    [TestHook("teams.where")]
    static string Where() => Fmt(P.transform.position);

    // ---- team ward -------------------------------------------------------------------------------------------

    /// <summary>Pretend the local player is an admin (true), not an admin (false) or ask the game again ("auto").</summary>
    [TestHook("teams.setAdmin")]
    static string SetAdmin(Dictionary<string, object?> args)
    {
        var v = S(args, "on");
        TeamWard.AdminOverride = v.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : B(args, "on", true);
        return TeamWard.AdminOverride?.ToString() ?? "auto";
    }

    static PrivateArea AreaOf(GameObject? ward) =>
        (ward != null ? ward.GetComponentInParent<PrivateArea>() ?? ward.GetComponentInChildren<PrivateArea>() : null)
        ?? throw new InvalidOperationException("that is not a ward");

    /// <summary>Does the named player get through the ward for this kind of action (build, chest, door, other)?</summary>
    [TestHook("teams.wardPermits")]
    static bool WardPermits(GameObject? ward, Dictionary<string, object?> args)
    {
        var area = AreaOf(ward);
        var kind = Enum.TryParse<WardKind>(S(args, "kind"), true, out var k) ? k : WardKind.Other;
        TeamWard.Enter(kind);
        try
        {
            // IsPermitted is private in the live game: reflection only
            return HarmonyLib.AccessTools.Method(typeof(PrivateArea), "IsPermitted", new[] { typeof(long) })!.Invoke(area, new object[] { Who(args).GetPlayerID() }) is true;
        }
        finally
        {
            TeamWard.Leave();
        }
    }

    /// <summary>The ward's look: "label|glow|icon|light".</summary>
    [TestHook("teams.wardLook")]
    static string WardLook(GameObject? ward) => AreaOf(ward).GetComponent<TeamWardVisual>()?.Describe() ?? "no visual";

    /// <summary>Is the Team Ward in the Hammer's piece list (and a prefab the game knows)?</summary>
    [TestHook("teams.hasWardPiece")]
    static bool HasWardPiece()
    {
        var hammer = ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>();
        var pieces = hammer?.m_itemData.m_shared.m_buildPieces;
        if (pieces == null || ZNetScene.instance?.GetPrefab(TeamWard.PrefabName) == null)
            return false;
        foreach (var p in pieces.m_pieces)
            if (p != null && p.name == TeamWard.PrefabName)
                return true;
        return false;
    }

    /// <summary>The ward's current protection flags: build/chests/doors as on|off, per ward override included.</summary>
    [TestHook("teams.wardFlags")]
    static string WardFlags(GameObject? ward)
    {
        var a = AreaOf(ward);
        string F(WardKind k) => TeamWard.Protects(a, k) ? "on" : "off";
        return $"{F(WardKind.Build)}/{F(WardKind.Chest)}/{F(WardKind.Door)}";
    }

    /// <summary>Change a Team Ward server setting for this run only (build, chests, doors, admin). Returns "Name=value".</summary>
    [TestHook("teams.setWardDefault")]
    static string SetWardDefault(Dictionary<string, object?> args)
    {
        var what = S(args, "what").ToLowerInvariant();
        var on = B(args, "on", true);
        var entry = what == "build" ? "ProtectBuild" : what == "chests" ? "ProtectChests" : what == "doors" ? "ProtectDoors" : "TeamWardAdminOnly";
        var cfg = DrakesServerTeamsPlugin.Instance!.Config;
        cfg["5 Team Ward", entry].BoxedValue = on;
        return entry + "=" + on;
    }

    /// <summary>Open (true) or close the in-game admin panel. For screenshots.</summary>
    [TestHook("teams.openPanel")]
    static bool OpenPanel(Dictionary<string, object?> args)
    {
        if (B(args, "open", true) != AdminPanel.IsOpen)
            AdminPanel.Toggle();
        return AdminPanel.IsOpen;
    }

    /// <summary>Did the Teams tab register with DrakesTestLab? ("yes", or why not)</summary>
    [TestHook("teams.labTabRegistered")]
    static string LabTabRegistered()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name != "DrakesTestLab")
                continue;
            var type = asm.GetType("DrakesTestLab.TestLabTabs");
            var list = type?.GetField("Registered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null) as System.Collections.IEnumerable;
            if (list == null)
                return "TestLabTabs not found (old DrakesTestLab?)";
            foreach (var e in list)
                if ((string?)e.GetType().GetField("Title", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(e) == "Teams")
                    return "yes";
            return "no Teams tab registered";
        }
        return "DrakesTestLab not loaded";
    }

    /// <summary>Press E (or Shift+E with alt=true) on the ward as the local player. Returns whether the ward's settings window is open afterwards.</summary>
    [TestHook("teams.wardInteract")]
    static bool WardInteract(GameObject? ward, Dictionary<string, object?> args)
    {
        var area = AreaOf(ward);
        WardPanel.Close();
        ((Interactable)area).Interact(P, false, B(args, "alt", false));
        return WardPanel.IsOpen;
    }

    [TestHook("teams.closeWardPanel")]
    static bool CloseWardPanel()
    {
        WardPanel.Close();
        return WardPanel.IsOpen;
    }

    /// <summary>The ward's hover text with tags stripped.</summary>
    [TestHook("teams.wardHover")]
    static string WardHover(GameObject? ward) =>
        System.Text.RegularExpressions.Regex.Replace(AreaOf(ward).GetHoverText() ?? "", "<[^>]+>", "").Replace("\n", " / ");

    /// <summary>The server's hello for a named player: true when the team picker would be shown to them (and the server writes the flag).</summary>
    [TestHook("teams.helloOffer")]
    static bool HelloOffer(Dictionary<string, object?> args)
    {
        NeedAuthority();
        var who = Who(args);
        return TeamsNet.ProcessHello(who.GetPlayerID(), who.GetPlayerName());
    }

    [TestHook("teams.pickerOpen")]
    static bool PickerOpen(Dictionary<string, object?> args)
    {
        if (B(args, "open", false))
            TeamPicker.Open();
        else
            TeamPicker.Close();
        return TeamPicker.IsOpen;
    }

    [TestHook("teams.setMode")]
    static string SetMode(Dictionary<string, object?> args)
    {
        TeamsConfig.SetAssignmentMode(Enum.TryParse<AssignmentMode>(S(args, "mode"), true, out var m) ? m : AssignmentMode.AdminOnly);
        return TeamsConfig.AssignmentMode.ToString();
    }
}
