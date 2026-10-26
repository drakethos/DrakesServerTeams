using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        Stamp = 4,
        State = 10,
        Message = 11,
        Teleport = 12,
    }

    static CustomRPC _rpc = null!;
    static ManualLogSource _log = null!;
    static MonoBehaviour _host = null!;
    static Coroutine? _hello;

    internal static void Warn(string message) => _log?.LogWarning(message);

    public static bool IsAuthority => ZNet.instance != null && ZNet.instance.IsServer();

    public static void Init(MonoBehaviour host, ManualLogSource log)
    {
        _host = host;
        _log = log;
        // Same handler both ways: it checks which side it is running on.
        _rpc = NetworkManager.Instance.AddRPC(RpcName, Receive, Receive);
        TeamRegistry.Instance.SetLog(log);
        TeamRegistry.Instance.Changed += OnServerTeamChanged;
        TeamRegistry.Instance.DefinitionsChanged += BroadcastState;
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

    /// <summary>
    /// An admin command from the local player (console, panel, test bench). The host runs it right here; a client asks the server, which
    /// checks admin rights. The reply is shown on screen either way.
    /// </summary>
    public static void RunAdmin(string commandLine)
    {
        if (IsAuthority && Player.m_localPlayer != null)
        {
            Say(TeamsAdmin.Run(commandLine, Player.m_localPlayer.GetPlayerName(), Player.m_localPlayer.transform.position));
            return;
        }
        RequestAdmin(commandLine);
    }

    /// <summary>The local player built a ward, chest or door: the server stamps it with our team.</summary>
    public static void RequestStamp(ZDOID id)
    {
        if (IsAuthority)
        {
            if (Player.m_localPlayer != null)
                PieceTeams.OnStampRequest(id, Player.m_localPlayer.GetPlayerID());
            return;
        }
        SendToServer(Op.Stamp, p => p.Write(id));
    }

    /// <summary>
    /// The local character changed under us (another character loaded, or a test tool swapped the player id):
    /// what we know about our own team no longer applies, so ask again.
    /// </summary>
    public static void CheckLocalIdentity()
    {
        var player = Player.m_localPlayer;
        if (player == null || _hello != null || !TeamsClientState.Ready)
            return;
        if (player.GetPlayerID() != TeamsClientState.PlayerId)
            OnLocalPlayerSpawned();
    }

    static void SendToServer(Op op, Action<ZPackage>? fill = null)
    {
        if (ZRoutedRpc.instance == null)
            return;
        var pkg = new ZPackage();
        pkg.Write((int)op);
        fill?.Invoke(pkg);
        _rpc.SendPackage(PeerIdentity.ServerPeerId(), pkg);
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
            case Op.Teleport:
                MoveToSpawn(pkg.ReadLong(), new Vector3(pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadSingle()));
                break;
        }
    }

    /// <summary>The server put us on our first team: go to its spawn point. Only for our own character.</summary>
    static void MoveToSpawn(long playerId, Vector3 point)
    {
        var player = Player.m_localPlayer;
        if (player == null || player.GetPlayerID() != playerId)
            return;
        player.TeleportTo(point, player.transform.rotation, true);
    }

    static void ReadState(ZPackage pkg)
    {
        if (!pkg.ReadBool())
            return; // server has not seen our character yet; the hello routine retries
        var own = pkg.ReadString();
        var mode = (AssignmentMode)pkg.ReadInt();
        var canSwitch = pkg.ReadBool();
        var offerPicker = pkg.ReadBool();
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
                HasSpawn = pkg.ReadBool(),
                SpawnX = pkg.ReadSingle(),
                SpawnY = pkg.ReadSingle(),
                SpawnZ = pkg.ReadSingle(),
                Icon = pkg.ReadString(),
                GlowColor = pkg.ReadString(),
            });
        }

        var rules = new DeathRules
        {
            Enabled = pkg.ReadBool(),
            AppliesTo = pkg.ReadString(),
            KeepEquipped = pkg.ReadBool(),
            EnemyLootPercent = pkg.ReadInt(),
            Keep = DeathRulesState.Split(pkg.ReadString()),
            EnemyLootable = DeathRulesState.Split(pkg.ReadString()),
        };
        // On the host the server's own copy is the original; do not replace it with a round trip.
        if (!IsAuthority)
            DeathRulesState.Apply(rules);

        var hadTeam = TeamsClientState.OwnTeamId != null;
        var firstState = !TeamsClientState.Ready;
        TeamsClientState.Apply(Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L, own, mode, canSwitch, teams);
        PublishOwnTeam();
        if (TeamsClientState.OwnTeamId != null)
            TeamPicker.Close();
        else if (offerPicker && mode == AssignmentMode.PlayerChoice)
            TeamPicker.Open();
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
        ShowCenter(text);
        Console.instance?.Print(text);
    }

    // MessageHud.ShowMessage gained a parameter in Valheim 1.0 and the stubs only know the old one, so find
    // the (MessageType, string, ...) overload at runtime and pass defaults for the rest.
    static MethodInfo? _showMessage;

    static void ShowCenter(string text)
    {
        var hud = MessageHud.instance;
        if (hud == null)
            return;
        _showMessage ??= typeof(MessageHud).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "ShowMessage")
            .Select(m => (m, ps: m.GetParameters()))
            .Where(x => x.ps.Length >= 2 && x.ps[0].ParameterType == typeof(MessageHud.MessageType) && x.ps[1].ParameterType == typeof(string))
            .OrderByDescending(x => x.ps.Length)
            .Select(x => x.m)
            .FirstOrDefault();
        if (_showMessage == null)
            return;
        var ps = _showMessage.GetParameters();
        var args = new object?[ps.Length];
        args[0] = MessageHud.MessageType.Center;
        args[1] = text;
        for (var i = 2; i < ps.Length; i++)
        {
            var t = ps[i].ParameterType;
            args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : t.IsValueType ? Activator.CreateInstance(t) : null;
        }
        _showMessage.Invoke(hud, args);
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
                SendState(sender, characterId, ProcessHello(characterId, name));
                break;

            case Op.Choose:
                var teamId = pkg.ReadString();
                if (registry.TrySelfAssign(characterId, teamId, name, out var reason))
                    SendMessage(sender, $"You joined {registry.GetTeam(teamId)?.DisplayName ?? teamId}.");
                else
                    SendMessage(sender, reason);
                // OnServerTeamChanged pushes the new state on success.
                break;

            case Op.Stamp:
                PieceTeams.OnStampRequest(pkg.ReadZDOID(), characterId);
                break;

            case Op.Admin:
                if (!PeerIdentity.IsAdmin(sender))
                {
                    SendMessage(sender, "Admins only.");
                    break;
                }
                PeerIdentity.TryGetPosition(sender, out var adminPos);
                SendMessage(sender, TeamsAdmin.Run(pkg.ReadString(), name, adminPos));
                break;
        }
    }

    /// <summary>
    /// Server: make every connected player's published team match the registry. The owning client writes it, so a
    /// modified client could claim another team; this puts the truth back within a few seconds.
    /// </summary>
    public static void GuardPublishedTeams()
    {
        if (!IsAuthority || ZDOMan.instance == null)
            return;
        var registry = TeamRegistry.Instance;
        var local = Player.m_localPlayer;
        if (local != null)
            Correct(local.GetComponent<ZNetView>()?.GetZDO(), registry.GetTeamId(local.GetPlayerID()));
        foreach (var peer in ZNet.instance.GetPeers())
        {
            if (PeerIdentity.TryResolve(peer.m_uid, out var characterId, out _))
                Correct(ZDOMan.instance.GetZDO(peer.m_characterID), registry.GetTeamId(characterId));
        }

        static void Correct(ZDO? zdo, string? team)
        {
            if (zdo == null)
                return;
            var want = team ?? "";
            if (zdo.GetString(TeamsAPI.TeamZdoKey, "") != want)
                zdo.Set(TeamsAPI.TeamZdoKey, want);
        }
    }

    static void OnServerTeamChanged(long characterId, string? oldTeam, string? newTeam)
    {
        var sender = PeerIdentity.FindSender(characterId);
        if (sender != null)
            SendState(sender.Value, characterId);
        // A character's very first team: put them at that team's spawn point.
        if (oldTeam == null && newTeam != null && TeamsConfig.MoveToSpawnOnFirstTeam)
        {
            var spawn = TeamRegistry.Instance.GetSpawnPoint(characterId);
            if (spawn != null)
            {
                TeleportsSent[characterId] = spawn.Value;
                if (sender != null)
                    SendTeleport(sender.Value, characterId, spawn.Value);
            }
        }
    }

    /// <summary>Last first-team move the server ordered, by character (for tests).</summary>
    internal static readonly Dictionary<long, Vector3> TeleportsSent = new();

    static void SendTeleport(long target, long characterId, Vector3 point)
    {
        var pkg = new ZPackage();
        pkg.Write((int)Op.Teleport);
        pkg.Write(characterId);
        pkg.Write(point.x);
        pkg.Write(point.y);
        pkg.Write(point.z);
        _rpc.SendPackage(target, pkg);
    }

    /// <summary>Server: every connected player gets the current team list again (a team definition changed).</summary>
    public static void BroadcastState()
    {
        if (!IsAuthority || ZNet.instance == null)
            return;
        if (!ZNet.instance.IsDedicated() && PeerIdentity.TryResolve(PeerIdentity.ServerPeerId(), out var hostId, out _))
            SendState(PeerIdentity.ServerPeerId(), hostId);
        foreach (var peer in ZNet.instance.GetPeers())
            if (PeerIdentity.TryResolve(peer.m_uid, out var id, out _))
                SendState(peer.m_uid, id);
    }

    static void SendNotReady(long target)
    {
        var pkg = new ZPackage();
        pkg.Write((int)Op.State);
        pkg.Write(false);
        _rpc.SendPackage(target, pkg);
    }

    /// <summary>
    /// A character says hello (it just loaded in). Auto-balance gives it a team; under PlayerChoice a character with no team that has not
    /// been shown the picker yet is offered it, and the server writes the flag so it is only offered once. Returns "show the picker".
    /// </summary>
    internal static bool ProcessHello(long characterId, string name)
    {
        var registry = TeamRegistry.Instance;
        if (registry.GetTeamId(characterId) == null && TeamsConfig.AssignmentMode == AssignmentMode.AutoBalance)
            registry.TryAutoAssign(characterId, name);
        registry.Touch(characterId);
        var offer = TeamsConfig.AssignmentMode == AssignmentMode.PlayerChoice && TeamsConfig.FirstJoinPicker
                    && registry.GetTeamId(characterId) == null && !registry.WasOffered(characterId);
        if (offer)
            registry.SetOffered(characterId, true);
        return offer;
    }

    /// <summary>Server: show the team picker to this character again (admin command "teams offer").</summary>
    internal static bool OfferPicker(long characterId)
    {
        var sender = PeerIdentity.FindSender(characterId);
        if (sender == null)
            return false;
        SendState(sender.Value, characterId, true);
        return true;
    }

    static void SendState(long target, long characterId, bool offerPicker = false)
    {
        var registry = TeamRegistry.Instance;
        var own = registry.GetTeamId(characterId);
        var canSwitch = TeamsConfig.AssignmentMode == AssignmentMode.PlayerChoice
                        && (own == null || (TeamsConfig.PlayersCanSwitchTeams && registry.GetTeam(own)?.Locked != true));

        var pkg = new ZPackage();
        pkg.Write((int)Op.State);
        pkg.Write(true);
        pkg.Write(own ?? "");
        pkg.Write((int)TeamsConfig.AssignmentMode);
        pkg.Write(canSwitch);
        pkg.Write(offerPicker);
        pkg.Write(registry.Teams.Count);
        foreach (var t in registry.Teams)
        {
            pkg.Write(t.Id);
            pkg.Write(t.DisplayName);
            pkg.Write(t.Color);
            pkg.Write(t.Joinable);
            pkg.Write(t.Locked);
            pkg.Write(t.HasSpawn);
            pkg.Write(t.SpawnX);
            pkg.Write(t.SpawnY);
            pkg.Write(t.SpawnZ);
            pkg.Write(t.Icon ?? "shield");
            pkg.Write(t.GlowColor ?? "");
        }

        DeathRulesState.EnsureLoadedOnServer(_log);
        var rules = DeathRulesState.Current;
        pkg.Write(rules.Enabled);
        pkg.Write(rules.AppliesTo ?? "EnemyTeamKill");
        pkg.Write(rules.KeepEquipped);
        pkg.Write(rules.EnemyLootPercent);
        pkg.Write(DeathRulesState.Join(rules.Keep));
        pkg.Write(DeathRulesState.Join(rules.EnemyLootable));
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
