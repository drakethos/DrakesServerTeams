using System;
using System.Text;

namespace DrakesServerTeams;

/// <summary>Admin commands, run on the server after the sender has been checked against the admin list.</summary>
public static class TeamsAdmin
{
    static readonly BepInEx.Logging.ManualLogSource DeathRulesLog = BepInEx.Logging.Logger.CreateLogSource("DrakesServerTeams.Rules");

    public const string Usage =
        "teams panel | teams who | teams assign <player|me|all> <team> | teams forget <player|id:N> | teams setspawn <team> | teams clearspawn <team> | teams seticon <team> <shield|circle|diamond|star|cross> | teams ward [team <team|none> | build|chests|doors <on|off|inherit>] | teams offer <player|all> | teams spawnstones <team> <on|off> | teams setcolor <team> <color> | teams setglow <team> <color|none> | teams deathrules [on|off]";

    public static string Run(string commandLine, string adminName, UnityEngine.Vector3? adminPosition = null)
    {
        var args = commandLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0)
            return Usage;

        var registry = TeamRegistry.Instance;
        switch (args[0].ToLowerInvariant())
        {
            case "who":
                return Who();

            case "assign":
                if (args.Length < 3)
                    return "Usage: teams assign <player|me|all> <team>";
                // Character names may contain spaces: the team is the last word, the name is the rest.
                var team = args[args.Length - 1];
                var target = string.Join(" ", args, 1, args.Length - 2);
                if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    var count = 0;
                    foreach (var (pid, pname) in ConnectedPlayers())
                        if (registry.TryAssign(pid, team, pname, adminName))
                            count++;
                    return count > 0 ? $"{count} player(s) are now on {registry.GetTeam(team)?.DisplayName}." : $"Unknown team '{team}' or nobody online.";
                }
                if (!FindPlayer(target.Equals("me", StringComparison.OrdinalIgnoreCase) ? adminName : target, out var id, out var name, out var err))
                    return err;
                return registry.TryAssign(id, team, name, adminName)
                    ? $"{name} is now on {registry.GetTeam(team)?.DisplayName}."
                    : $"Unknown team '{team}'. Teams: {TeamsNet.JoinIds(registry.Teams, joinableOnly: false)}";

            case "forget":
                if (args.Length < 2)
                    return "Usage: teams forget <player|id:N>";
                var forgetTarget = string.Join(" ", args, 1, args.Length - 1);
                if (!FindPlayer(forgetTarget.Equals("me", StringComparison.OrdinalIgnoreCase) ? adminName : forgetTarget, out id, out name, out err))
                    return err;
                return registry.Forget(id, name, adminName)
                    ? $"Deleted the stored entry for {name}."
                    : $"{name} has no stored entry.";

            case "setspawn":
                if (args.Length < 2)
                    return "Usage: teams setspawn <team> [x z]  (no numbers = where you stand)";
                var spawnAt = adminPosition;
                if (args.Length >= 4)
                {
                    if (!float.TryParse(args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sx)
                        || !float.TryParse(args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sz))
                        return "Usage: teams setspawn <team> <x> <z>";
                    var ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(new UnityEngine.Vector3(sx, 0f, sz)) : 0f;
                    spawnAt = new UnityEngine.Vector3(sx, ground, sz);
                }
                if (spawnAt == null)
                    return "Could not find where you are standing.";
                return registry.SetTeamSpawn(args[1], spawnAt)
                    ? $"{registry.GetTeam(args[1])?.DisplayName} now spawns at {spawnAt.Value.x:0}, {spawnAt.Value.y:0}, {spawnAt.Value.z:0}."
                    : $"Unknown team '{args[1]}'. Teams: {TeamsNet.JoinIds(registry.Teams, joinableOnly: false)}";

