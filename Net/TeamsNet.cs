using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// One Jotunn custom RPC carries everything. Client to server: hello, choose, admin command.
/// Server to client: state (own team + team display data), message. The server decides everything.
/// </summary>
public static class TeamsNet
{
    const string RpcName = "DrakesServerTeams";

    enum Op
    {
        Hello = 1,
        Choose = 2,
        Admin = 3,
        State = 10,
        Message = 11,
    }

    static CustomRPC _rpc = null!;
    static ManualLogSource _log = null!;
    static MonoBehaviour _host = null!;
    static Coroutine? _hello;

    public static bool IsAuthority => ZNet.instance != null && ZNet.instance.IsServer();

    public static void Init(MonoBehaviour host, ManualLogSource log)
    {
        _host = host;
        _log = log;
        // Same handler both ways: it checks which side it is running on.
        _rpc = NetworkManager.Instance.AddRPC(RpcName, Receive, Receive);
        TeamRegistry.Instance.SetLog(log);
        TeamRegistry.Instance.Changed += OnServerTeamChanged;
    }

    // ---- client side ----------------------------------------------------------------------------------

    /// <summary>Called when the local player spawns. Retries until the server has seen the character.</summary>
    public static void OnLocalPlayerSpawned()
    {
        if (_hello != null)
            _host.StopCoroutine(_hello);
        TeamsClientState.Reset();
        _hello = _host.StartCoroutine(HelloRoutine());
    }

    static IEnumerator HelloRoutine()
    {
        for (var attempt = 0; attempt < 8 && !TeamsClientState.Ready; attempt++)
        {
            yield return new WaitForSeconds(attempt == 0 ? 2f : 3f);
            if (ZRoutedRpc.instance == null || Player.m_localPlayer == null)
                continue;
            SendToServer(Op.Hello);
        }
        _hello = null;
    }

    public static void RequestChoose(string teamId) => SendToServer(Op.Choose, p => p.Write(teamId));

    public static void RequestAdmin(string commandLine) => SendToServer(Op.Admin, p => p.Write(commandLine));

    static void SendToServer(Op op, Action<ZPackage>? fill = null)
    {
        if (ZRoutedRpc.instance == null)
            return;
        var pkg = new ZPackage();
        pkg.Write((int)op);
        fill?.Invoke(pkg);
        _rpc.SendPackage(ZRoutedRpc.instance.GetServerPeerID(), pkg);
    }

    // ---- receive (both sides) -------------------------------------------------------------------------

    static IEnumerator Receive(long sender, ZPackage pkg)
    {
        try
        {
            var op = (Op)pkg.ReadInt();
            if (op < Op.State)
            {
                if (IsAuthority)
                    HandleOnServer(sender, op, pkg);
            }
            // State/Message are server -> client. On a host the server is also the client, so accept them
            // there only from the host itself; a remote client must not be able to feed them to the host.
            else if (!IsAuthority || PeerIdentity.IsHostLocal(sender))
                HandleOnClient(op, pkg);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Bad packet: {ex.Message}");
        }
        yield break;
    }

    static void HandleOnClient(Op op, ZPackage pkg)
    {
        switch (op)
        {
            case Op.State:
                ReadState(pkg);
                break;
            case Op.Message:
                Say(pkg.ReadString());
                break;
        }
    }

    static void ReadState(ZPackage pkg)
    {
        if (!pkg.ReadBool())
            return; // server has not seen our character yet; the hello routine retries
        var own = pkg.ReadString();
        var mode = (AssignmentMode)pkg.ReadInt();
        var canSwitch = pkg.ReadBool();
        var count = pkg.ReadInt();
        var teams = new List<TeamDefinition>(count);
        for (var i = 0; i < count; i++)
        {
            teams.Add(new TeamDefinition
            {
                Id = pkg.ReadString(),
                DisplayName = pkg.ReadString(),
                Color = pkg.ReadString(),
                Joinable = pkg.ReadBool(),
                Locked = pkg.ReadBool(),
            });
        }

        var hadTeam = TeamsClientState.OwnTeamId != null;
        var firstState = !TeamsClientState.Ready;
        TeamsClientState.Apply(own, mode, canSwitch, teams);
        PublishOwnTeam();
        if (firstState && !hadTeam && TeamsClientState.OwnTeamId == null && mode == AssignmentMode.PlayerChoice)
            Say("Pick a team: open the console (F5) and type  teams join <team>.  Teams: " + JoinIds(TeamsClientState.Teams, joinableOnly: true));
        else if (firstState && TeamsClientState.OwnTeamId != null)
            Say("Team: " + (TeamsClientState.GetTeam(TeamsClientState.OwnTeamId)?.DisplayName ?? TeamsClientState.OwnTeamId));
    }

