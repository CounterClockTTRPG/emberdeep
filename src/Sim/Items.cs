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
    /// <summary>A treasure map: which lair it opens (Data.Lairs), and the seed its lair is made from.</summary>
    public int Lair = -1;
    public ulong MapSeed;
    /// <summary>A gem's grade: 0 chipped, 1 plain, 2 flawless.</summary>
    public int Grade;
    /// <summary>Sockets an item has, and the gems set in them (base id and grade, e.g. "gem-ruby:2").</summary>
    public int Sockets;
    public List<string> Gems = new();

    public Item() { }
    public Item(string baseId, int count = 1) { Base = baseId; Count = count; Name = Data.BaseOf(baseId)?.Name ?? baseId; }

    public Base B => Data.BaseOf(Base);

    /// <summary>A stat from the item's own affixes and the gems set in it.</summary>
    public int Get(Stat s)
    {
        int v = 0;
        foreach (Mod m in Mods) if (m.Stat == s) v += m.Value;
        foreach (var (stat, value) in GemMods()) if (stat == s) v += value;
        return v;
    }

    /// <summary>What the gems set in this item give, by where it is (a weapon, armour, a shield).</summary>
    public IEnumerable<(Stat stat, int value)> GemMods()
    {
        Base b = B;
        if (b == null || Gems.Count == 0) yield break;
        foreach (string g in Gems)
        {
            var (id, grade) = GemOf(g);
            Data.GemDef d = Data.Gem(id);
            if (d == null) continue;
            var (stat, v) = b.Weapon ? d.Weapon : b.Kind == Kind.Shield ? d.Shield : d.Armour;
            yield return (stat, v[Math.Clamp(grade, 0, 2)]);
        }
    }

    /// <summary>"gem-ruby:2" as (ruby, 2).</summary>
    public static (string id, int grade) GemOf(string socketed)
    {
        string[] p = socketed.Split(':');
        return (p[0].Replace("gem-", ""), p.Length > 1 && int.TryParse(p[1], out int g) ? g : 0);
    }

    /// <summary>A gem of a kind and grade, its name with the grade ("Flawless Ruby").</summary>
    public static Item Gem(string id, int grade)
    {
        Data.GemDef d = Data.Gem(id);
        string pre = Data.GemGrades[Math.Clamp(grade, 0, 2)];
        return new Item("gem-" + id) { Grade = Math.Clamp(grade, 0, 2), Name = (pre.Length > 0 ? pre + " " : "") + d.Name, Rarity = Rarity.Magic };
    }

    /// <summary>The level the hero needs to wear it (a unique: its own).</summary>
    public int Requires => B == null ? 1 : Rarity == Rarity.Unique ? Math.Max(B.Level, Level)
        : Math.Max(B.Level, Rarity switch { Rarity.Rare => Level - 3, Rarity.Magic => Level - 6, _ => 0 });

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

    /// <summary>An item with an empty socket.</summary>
    public bool OpenSocket => Sockets > Gems.Count;

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
        if (b.Kind == Kind.Map && Lair >= 0)
        {
            yield return Data.Lairs[Lair].Map;
            yield return $"Opens a portal to {Data.Lairs[Lair].Name}: monster level {Level}";
            yield return "One way in, one way out: it closes behind you";
        }
        if (b.Kind == Kind.Gem && Data.Gem(Base.Replace("gem-", "")) is Data.GemDef gd)
        {
            int g = Math.Clamp(Grade, 0, 2);
            yield return $"In a weapon: {Data.Describe(gd.Weapon.stat, gd.Weapon.v[g])}";
            yield return $"In armour: {Data.Describe(gd.Armour.stat, gd.Armour.v[g])}";
            yield return $"In a shield: {Data.Describe(gd.Shield.stat, gd.Shield.v[g])}";
            yield return "Click it, then an item with an empty socket";
        }
        foreach (Mod m in Mods) yield return Data.Describe(m.Stat, m.Value);
        foreach (var (stat, value) in GemMods()) yield return Data.Describe(stat, value) + " (gem)";
        if (Sockets > 0) yield return $"Sockets {Gems.Count} of {Sockets}" + (Gems.Count < Sockets ? $": {Sockets - Gems.Count} empty" : "");
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
        // Sockets: plain things often have them, magic ones fewer, rare ones at most one.
        int most = Data.MaxSockets(b);
        if (most > 0)
        {
            double chance = rarity switch { Rarity.Plain => 0.35, Rarity.Magic => 0.2, _ => 0.1 };
            if (r.Chance(chance))
            {
                int cap = rarity switch { Rarity.Plain => most, Rarity.Magic => Math.Min(2, most), _ => 1 };
                it.Sockets = r.Int(1, cap + 1);
            }
        }
        it.Name = rarity switch
        {
            Rarity.Magic => string.Join(" ", new[] { prefix?.Name, b.Name, suffix?.Name }.Where(s => s != null)),
            Rarity.Rare => $"{Data.RareFirst[r.Int(0, Data.RareFirst.Length)]} {Data.RareSecond(b.Kind, r.Int(0, 600))}",
            _ => b.Name,
        };
        return it;
    }

    /// <summary>A treasure map to a lair (a kind from the RNG unless given), for a level.</summary>
    public static Item Map(int level, Rng r, int lair = -1)
    {
        int k = lair >= 0 ? lair : r.Int(0, Data.Lairs.Length);
        return new Item("treasure-map") { Lair = k, Level = Math.Max(3, level), MapSeed = r.Next(), Name = $"Map to {Data.Lairs[k].Name}", Rarity = Rarity.Rare };
    }

    /// <summary>A unique item from the table (its name, its base, its fixed affixes).</summary>
    public static Item Unique(int index)
    {
        var u = Data.UniqueItems[index];
        var it = new Item(u.baseId) { Rarity = Rarity.Unique, Level = u.level, Name = u.name };
        foreach (var (stat, value) in u.mods) it.Mods.Add(new Mod(stat, value));
        return it;
    }

    /// <summary>A unique that can drop at a level (one no higher than the level, the newer ones more often), or null.</summary>
    public static Item RollUnique(int level, Rng r)
    {
        var can = Enumerable.Range(0, Data.UniqueItems.Length).Where(i => Data.UniqueItems[i].level <= level + 2).ToList();
        if (can.Count == 0) return null;
        double total = can.Sum(i => 1.0 + Data.UniqueItems[i].level / 5.0), x = r.Float() * total;
        foreach (int i in can) { x -= 1.0 + Data.UniqueItems[i].level / 5.0; if (x <= 0) return Unique(i); }
        return Unique(can[^1]);
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
        int rolls = quality >= 10 ? 7 : quality >= 6 ? 5 : quality >= 2 ? 2 : 1;
        // A lair's boss always leaves a unique; named foes a treasure map half the time, champions now and then.
        if (quality >= 10 && RollUnique(level, r) is Item u0) got.Add(u0);
        if (quality >= 7 && r.Chance(0.5) || quality >= 2 && quality < 7 && r.Chance(0.03)) got.Add(Map(level + 2, r));
        for (int k = 0; k < rolls; k++)
        {
            double x = r.Float();
            double nothing = quality >= 2 ? 0.1 : 0.55;
            if (x < nothing) continue;
            x = r.Float();
            if (x < 0.35) got.Add(new Item("gold", Math.Max(1, (int)((r.Int(3, 12) + level * r.Int(2, 6)) * (1 + goldFind / 100.0)))));
            else if (x < 0.55) got.Add(new Item(r.Chance(0.6) ? "pot-hp" : "pot-mp"));
            else if (x < 0.6) got.Add(new Item("scroll"));
            else if (x < 0.66)
            {
                // A gem, better the deeper: chipped above, flawless below, plain between.
                int grade = level < 7 ? (r.Chance(0.85) ? 0 : 1) : level < 16 ? (r.Chance(0.25) ? 0 : r.Chance(0.8) ? 1 : 2) : (r.Chance(0.5) ? 1 : 2);
                got.Add(Item.Gem(Data.Gems[r.Int(0, Data.Gems.Length)].Id, grade));
            }
            else
            {
                Rarity rarity = RollRarity(quality, magicFind, r);
                // Now and then a rare roll is a unique instead (more often from champions and the named foes).
                Item u = rarity == Rarity.Rare && r.Chance(Math.Min(0.6, 0.12 * Math.Sqrt(quality))) ? RollUnique(level, r) : null;
                got.Add(u ?? Make(PickBase(level, r).Id, rarity, level, r));
            }
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
