using System;
using System.Collections.Generic;

namespace DrakesServerTeams;

/// <summary>What to do with one item when its owner dies under the death rules.</summary>
public enum LootFate
{
    Grave,
    Keep,
    Drop,
}

public readonly struct LootDecision
{
    public readonly ItemDrop.ItemData Item;
    public readonly LootFate Fate;
    /// <summary>For Drop: how many of the stack go on the ground (the rest goes to the grave).</summary>
    public readonly int DropAmount;

    public LootDecision(ItemDrop.ItemData item, LootFate fate, int dropAmount = 0)
    {
        Item = item;
        Fate = fate;
        DropAmount = dropAmount;
    }
}

/// <summary>The death-rules decision, kept apart from the game hooks so it can be tested on its own.</summary>
public static class TeamLoot
{
    /// <summary>Does this entry name the item? "*", "equipped", "type:Material", or a prefab name.</summary>
    public static bool Matches(ItemDrop.ItemData item, string entry, ISet<ItemDrop.ItemData>? wornAtDeath = null)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return false;
        var e = entry.Trim();
        if (e == "*")
            return true;
        if (e.Equals("equipped", StringComparison.OrdinalIgnoreCase))
            return IsWorn(item, wornAtDeath);
        if (e.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            return string.Equals(item.m_shared.m_itemType.ToString(), e.Substring(5).Trim(), StringComparison.OrdinalIgnoreCase);
        var prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
        return string.Equals(prefab, e, StringComparison.OrdinalIgnoreCase);
    }

    // The game unequips everything before it builds the grave, so what was worn is recorded when the death starts.
    static bool IsWorn(ItemDrop.ItemData item, ISet<ItemDrop.ItemData>? wornAtDeath) =>
        item.m_equipped || (wornAtDeath != null && wornAtDeath.Contains(item));

    static bool MatchesAny(ItemDrop.ItemData item, List<string> entries, ISet<ItemDrop.ItemData>? wornAtDeath)
    {
        foreach (var entry in entries)
            if (Matches(item, entry, wornAtDeath))
                return true;
        return false;
    }

    /// <summary>One decision per item. Keep beats Drop beats Grave; quest items always stay (the game never loses those).</summary>
    public static List<LootDecision> Plan(IEnumerable<ItemDrop.ItemData> items, DeathRules rules, ISet<ItemDrop.ItemData>? wornAtDeath = null)
    {
        var plan = new List<LootDecision>();
        foreach (var item in items)
        {
            if (rules.KeepEquipped && IsWorn(item, wornAtDeath) || MatchesAny(item, rules.Keep, wornAtDeath))
                plan.Add(new LootDecision(item, LootFate.Keep));
            else if (MatchesAny(item, rules.EnemyLootable, wornAtDeath))
            {
                var amount = Math.Max(1, (int)Math.Ceiling(item.m_stack * rules.EnemyLootPercent / 100.0));
                plan.Add(new LootDecision(item, LootFate.Drop, Math.Min(amount, item.m_stack)));
            }
            else
                plan.Add(new LootDecision(item, LootFate.Grave));
        }
        return plan;
    }

    /// <summary>Do the rules apply to this death? killerIsPlayer / killerTeam describe whoever dealt the last hit.</summary>
    public static bool Applies(DeathRules rules, string? ownTeam, bool killerIsPlayer, string? killerTeam)
    {
        if (!rules.Enabled)
            return false;
        switch (rules.Scope)
        {
            case DeathRuleScope.Always:
                return true;
            case DeathRuleScope.AnyPlayerKill:
                return killerIsPlayer;
            default:
                return killerIsPlayer && ownTeam != null && killerTeam != null
                       && !string.Equals(ownTeam, killerTeam, StringComparison.OrdinalIgnoreCase);
        }
    }
}
