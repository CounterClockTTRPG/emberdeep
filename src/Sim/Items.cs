using System;
using System.Collections.Generic;
using System.Linq;

namespace Emberdeep;

/// <summary>A rolled stat on an item.</summary>
public sealed class Mod
{
    public Stat Stat;
    public int Value;
    public Mod() { }
    public Mod(Stat s, int v) { Stat = s; Value = v; }
}

/// <summary>One item: a base, a rarity, the affixes it rolled. Stacks (potions, scrolls, gold) carry a count.</summary>
public sealed class Item
{
    public string Base;
    public Rarity Rarity;
    /// <summary>The level it dropped at: decides how strong its affixes rolled.</summary>
    public int Level = 1;
    public string Name;
    public List<Mod> Mods = new();
    public int Count = 1;

    public Item() { }
    public Item(string baseId, int count = 1) { Base = baseId; Count = count; Name = Data.BaseOf(baseId)?.Name ?? baseId; }

    public Base B => Data.BaseOf(Base);

    public int Get(Stat s) { int v = 0; foreach (Mod m in Mods) if (m.Stat == s) v += m.Value; return v; }

    /// <summary>The level the hero needs to wear it.</summary>
    public int Requires => B == null ? 1 : Math.Max(B.Level, Rarity switch { Rarity.Rare => Level - 3, Rarity.Magic => Level - 6, _ => 0 });

    /// <summary>Its armour with its own affixes counted (the hero's other bonuses come on top).</summary>
    public int Armour => B == null ? 0 : (int)Math.Round((B.Armour + Get(Stat.Armour)) * (1 + Get(Stat.ArmourPct) / 100.0));

    public (int lo, int hi) Damage
    {
        get
        {
            if (B == null || !B.Weapon) return (0, 0);
            double pct = 1 + Get(Stat.DmgPct) / 100.0;
            int lo = (int)Math.Round((B.DMin + Get(Stat.DmgMin)) * pct, MidpointRounding.AwayFromZero), hi = (int)Math.Round((B.DMax + Get(Stat.DmgMax)) * pct, MidpointRounding.AwayFromZero);
            return (lo, Math.Max(lo, hi));
        }
    }

    /// <summary>What a merchant pays for it; buying costs four times as much.</summary>
    public int Worth => B == null ? 0 : B.Kind == Kind.Gold ? Count
        : Math.Max(1, (int)(B.Price * Count * Rarity switch { Rarity.Magic => 2.5, Rarity.Rare => 5, Rarity.Unique => 10, _ => 1 } * (1 + Level / 20.0)) / 4);

    public string Title => B?.Stacks == true && Count > 1 ? $"{Name} ({Count})" : Name;

    /// <summary>The tooltip's lines after the name: the base's numbers, then the affixes.</summary>
    public IEnumerable<string> Lines()
    {
        Base b = B;
        if (b == null) yield break;
        if (b.Weapon)
        {
            var (lo, hi) = Damage;
            yield return $"{(b.TwoHanded ? "Two-handed" : "One-handed")} {b.Kind.ToString().ToLowerInvariant()}, damage {lo}-{hi}";
            if (b.SpellPct > 0) yield return $"+{b.SpellPct}% spell damage";
        }
        if (b.Armour > 0) yield return $"Armour {Armour}";
        if (b.Block > 0) yield return $"Block {b.Block}%";
        if (b.Kind == Kind.PotLife) yield return $"Restores {Data.PotionHeal} life";
        if (b.Kind == Kind.PotMana) yield return $"Restores {Data.PotionHeal} mana";
        if (b.Kind == Kind.Scroll) yield return "Opens a way back to Emberwick";
        foreach (Mod m in Mods) yield return Data.Describe(m.Stat, m.Value);
        if (b.Wearable && Requires > 1) yield return $"Requires level {Requires}";
    }
}

