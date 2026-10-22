using System.Collections.Generic;
using Jotunn.Entities;

namespace DrakesServerTeams;

/// <summary>Console command (F5). Everything goes to the server, which decides; this only asks and prints.</summary>
public sealed class TeamsCommand : ConsoleCommand
{
    public override string Name => "teams";

    public override string Help =>
        "Teams. 'teams' shows yours, 'teams join <team>' picks one when the server allows it. "
        + "Admins: " + TeamsAdmin.Usage;

    public override bool IsNetwork => true;

    public override void Run(string[] args)
    {
        if (args.Length == 0 || args[0].ToLowerInvariant() == "list")
        {
            Show();
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "join":
                if (args.Length < 2)
                    TeamsNet.Say("Usage: teams join <team>");
                else
                    TeamsNet.RequestChoose(args[1]);
                break;
            default:
                TeamsNet.RequestAdmin(string.Join(" ", args));
                break;
        }
    }

    public override List<string> CommandOptionList() => new() { "list", "join", "who", "assign", "forget" };

    static void Show()
    {
        if (!TeamsClientState.Ready)
        {
            TeamsNet.Say("Teams: waiting for the server.");
            return;
        }
        var own = TeamsClientState.GetTeam(TeamsClientState.OwnTeamId);
        TeamsNet.Say($"Your team: {own?.DisplayName ?? "none"}. Teams: {TeamsNet.JoinIds(TeamsClientState.Teams, joinableOnly: false)}"
                     + (TeamsClientState.CanSwitch ? " (you can switch with 'teams join <team>')" : ""));
    }
}
