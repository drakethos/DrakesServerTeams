#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using DrakesServerTeams.Testing;

namespace DrakesServerTeams.Spikes;

/// <summary>
/// SPIKE (debug builds only, throwaway): how would "crumbling" structures and a ward shield work in Valheim 1.0?
/// Stage 1 is measurement: how a hit on a piece inside a ward behaves today, where the ward's immunity lives, what the broken state
/// of a piece looks like. Stage 2 (below, off until switched on) prototypes the crumble and the shield. Moves to the War extension if it works.
/// </summary>
internal static class WarSpike
{
    static Player P => Player.m_localPlayer ?? throw new InvalidOperationException("no local player");

    static string S(Dictionary<string, object?> a, string key) =>
        a.TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";

    static float F(Dictionary<string, object?> a, string key, float fallback = 0f) =>
        float.TryParse(S(a, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;

    static Player PlayerNamed(string name)
    {
        if (name.Length == 0 || name.Equals("me", StringComparison.OrdinalIgnoreCase))
            return P;
        foreach (var p in Player.GetAllPlayers())
            if (p != null && string.Equals(p.GetPlayerName(), name, StringComparison.OrdinalIgnoreCase))
                return p;
        throw new InvalidOperationException($"no player named '{name}'");
    }

    static WearNTear Wnt(GameObject? go) =>
        (go != null ? go.GetComponentInParent<WearNTear>() ?? go.GetComponentInChildren<WearNTear>() : null)
        ?? throw new InvalidOperationException("that object has no WearNTear");

    static readonly FieldInfo? NviewField = AccessTools.Field(typeof(WearNTear), "m_nview");
    static readonly FieldInfo? MaxHealth = AccessTools.Field(typeof(WearNTear), "m_health");
    static readonly FieldInfo? Colliders = AccessTools.Field(typeof(WearNTear), "m_colliders");

    static ZNetView View(WearNTear w) => (ZNetView)NviewField!.GetValue(w);

    /// <summary>Current and maximum health of a piece: "hp/max (pct)".</summary>
    [TestHook("spike.hp")]
    static string Hp(GameObject? piece)
    {
        var w = Wnt(piece);
        var max = (float)MaxHealth!.GetValue(w);
        var hp = View(w).GetZDO().GetFloat("health", max);
        return string.Format(CultureInfo.InvariantCulture, "{0:0.#}/{1:0.#}", hp, max);
    }

    /// <summary>Switch a vanilla ward on or off (a fresh ward starts off). Returns whether it is enabled.</summary>
    [TestHook("spike.enableWard")]
    static bool EnableWard(GameObject? ward, Dictionary<string, object?> args)
    {
        var area = ward != null ? ward.GetComponentInParent<PrivateArea>() ?? ward.GetComponentInChildren<PrivateArea>() : null;
        if (area == null)
            throw new InvalidOperationException("that is not a ward");
        var on = !S(args, "on").Equals("false", StringComparison.OrdinalIgnoreCase);
        AccessTools.Method(typeof(PrivateArea), "SetEnabled", new[] { typeof(bool) })!.Invoke(area, new object[] { on });
        return (bool)AccessTools.Method(typeof(PrivateArea), "IsEnabled")!.Invoke(area, null)!;
    }

    /// <summary>
    /// Hit a piece the way a weapon does: WearNTear.Damage with a HitData from the named player. Returns "before -> after" and whether
    /// the piece still exists. Blunt damage "dmg" (default 50), tool tier high enough for any piece.
    /// </summary>
    [TestHook("spike.hit")]
    static string Hit(GameObject? piece, Dictionary<string, object?> args)
    {
        var w = Wnt(piece);
        var before = Hp(w.gameObject);
        var attacker = PlayerNamed(S(args, "attacker"));
        var hit = new HitData
        {
            m_point = w.transform.position + Vector3.up * 0.5f,
            m_dir = (w.transform.position - attacker.transform.position).normalized,
            m_attacker = attacker.GetZDOID(),
            m_toolTier = 100,
        };
        hit.m_damage.m_blunt = F(args, "dmg", 50f);
        hit.m_damage.m_chop = hit.m_damage.m_blunt;
        w.Damage(hit);
        try
        {
            return before + " -> " + Hp(w.gameObject);
        }
        catch (Exception)
        {
            return before + " -> destroyed";
        }
    }

    /// <summary>The ward's own opinion: does the named player have access at this piece's position? (CheckAccess as that player.)</summary>
    [TestHook("spike.wardAccess")]
    static string WardAccess(GameObject? piece, Dictionary<string, object?> args)
    {
        var who = PlayerNamed(S(args, "player"));
        var pos = piece != null ? piece.transform.position : who.transform.position;
        // CheckAccess is static and tests the *local* player, so the caller plays as the player first.
        var m = AccessTools.Method(typeof(PrivateArea), "CheckAccess", new[] { typeof(Vector3), typeof(float), typeof(bool), typeof(bool) });
        return m!.Invoke(null, new object[] { pos, 0f, false, false }) is true ? "access" : "blocked";
    }

    /// <summary>What the piece's broken state looks like and what would have to change to make it walkable: renderers and colliders.</summary>
    [TestHook("spike.pieceParts")]
    static string PieceParts(GameObject? piece)
    {
        var w = Wnt(piece);
        var sb = new StringBuilder();
        foreach (var name in new[] { "m_new", "m_worn", "m_broken" })
        {
            var go = AccessTools.Field(typeof(WearNTear), name)?.GetValue(w) as GameObject;
            sb.Append(name).Append('=').Append(go != null ? go.name + (go.activeSelf ? "(on)" : "(off)") : "null").Append(' ');
        }
        var cols = Colliders?.GetValue(w) as Collider[];
        sb.Append("colliders=").Append(cols?.Length ?? -1).Append(" triggers=");
        var trig = 0;
        if (cols != null)
            foreach (var c in cols)
                if (c != null && c.isTrigger) trig++;
        sb.Append(trig);
        sb.Append(" piece=").Append(w.GetComponent<Piece>() != null);
        return sb.ToString();
    }

    /// <summary>The percentage the game reports (what drives the new/worn/broken visual).</summary>
    [TestHook("spike.healthPercent")]
    static string HealthPercent(GameObject? piece) =>
        Wnt(piece).GetHealthPercentage().ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Set a piece's health directly (ZDO), to look at its visual stages. Returns the new "hp/max".</summary>
    [TestHook("spike.setHp")]
    static string SetHp(GameObject? piece, Dictionary<string, object?> args)
    {
        var w = Wnt(piece);
        var view = View(w);
        view.ClaimOwnership();
        view.GetZDO().Set("health", F(args, "hp", 1f));
        AccessTools.Method(typeof(WearNTear), "UpdateVisual", new[] { typeof(bool) })!.Invoke(w, new object[] { false });
        return Hp(piece);
    }
}
#endif