    /// <summary>
    /// Puts the team id on the local player's ZDO so every client can read the team of any visible player.
    /// Display and client-side hints only: the owning client writes it, so it can be faked by a modified
    /// client. Anything that must be enforced asks the authority (<see cref="TeamsAPI"/> on the server).
    /// </summary>
    static void PublishOwnTeam()
    {
        var player = Player.m_localPlayer;
        var zdo = player != null ? player.GetComponent<ZNetView>()?.GetZDO() : null;
        if (zdo == null)
            return;
        zdo.Set(TeamsAPI.TeamZdoKey, TeamsClientState.OwnTeamId ?? "");
    }

    public static string JoinIds(IEnumerable<TeamDefinition> teams, bool joinableOnly)
    {
        var sb = new StringBuilder();
        foreach (var t in teams)
        {
            if (joinableOnly && !t.Joinable)
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(t.Id);
        }
        return sb.Length == 0 ? "(none)" : sb.ToString();
    }

    public static void Say(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, text);
        Console.instance?.Print(text);
    }

    // ---- server side ----------------------------------------------------------------------------------

    static void HandleOnServer(long sender, Op op, ZPackage pkg)
    {
        var registry = TeamRegistry.Instance;
        registry.EnsureLoaded();

        if (!PeerIdentity.TryResolve(sender, out var characterId, out var name))
        {
            if (op == Op.Hello)
                SendNotReady(sender);
            else
                SendMessage(sender, "Your character is not ready yet. Try again in a moment.");
            return;
        }

        switch (op)
        {
            case Op.Hello:
                if (!registry.HasEntry(characterId) && TeamsConfig.AssignmentMode == AssignmentMode.AutoBalance)
                    registry.TryAutoAssign(characterId, name);
                registry.Touch(characterId);
                SendState(sender, characterId);
                break;

            case Op.Choose:
                var teamId = pkg.ReadString();
                if (registry.TrySelfAssign(characterId, teamId, name, out var reason))
                    SendMessage(sender, $"You joined {registry.GetTeam(teamId)?.DisplayName ?? teamId}.");
                else
                    SendMessage(sender, reason);
                // OnServerTeamChanged pushes the new state on success.
                break;

            case Op.Admin:
                if (!PeerIdentity.IsAdmin(sender))
                {
                    SendMessage(sender, "Admins only.");
                    break;
                }
                SendMessage(sender, TeamsAdmin.Run(pkg.ReadString(), name));
                break;
        }
    }

    static void OnServerTeamChanged(long characterId, string? oldTeam, string? newTeam)
    {
        var sender = PeerIdentity.FindSender(characterId);
        if (sender != null)
            SendState(sender.Value, characterId);
    }

    static void SendNotReady(long target)
    {
        var pkg = new ZPackage();
        pkg.Write((int)Op.State);
        pkg.Write(false);
        _rpc.SendPackage(target, pkg);
    }

    static void SendState(long target, long characterId)
    {
        var registry = TeamRegistry.Instance;
        var own = registry.GetTeamId(characterId);
        var canSwitch = TeamsConfig.AssignmentMode == AssignmentMode.PlayerChoice
                        && !(own != null && registry.GetTeam(own)?.Locked == true);

        var pkg = new ZPackage();
        pkg.Write((int)Op.State);
        pkg.Write(true);
        pkg.Write(own ?? "");
        pkg.Write((int)TeamsConfig.AssignmentMode);
        pkg.Write(canSwitch);
        pkg.Write(registry.Teams.Count);
        foreach (var t in registry.Teams)
        {
            pkg.Write(t.Id);
            pkg.Write(t.DisplayName);
            pkg.Write(t.Color);
            pkg.Write(t.Joinable);
            pkg.Write(t.Locked);
        }
        _rpc.SendPackage(target, pkg);
    }

    public static void SendMessage(long target, string text)
    {
        var pkg = new ZPackage();
        pkg.Write((int)Op.Message);
        pkg.Write(text);
        _rpc.SendPackage(target, pkg);
    }
}
