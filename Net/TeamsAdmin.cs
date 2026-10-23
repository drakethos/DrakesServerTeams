using System;
using System.Text;

namespace DrakesServerTeams;

/// <summary>Admin commands, run on the server after the sender has been checked against the admin list.</summary>
public static class TeamsAdmin
{
    public const string Usage =
        "teams who | teams assign <player> <team> | teams forget <player|id:N>";

    public static string Run(string commandLine, string adminName)
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
                    return "Usage: teams assign <player> <team>";
                // Character names may contain spaces: the team is the last word, the name is the rest.
                var team = args[args.Length - 1];
                if (!FindPlayer(string.Join(" ", args, 1, args.Length - 2), out var id, out var name, out var err))
                    return err;
                return registry.TryAssign(id, team, name, adminName)
                    ? $"{name} is now on {registry.GetTeam(team)?.DisplayName}."
                    : $"Unknown team '{team}'. Teams: {TeamsNet.JoinIds(registry.Teams, joinableOnly: false)}";

            case "forget":
                if (args.Length < 2)
                    return "Usage: teams forget <player|id:N>";
                if (!FindPlayer(string.Join(" ", args, 1, args.Length - 1), out id, out name, out err))
                    return err;
                return registry.Forget(id, name, adminName)
                    ? $"Deleted the stored entry for {name}."
                    : $"{name} has no stored entry.";

            default:
                return Usage;
        }
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
