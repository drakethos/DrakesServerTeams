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
}
