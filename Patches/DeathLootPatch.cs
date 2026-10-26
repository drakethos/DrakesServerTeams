using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// Death rules on the dying player's client. Player.CreateTombStone decides whether the rules apply (who killed us, which
/// team); Inventory.MoveInventoryToGrave then runs the plan: kept items are set aside and put back afterwards, lootable
/// items are dropped on the ground, the rest goes into the grave as usual.
/// </summary>
static class DeathLoot
{
    static readonly FieldInfo? LastHit = AccessTools.Field(typeof(Character), "m_lastHit");

    /// <summary>Set while a death is being processed.</summary>
    internal static Player? Active;
    internal static DeathRules? ActiveRules;
    internal static readonly HashSet<ItemDrop.ItemData> WornAtDeath = new();
    /// <summary>Where the last death happened (for tests).</summary>
    internal static Vector3 LastPoint;
    /// <summary>Test tooling: replaces "who dealt the last hit". (isPlayer, team)</summary>
    internal static Func<Player, (bool isPlayer, string? team)?>? KillerOverride;
    /// <summary>What the last death did (for tests and logs): "keep=2 drop=1 grave=3" or "rules did not apply".</summary>
    internal static string LastOutcome = "none yet";

    internal static (bool isPlayer, string? team) Killer(Player dying)
    {
        var forced = KillerOverride?.Invoke(dying);
        if (forced != null)
            return forced.Value;
        var hit = LastHit?.GetValue(dying) as HitData;
        var attacker = hit?.GetAttacker();
        return attacker is Player p ? (true, TeamsAPI.GetTeamId(p)) : (false, null);
    }

    internal static void Begin(Player dying)
    {
        Active = null;
        ActiveRules = null;
        var rules = DeathRulesState.Current;
        if (!rules.Enabled)
        {
            LastOutcome = "rules off";
            return;
        }
        var (isPlayer, team) = Killer(dying);
        if (!TeamLoot.Applies(rules, TeamsAPI.GetTeamId(dying), isPlayer, team))
        {
            LastOutcome = "rules did not apply";
            return;
        }
        Active = dying;
        ActiveRules = rules;
        LastPoint = dying.transform.position;
        WornAtDeath.Clear();
        foreach (var worn in dying.GetInventory().GetEquippedItems())
            WornAtDeath.Add(worn);
    }

    internal static void End()
    {
        WornAtDeath.Clear();
        Active = null;
        ActiveRules = null;
    }
}

[HarmonyPatch(typeof(Player), "CreateTombStone")]
static class CreateTombStonePatch
{
    static void Prefix(Player __instance) => DeathLoot.Begin(__instance);

    static Exception? Finalizer(Exception? __exception)
    {
        DeathLoot.End();
        return __exception;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveInventoryToGrave))]
static class MoveToGravePatch
{
    sealed class State
    {
        public Inventory Source = null!;
        public readonly List<ItemDrop.ItemData> Kept = new();
        public readonly List<bool> WasEquipped = new();
    }

    static void Prefix(Inventory original, out object? __state)
    {
        __state = null;
        var player = DeathLoot.Active;
        var rules = DeathLoot.ActiveRules;
        if (player == null || rules == null || player.GetInventory() != original)
            return;

        var state = new State { Source = original };
        var drops = 0;
        var graves = 0;
        var at = player.transform.position + Vector3.up * 0.6f;
        foreach (var d in TeamLoot.Plan(new List<ItemDrop.ItemData>(original.GetAllItems()), rules, DeathLoot.WornAtDeath))
        {
            switch (d.Fate)
            {
                case LootFate.Keep:
                    state.Kept.Add(d.Item);
                    state.WasEquipped.Add(d.Item.m_equipped || DeathLoot.WornAtDeath.Contains(d.Item));
                    original.RemoveItem(d.Item);
                    break;
                case LootFate.Drop:
                    var jitter = UnityEngine.Random.insideUnitCircle * 0.8f;
                    ItemDrop.DropItem(d.Item, d.DropAmount, at + new Vector3(jitter.x, 0f, jitter.y), Quaternion.identity);
                    original.RemoveItem(d.Item, d.DropAmount);
                    drops++;
                    if (d.DropAmount < d.Item.m_stack)
                        graves++; // the remainder of the stack goes to the grave
                    break;
                default:
                    graves++;
                    break;
            }
        }
        DeathLoot.LastOutcome = $"keep={state.Kept.Count} drop={drops} grave={graves}";
        __state = state;
    }

    // Always runs, so kept items come back even if the game's own code threw.
    static Exception? Finalizer(Exception? __exception, object? __state)
    {
        if (__state is State state)
        {
            for (var i = 0; i < state.Kept.Count; i++)
            {
                state.Kept[i].m_equipped = state.WasEquipped[i]; // wear it again when the character respawns
                state.Source.AddItem(state.Kept[i]);
            }
            try
            {
                if (DeathLoot.Active != null)
                    Game.instance?.GetPlayerProfile()?.SavePlayerData(DeathLoot.Active);
            }
            catch (Exception ex)
            {
                TeamsNet.Warn($"Death rules: could not save the kept items: {ex.Message}");
            }
        }
        return __exception;
    }
}
