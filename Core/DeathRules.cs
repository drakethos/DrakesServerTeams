using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;

namespace DrakesServerTeams;

/// <summary>When the death rules apply.</summary>
public enum DeathRuleScope
{
    /// <summary>The dying player was killed by a player of another team (both on a team).</summary>
    EnemyTeamKill,
    /// <summary>Killed by any player.</summary>
    AnyPlayerKill,
    /// <summary>Every death.</summary>
    Always,
}

/// <summary>
/// What happens to a player's items when they die, per the server's deathrules.json. Off unless the admin turns it on.
/// Each item falls in one of three places:
/// KEEP: stays with the player (equipped gear, plus anything in <see cref="Keep"/>);
/// ENEMY LOOT: dropped on the ground where they died, so anyone (the enemy that killed them) can pick it up;
/// GRAVE: everything else goes into the grave as usual.
/// </summary>
/// <remarks>Public fields on purpose: the file is read and written by field (Jotunn SimpleJson).</remarks>
[Serializable]
public sealed class DeathRules
{
    public bool Enabled;
    /// <summary>EnemyTeamKill, AnyPlayerKill or Always.</summary>
    public string AppliesTo = "EnemyTeamKill";
    /// <summary>Gear the player has equipped stays with them.</summary>
    public bool KeepEquipped = true;
    /// <summary>Also stays with the player. Entries: a prefab name (Coins), type:Material, equipped, or * for everything.</summary>
    public List<string> Keep = new();
    /// <summary>Dropped on the ground for the killer to take. Same entry forms as <see cref="Keep"/>.</summary>
    public List<string> EnemyLootable = new() { "Coins" };
    /// <summary>How much of each lootable stack is dropped (1-100). The rest goes into the grave.</summary>
    public int EnemyLootPercent = 100;

    public DeathRuleScope Scope =>
        Enum.TryParse<DeathRuleScope>(AppliesTo, true, out var s) ? s : DeathRuleScope.EnemyTeamKill;

    public DeathRules Copy() => new()
    {
        Enabled = Enabled,
        AppliesTo = AppliesTo,
        KeepEquipped = KeepEquipped,
        Keep = new List<string>(Keep ?? new List<string>()),
        EnemyLootable = new List<string>(EnemyLootable ?? new List<string>()),
        EnemyLootPercent = EnemyLootPercent,
    };

    public string Describe() =>
        $"{(Enabled ? "on" : "off")}, applies to {Scope}, keep equipped: {KeepEquipped}, keep: [{string.Join(", ", Keep ?? new List<string>())}], "
        + $"enemy loot: [{string.Join(", ", EnemyLootable ?? new List<string>())}] {EnemyLootPercent}%";
}

/// <summary>The rules in force: the server's file on the authority, the server's copy on a client.</summary>
public static class DeathRulesState
{
    public static DeathRules Current { get; private set; } = new();
    static bool _loaded;

    public static void EnsureLoadedOnServer(ManualLogSource log)
    {
        if (_loaded)
            return;
        _loaded = true;
        Current = TeamStore.LoadRules(log);
        Normalize(Current);
    }

    /// <summary>Client: the server sent its rules.</summary>
    public static void Apply(DeathRules rules)
    {
        Normalize(rules);
        Current = rules;
        _loaded = true;
    }

    public static void Save(ManualLogSource log) => TeamStore.SaveRules(Current, log);

    static void Normalize(DeathRules r)
    {
        r.Keep ??= new List<string>();
        r.EnemyLootable ??= new List<string>();
        r.EnemyLootPercent = Math.Max(1, Math.Min(100, r.EnemyLootPercent));
    }

    internal static string Join(List<string> list) => string.Join(",", list);

    internal static List<string> Split(string text)
    {
        var list = new List<string>();
        foreach (var part in (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = part.Trim();
            if (t.Length > 0)
                list.Add(t);
        }
        return list;
    }

    internal static string Pct(int v) => v.ToString(CultureInfo.InvariantCulture);
}
