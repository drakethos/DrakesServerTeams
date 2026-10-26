using System.Reflection;
using HarmonyLib;

namespace DrakesServerTeams;

/// <summary>
/// Server-side: who is behind an RPC sender. The character ID is read from the player's own ZDO, never from
/// the message, so a client cannot claim somebody else's ID.
/// </summary>
public static class PeerIdentity
{
    // Same rule as DrakePlayerIdentity in Libs, but for a peer instead of a Player: the admin list holds
    // platform host IDs, and ZNet.m_adminList / ListContainsId are private in the real game (reflection only).
    static readonly FieldInfo? AdminListField = AccessTools.Field(typeof(ZNet), "m_adminList");
    static readonly MethodInfo? ListContainsIdMethod = AccessTools.Method(typeof(ZNet), "ListContainsId");
    static readonly int PlayerIdHash = "playerID".GetStableHashCode();

    /// <summary>
    /// The server's peer uid: our own session id on the server, the server peer's uid on a client.
    /// Same as ZRoutedRpc.GetServerPeerID, which is private in the real game.
    /// </summary>
    public static long ServerPeerId()
    {
        if (ZNet.instance == null)
            return 0;
        return ZNet.instance.IsServer() ? ZNet.GetUID() : ZNet.instance.GetServerPeer()?.m_uid ?? 0;
    }

    public static bool IsHostLocal(long sender) =>
        ZNet.instance != null && sender == ServerPeerId();

    /// <summary>False while the sender's character has not spawned yet.</summary>
    public static bool TryResolve(long sender, out long characterId, out string name)
    {
        characterId = 0;
        name = "";
        if (ZNet.instance == null)
            return false;

        if (IsHostLocal(sender))
        {
            // Listen server: the host plays locally and has no ZNetPeer.
            var local = Player.m_localPlayer;
            if (local == null)
                return false;
            characterId = local.GetPlayerID();
            name = local.GetPlayerName();
            return characterId != 0;
        }

        var peer = ZNet.instance.GetPeer(sender);
        if (peer == null || ZDOMan.instance == null)
            return false;
        var zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
        if (zdo == null)
            return false;
        characterId = zdo.GetLong(PlayerIdHash, 0L);
        name = peer.m_playerName ?? "";
        return characterId != 0;
    }

    /// <summary>Where the sender's character stands (host: the local player, a client: its player ZDO).</summary>
    public static bool TryGetPosition(long sender, out UnityEngine.Vector3 position)
    {
        position = default;
        if (ZNet.instance == null)
            return false;
        if (IsHostLocal(sender))
        {
            if (Player.m_localPlayer == null)
                return false;
            position = Player.m_localPlayer.transform.position;
            return true;
        }
        var peer = ZNet.instance.GetPeer(sender);
        var zdo = peer != null && ZDOMan.instance != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
        if (zdo == null)
            return false;
        position = zdo.GetPosition();
        return true;
    }

    public static bool IsAdmin(long sender)
    {
        if (ZNet.instance == null)
            return false;
        if (IsHostLocal(sender))
            return true;
        try
        {
            var hostId = ZNet.instance.GetPeer(sender)?.m_socket?.GetHostName();
            var adminList = AdminListField?.GetValue(ZNet.instance);
            return adminList != null
                   && !string.IsNullOrEmpty(hostId)
                   && ListContainsIdMethod?.Invoke(ZNet.instance, new[] { adminList, hostId }) is true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The RPC sender (peer uid) of a connected character, or null when offline.</summary>
    public static long? FindSender(long characterId)
    {
        if (ZNet.instance == null)
            return null;
        if (Player.m_localPlayer != null && !ZNet.instance.IsDedicated() && Player.m_localPlayer.GetPlayerID() == characterId)
            return ServerPeerId();
        foreach (var peer in ZNet.instance.GetPeers())
        {
            if (TryResolve(peer.m_uid, out var id, out _) && id == characterId)
                return peer.m_uid;
        }
        return null;
    }
}