            case "offer":
            {
                if (args.Length < 2)
                    return "Usage: teams offer <player|all>  (clears their team and shows them the team picker again; needs AssignmentMode PlayerChoice)";
                if (TeamsConfig.AssignmentMode != AssignmentMode.PlayerChoice)
                    return "The picker is only used when AssignmentMode is PlayerChoice.";
                var offerTarget = string.Join(" ", args, 1, args.Length - 1);
                var offered = 0;
                foreach (var (pid, pname) in ConnectedPlayers())
                {
                    if (!offerTarget.Equals("all", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(pname, offerTarget.Equals("me", StringComparison.OrdinalIgnoreCase) ? adminName : offerTarget, StringComparison.OrdinalIgnoreCase))
                        continue;
                    registry.Forget(pid, pname, adminName);
                    registry.SetOffered(pid, true);
                    if (TeamsNet.OfferPicker(pid))
                        offered++;
                }
                return offered > 0 ? $"Team picker shown to {offered} player(s)." : $"No online player named '{offerTarget}'.";
            }

            case "ward":
                return Ward(args, adminPosition);

            case "spawnstones":
            {
                if (args.Length < 3 || !(args[2].Equals("on", StringComparison.OrdinalIgnoreCase) || args[2].Equals("off", StringComparison.OrdinalIgnoreCase)))
                    return "Usage: teams spawnstones <team> <on|off>  (a copy of the start stones at the team's spawn point)";
                var st = registry.GetTeam(args[1]);
                if (st == null)
                    return $"Unknown team '{args[1]}'.";
                var on = args[2].Equals("on", StringComparison.OrdinalIgnoreCase);
                if (on && !TeamsConfig.SpawnStonesExperimental)
                    return "Start stones are experimental and off. They are placed with a mid-game terrain edit that can leave seams and height jumps in the world. "
                           + "If you really want to try them on a world you have backed up, set 7 Experimental/SpawnStonesExperimental to true.";
                if (on && !st.HasSpawn)
                    return $"{st.DisplayName} has no spawn point yet. Stand where you want it and run: teams setspawn {st.Id}";
                registry.SetTeamSpawnStones(st.Id, on);
                if (!on)
                    return $"{st.DisplayName}: no new start stones. Stones already placed stay in the world.";
                var result = SpawnStones.Ensure(st);
                return result == "zone not loaded yet"
                    ? $"{st.DisplayName}: start stones will appear when a player is near the spawn point."
                    : $"{st.DisplayName}: start stones {result}.";
            }

            case "setcolor":
                if (args.Length < 3)
                    return "Usage: teams setcolor <team> <#RRGGBB or a name: red, blue, green, gold ...>";
                if (registry.GetTeam(args[1]) == null)
                    return $"Unknown team '{args[1]}'.";
                return registry.SetTeamColor(args[1], args[2])
                    ? $"{registry.GetTeam(args[1])?.DisplayName} color: {registry.GetTeam(args[1])?.Color}."
                    : "Use a color like #FF8800, or a name: red, blue, green, gold, orange, purple ...";

            case "setglow":
            {
                if (args.Length < 3)
                    return "Usage: teams setglow <team> <#RRGGBB|none>  (none = use the team color)";
                var color = args[2].Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : args[2];
                if (registry.GetTeam(args[1]) == null)
                    return $"Unknown team '{args[1]}'.";
                if (!registry.SetTeamGlow(args[1], color))
                    return "Use a color like #FF8800.";
                return color.Length == 0
                    ? $"{registry.GetTeam(args[1])?.DisplayName} glow follows the team color."
                    : $"{registry.GetTeam(args[1])?.DisplayName} glow: {color}.";
            }

            case "deathrules":
            {
                DeathRulesState.EnsureLoadedOnServer(DeathRulesLog);
                var rules = DeathRulesState.Current;
                if (args.Length >= 2)
                {
                    var on = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                    if (!on && !args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                        return "Usage: teams deathrules [on|off]. Edit the rest in deathrules.json and restart.";
                    rules.Enabled = on;
                    DeathRulesState.Save(DeathRulesLog);
                    TeamsNet.BroadcastState();
                }
                return "Death rules: " + rules.Describe();
            }

            case "seticon":
                if (args.Length < 3)
                    return "Usage: teams seticon <team> <" + string.Join("|", TeamIcons.Shapes) + "|your-file-name>  (custom: icons\\<name>.png)";
                return registry.SetTeamIcon(args[1], args[2])
                    ? $"{registry.GetTeam(args[1])?.DisplayName} icon: {registry.GetTeam(args[1])?.Icon}."
                    : $"Unknown team '{args[1]}'.";

            case "clearspawn":
                if (args.Length < 2)
                    return "Usage: teams clearspawn <team>";
                return registry.SetTeamSpawn(args[1], null)
                    ? $"{registry.GetTeam(args[1])?.DisplayName} no longer has a spawn point."
                    : $"Unknown team '{args[1]}'.";

            default:
                return Usage;
        }
    }