/// <summary>Making items: rolling a base and its affixes for a level, and what monsters drop.</summary>
public static class Loot
{
    /// <summary>A wearable item of a base, at a level, with the rarity given (affixes rolled from the RNG).</summary>
    public static Item Make(string baseId, Rarity rarity, int level, Rng r)
    {
        var it = new Item(baseId) { Rarity = rarity, Level = Math.Max(1, level) };
        Base b = it.B;
        if (b == null || !b.Wearable) { it.Rarity = Rarity.Plain; return it; }
        bool jewel = b.Kind is Kind.Ring or Kind.Amulet;
        if (jewel && rarity == Rarity.Plain) it.Rarity = rarity = Rarity.Magic;
        int count = rarity switch { Rarity.Magic => r.Int(1, 3), Rarity.Rare => r.Int(3, 6), _ => 0 };
        string where = Data.Where(b);
        var pool = Data.Affixes.Where(a => a.Level <= it.Level && a.Where.IndexOfAny(where.ToCharArray()) >= 0).ToList();
        Affix prefix = null, suffix = null;
        var used = new HashSet<Stat>();
        for (int k = 0; k < count && pool.Count > 0; k++)
        {
            // Magic items take at most one prefix and one suffix; rares up to three of each.
            int pre = it.Mods.Count(m => pool.Any(a => a.Prefix && a.Stat == m.Stat)), suf = it.Mods.Count - pre;
            int cap = rarity == Rarity.Magic ? 1 : 3;
            var can = pool.Where(a => !used.Contains(a.Stat) && (a.Prefix ? pre < cap : suf < cap)).ToList();
            if (can.Count == 0) break;
            Affix a = can[r.Int(0, can.Count)];
            used.Add(a.Stat);
            // Stronger the deeper it dropped: the range's top grows with the item level (full at level 30).
            double t = Math.Clamp((it.Level - a.Level) / 30.0 + 0.15, 0.15, 1);
            int hi = a.Lo + (int)Math.Round((a.Hi - a.Lo) * t);
            int v = r.Int(a.Lo, Math.Max(a.Lo, hi) + 1);
            it.Mods.Add(new Mod(a.Stat, v));
            if (a.Prefix) prefix ??= a; else suffix ??= a;
        }
        it.Name = rarity switch
        {
            Rarity.Magic => string.Join(" ", new[] { prefix?.Name, b.Name, suffix?.Name }.Where(s => s != null)),
            Rarity.Rare => $"{Data.RareFirst[r.Int(0, Data.RareFirst.Length)]} {Data.RareSecond(b.Kind, r.Int(0, 600))}",
            _ => b.Name,
        };
        return it;
    }

    /// <summary>A base fit for a level: one the level could wear, the newer ones more often.</summary>
    public static Base PickBase(int level, Rng r, Func<Base, bool> filter = null)
    {
        var can = Data.Bases.Where(b => b.Wearable && b.Level <= level + 2 && (filter == null || filter(b))).ToList();
        if (can.Count == 0) can = Data.Bases.Where(b => b.Wearable).ToList();
        double total = can.Sum(b => 1.0 + b.Level / 4.0);
        double x = r.Float() * total;
        foreach (Base b in can) { x -= 1.0 + b.Level / 4.0; if (x <= 0) return b; }
        return can[^1];
    }

    /// <summary>
    /// What one monster leaves behind: nothing most of the time, gold, a draught, a scroll, or something to wear.
    /// <paramref name="quality"/> is 1 for a plain monster, more for a champion or a boss (more rolls, better rarity).
    /// </summary>
    public static List<Item> Drop(int level, double quality, int magicFind, int goldFind, Rng r)
    {
        var got = new List<Item>();
        int rolls = quality >= 6 ? 5 : quality >= 2 ? 2 : 1;
        for (int k = 0; k < rolls; k++)
        {
            double x = r.Float();
            double nothing = quality >= 2 ? 0.1 : 0.55;
            if (x < nothing) continue;
            x = r.Float();
            if (x < 0.35) got.Add(new Item("gold", Math.Max(1, (int)((r.Int(3, 12) + level * r.Int(2, 6)) * (1 + goldFind / 100.0)))));
            else if (x < 0.55) got.Add(new Item(r.Chance(0.6) ? "pot-hp" : "pot-mp"));
            else if (x < 0.6) got.Add(new Item("scroll"));
            else got.Add(Make(PickBase(level, r).Id, RollRarity(quality, magicFind, r), level, r));
        }
        return got;
    }

    public static Rarity RollRarity(double quality, int magicFind, Rng r)
    {
        double mf = 1 + magicFind / 100.0;
        double x = r.Float();
        double rare = Math.Min(0.5, 0.04 * quality * mf), magic = Math.Min(0.85, 0.25 * Math.Sqrt(quality) * mf);
        return x < rare ? Rarity.Rare : x < rare + magic ? Rarity.Magic : Rarity.Plain;
    }

    /// <summary>The smith's wares: a dozen things for the hero's level, re-rolled each time the hero comes back up.</summary>
    public static List<Item> SmithStock(int level, Rng r)
    {
        var list = new List<Item>();
        for (int k = 0; k < 12; k++)
        {
            Base b = PickBase(level, r, x => x.Kind is not (Kind.Ring or Kind.Amulet));
            list.Add(Make(b.Id, r.Chance(0.3) ? Rarity.Magic : Rarity.Plain, level, r));
        }
        return list;
    }

    /// <summary>The healer's wares: always the same.</summary>
    public static List<Item> HealerStock() => new() { new Item("pot-hp"), new Item("pot-mp"), new Item("scroll") };
}