    static string WardFlag(UnityEngine.Vector3? where, PrivateArea ward, string what, string value)
    {
        var key = what == "build" ? TeamWard.BuildKey : what == "chests" ? TeamWard.ChestKey : TeamWard.DoorKey;
        var v = value.Equals("on", StringComparison.OrdinalIgnoreCase) ? 1 : value.Equals("off", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        if (v == 0 && !value.Equals("inherit", StringComparison.OrdinalIgnoreCase))
            return "Use on, off or inherit.";
        var zdo = TeamWard.Zdo(ward);
        if (zdo == null)
            return "That ward is not ready.";
        zdo.Set(key, v);
        return $"Team Ward {what}: {(v == 1 ? "protected" : v == 2 ? "open to everyone" : "server default")}.";
    }

    /// <summary>teams ward: describe or change the Team Ward nearest to the admin (within 12 m).</summary>
    static string Ward(string[] args, UnityEngine.Vector3? where)
    {
        if (where == null)
            return "Could not find where you are standing.";
        var ward = TeamWard.Nearest(where.Value, 12f);
        if (ward == null)
            return "No Team Ward within 12 m.";
        var zdo = TeamWard.Zdo(ward);
        if (zdo == null)
            return "That ward is not ready.";
        if (args.Length >= 3 && args[1].Equals("team", StringComparison.OrdinalIgnoreCase))
        {
            var id = args[2].Equals("none", StringComparison.OrdinalIgnoreCase) ? null : args[2];
            if (id != null && TeamRegistry.Instance.GetTeam(id) == null)
                return $"Unknown team '{id}'.";
            return PieceTeams.Assign(zdo, id)
                ? $"Team Ward team: {(id == null ? "none" : TeamRegistry.Instance.GetTeam(id)?.DisplayName)}."
                : "That ward has no builder to sign for.";
        }
        if (args.Length >= 3 && (args[1] == "build" || args[1] == "chests" || args[1] == "doors"))
            return WardFlag(where, ward, args[1], args[2]);
        var team = TeamRegistry.Instance.GetTeam(PieceTeams.ReadTeam(zdo));
        string Flag(int key, bool global)
        {
            var v = zdo.GetInt(key, 0);
            return v == 1 ? "protected (this ward)" : v == 2 ? "open (this ward)" : global ? "protected (default)" : "open (default)";
        }
        return $"Team Ward of {team?.DisplayName ?? "no team"}: build {Flag(TeamWard.BuildKey, TeamsConfig.WardProtectBuild)}, "
               + $"chests {Flag(TeamWard.ChestKey, TeamsConfig.WardProtectChests)}, doors {Flag(TeamWard.DoorKey, TeamsConfig.WardProtectDoors)}.";
    }

    static string Who()
    {
        var sb = new StringBuilder();
        foreach (var (id, name) in ConnectedPlayers())
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(name).Append(": ").Append(TeamRegistry.Instance.GetTeam(TeamRegistry.Instance.GetTeamId(id))?.DisplayName ?? "no team");
        }
        return sb.Length == 0 ? "No players online." : sb.ToString();
    }

    static System.Collections.Generic.IEnumerable<(long id, string name)> ConnectedPlayers()
    {
        if (ZNet.instance == null)
            yield break;
        if (!ZNet.instance.IsDedicated() && PeerIdentity.TryResolve(PeerIdentity.ServerPeerId(), out var hostId, out var hostName))
            yield return (hostId, hostName);
        foreach (var peer in ZNet.instance.GetPeers())
            if (PeerIdentity.TryResolve(peer.m_uid, out var id, out var name))
                yield return (id, name);
    }

    /// <summary>Connected character by name (unique, case-insensitive), or "id:N" for an offline character.</summary>
    static bool FindPlayer(string arg, out long id, out string name, out string error)
    {
        id = 0;
        name = arg;
        error = "";

        if (arg.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(arg.Substring(3), out id) && id != 0)
            {
                name = "that character";
                return true;
            }
            error = "Bad id.";
            return false;
        }

        var matches = 0;
        foreach (var (pid, pname) in ConnectedPlayers())
        {
            if (!string.Equals(pname, arg, StringComparison.OrdinalIgnoreCase))
                continue;
            matches++;
            id = pid;
            name = pname;
        }
        if (matches == 1)
            return true;
        error = matches == 0 ? $"No online player named '{arg}'." : $"More than one player is named '{arg}'.";
        return false;
    }
}
