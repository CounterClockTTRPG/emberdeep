using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Emberdeep;

/// <summary>The numbers that come out of a hero's attributes, level, items and passive skills.</summary>
public sealed class Stats
{
    public int Might, Agility, Vigour, Spirit;
    public double MaxLife, MaxMana, Armour, Accuracy, Crit, Block, Evade, LifeRegen, ManaRegen, MoveSpeed;
    public double DMin, DMax, AttackTime, CastTime, SpellPct, LifeSteal;
    public double FireMin, FireMax, ColdMin, ColdMax, LightMin, LightMax;
    public int FireRes, ColdRes, LightRes, MagicFind, GoldFind, PlusSkills;
    public bool Ranged;
    public int Res(Elem e) => e switch { Elem.Fire => FireRes, Elem.Cold => ColdRes, Elem.Light => LightRes, _ => 0 };
}

/// <summary>The player's character: who they are, what they carry, what they have learned and done.</summary>
public sealed class Hero
{
    public Cls Class;
    /// <summary>A woman (else a man): which base body the hero is dressed on.</summary>
    public bool Female;
    public string Name = "Wanderer";
    public int Level = 1;
    public long Xp;
    public int Might, Agility, Vigour, Spirit;
    public int AttrPoints, SkillPoints;
    /// <summary>Ranks in each skill learned (id to rank).</summary>
    public Dictionary<string, int> Skills = new();
    /// <summary>Worn, by <see cref="Slot"/>.</summary>
    public Item[] Equip = new Item[9];
    /// <summary>The pack: <see cref="Data.PackSize"/> places, null where empty.</summary>
    public Item[] Pack = new Item[Data.PackSize];
    /// <summary>The belt: four draughts or scrolls, on keys 1-4.</summary>
    public Item[] Belt = new Item[4];
    public List<Item> Stash = new();
    public int Gold;
    /// <summary>The floors whose waystone the hero has touched (0, the village, always).</summary>
    public HashSet<int> Waystones = new() { 0 };
    /// <summary>Quest id to state: 1 given, 2 done (the foe is dead), 3 rewarded.</summary>
    public Dictionary<string, int> Quests = new();
    /// <summary>The skills on the left and right mouse buttons ("attack" is the plain weapon attack).</summary>
    public string Left = "attack", Right;
    public int Kills, Deaths;
    public int Deepest;

    [JsonIgnore] private Stats _stats;

    public static Hero New(Cls c, bool female = false)
    {
        var (m, a, v, s) = Data.StartAttrs(c);
        var h = new Hero { Class = c, Female = female, Name = c.ToString(), Might = m, Agility = a, Vigour = v, Spirit = s };
        SkillDef first = Data.SkillsOf(c).First(sk => !sk.Passive);
        h.Skills[first.Id] = 1;
        h.Right = first.Id;
        foreach (string b in Data.StartItems(c)) h.Wear(new Item(b));
        h.Belt[0] = new Item("pot-hp", 1);
        h.Belt[1] = new Item("pot-hp", 1);
        h.Belt[2] = new Item("pot-mp", 1);
        h.Belt[3] = new Item("scroll", 1);
        h.Gold = 40;
        return h;
    }

    public void Changed() => _stats = null;
    public Stats S => _stats ??= Compute();

    public int Rank(string skill) => Skills.TryGetValue(skill, out int r) && r > 0 ? r + S.PlusSkills : 0;
    public int BaseRank(string skill) => Skills.TryGetValue(skill, out int r) ? r : 0;
    public Item Weapon => Equip[(int)Slot.Weapon];

    private int Sum(Stat s) => Equip.Where(i => i != null).Sum(i => i.Get(s));

    private Stats Compute()
    {
        var st = new Stats
        {
            Might = Might + Sum(Stat.Might), Agility = Agility + Sum(Stat.Agility),
            Vigour = Vigour + Sum(Stat.Vigour), Spirit = Spirit + Sum(Stat.Spirit),
            PlusSkills = Sum(Stat.Skills),
        };
        int R(string id) => Skills.TryGetValue(id, out int r) && r > 0 ? r + st.PlusSkills : 0;
        st.MaxLife = (4 * st.Vigour + 3 * (Level - 1) + Sum(Stat.Life)) * (1 + 0.05 * R("brawn"));
        st.MaxMana = 2 * st.Spirit + 2 * (Level - 1) + Sum(Stat.Mana);
        double armour = Equip.Where(i => i != null).Sum(i => i.Armour);
        st.Armour = (armour + st.Agility / 4.0) * (1 + 0.1 * R("ironskin"));
        st.Accuracy = (5 * st.Agility + 5 * Level + Sum(Stat.Accuracy)) * (1 + 0.1 * R("precision"));
        st.Crit = 5 + Sum(Stat.Crit) + 2 * R("precision") + R("arcane");
        Item shield = Equip[(int)Slot.Shield];
        st.Block = shield == null ? 0 : Math.Min(60, shield.B.Block + Sum(Stat.Block) + (Class == Cls.Wanderer ? 5 : 0));
        st.Evade = Data.EvadePct(R("evasion"));
        st.LifeRegen = Sum(Stat.LifeRegen) + st.MaxLife * 0.006;
        st.ManaRegen = st.MaxMana * 0.03 * (1 + (Sum(Stat.ManaRegen) + 15 * R("meditation")) / 100.0);
        st.MoveSpeed = 4.6 * (1 + Sum(Stat.MoveSpeed) / 100.0);
        Item w = Weapon;
        st.Ranged = w?.B.Ranged == true;
        double statBonus = (st.Ranged ? st.Agility : st.Might) / 100.0;
        double meleeBonus = st.Ranged ? 0 : 0.08 * R("brawn");
        if (w != null)
        {
            var (lo, hi) = w.Damage;
            st.DMin = lo; st.DMax = hi;
            st.AttackTime = 0.8 * w.B.Speed;
        }
        else { st.DMin = 1; st.DMax = 3; st.AttackTime = 0.7; }
        // Affixes on the other things worn (rings, a jagged amulet) add to the weapon.
        foreach (Item i in Equip.Where(i => i != null && i != w)) { st.DMin += i.Get(Stat.DmgMin); st.DMax += i.Get(Stat.DmgMax); }
        st.DMin *= 1 + statBonus + meleeBonus;
        st.DMax *= 1 + statBonus + meleeBonus;
        st.AttackTime /= 1 + Sum(Stat.AttackSpeed) / 100.0;
        st.CastTime = 0.65 / (1 + Sum(Stat.CastSpeed) / 100.0);
        st.SpellPct = st.Spirit * 0.5 + Sum(Stat.SpellDmg) + (w?.B.SpellPct ?? 0) + 6 * R("arcane");
        st.LifeSteal = Sum(Stat.LifeSteal);
        int fire = Sum(Stat.FireDmg), cold = Sum(Stat.ColdDmg), light = Sum(Stat.LightDmg);
        st.FireMin = fire > 0 ? Math.Max(1, fire / 2) : 0; st.FireMax = fire;
        st.ColdMin = cold > 0 ? Math.Max(1, cold / 2) : 0; st.ColdMax = cold;
        st.LightMin = light > 0 ? 1 : 0; st.LightMax = light;
        int all = Sum(Stat.AllRes) + 2 * R("ironskin");
        st.FireRes = Math.Min(75, Sum(Stat.FireRes) + all);
        st.ColdRes = Math.Min(75, Sum(Stat.ColdRes) + all);
        st.LightRes = Math.Min(75, Sum(Stat.LightRes) + all);
        st.MagicFind = Sum(Stat.MagicFind);
        st.GoldFind = Sum(Stat.GoldFind);
        return st;
    }

    // ---- items ------------------------------------------------------------------------------------------------------

    /// <summary>Which slot an item goes in (the second ring slot when the first is taken), or null.</summary>
    public Slot? SlotFor(Item it)
    {
        Base b = it?.B;
        if (b == null || !b.Wearable) return null;
        if (b.Weapon) return Slot.Weapon;
        return b.Kind switch
        {
            Kind.Shield => Slot.Shield, Kind.Body => Slot.Body, Kind.Head => Slot.Head, Kind.Hands => Slot.Hands, Kind.Feet => Slot.Feet,
            Kind.Amulet => Slot.Amulet, Kind.Ring => Equip[(int)Slot.Ring1] == null || Equip[(int)Slot.Ring2] != null ? Slot.Ring1 : Slot.Ring2,
            _ => null,
        };
    }

    /// <summary>Can the hero wear this now? Null when they can, else why not.</summary>
    public string CannotWear(Item it)
    {
        if (SlotFor(it) == null) return "that is not worn";
        if (it.Requires > Level) return $"needs level {it.Requires}";
        return null;
    }

    /// <summary>Put an item on, straight from nowhere (a new hero's kit); what was there goes to the pack.</summary>
    public void Wear(Item it)
    {
        Slot? s = SlotFor(it);
        if (s == null) return;
        Item old = Equip[(int)s];
        Equip[(int)s] = it;
        if (old != null) Take(old);
        if (it.B.TwoHanded && Equip[(int)Slot.Shield] is Item sh) { Equip[(int)Slot.Shield] = null; Take(sh); }
        Changed();
    }

    /// <summary>
    /// Wear the item in a pack place; what it replaces takes its place (a shield puts a two-handed weapon away, and a
    /// two-handed weapon the shield). Null when done, else why not.
    /// </summary>
    public string EquipFromPack(int place)
    {
        Item it = place >= 0 && place < Pack.Length ? Pack[place] : null;
        if (it == null) return "nothing there";
        string why = CannotWear(it);
        if (why != null) return why;
        Slot s = SlotFor(it).Value;
        Item[] before = (Item[])Equip.Clone();
        Pack[place] = null;
        var off = new List<Item>();
        if (Equip[(int)s] != null) off.Add(Equip[(int)s]);
        Equip[(int)s] = it;
        if (it.B.Kind == Kind.Shield && Weapon?.B.TwoHanded == true) { off.Add(Weapon); Equip[(int)Slot.Weapon] = null; }
        if (it.B.TwoHanded && Equip[(int)Slot.Shield] is Item sh) { off.Add(sh); Equip[(int)Slot.Shield] = null; }
        if (off.Count > Free())
        {
            Equip = before;
            Pack[place] = it;
            return "no room in the pack";
        }
        foreach (Item o in off)
        {
            if (Pack[place] == null) Pack[place] = o;
            else Pack[Array.IndexOf(Pack, null)] = o;
        }
        Changed();
        return null;
    }

    /// <summary>Take off what is in a slot, into the pack. Null when done.</summary>
    public string Unequip(Slot s)
    {
        Item it = Equip[(int)s];
        if (it == null) return "nothing there";
        if (Free() == 0) return "the pack is full";
        Equip[(int)s] = null;
        Take(it);
        Changed();
        return null;
    }

    public int Free() => Pack.Count(i => i == null);

    /// <summary>Put an item away: gold to the purse, draughts and scrolls onto a stack or the belt, the rest in the pack.</summary>
    public bool Take(Item it)
    {
        if (it.B?.Kind == Kind.Gold) { Gold += it.Count; return true; }
        if (it.B?.Stacks == true)
        {
            foreach (Item[] row in new[] { Belt, Pack })
                foreach (Item have in row)
                    if (have != null && have.Base == it.Base && have.Count < 5)
                    {
                        int move = Math.Min(5 - have.Count, it.Count);
                        have.Count += move;
                        it.Count -= move;
                        if (it.Count == 0) return true;
                    }
            for (int k = 0; k < Belt.Length; k++) if (Belt[k] == null) { Belt[k] = it; return true; }
        }
        for (int k = 0; k < Pack.Length; k++) if (Pack[k] == null) { Pack[k] = it; return true; }
        return false;
    }

    // ---- growing ----------------------------------------------------------------------------------------------------

    /// <summary>Spend an attribute point. Null when done.</summary>
    public string Raise(string attr)
    {
        if (AttrPoints <= 0) return "no points to spend";
        switch (attr.ToLowerInvariant())
        {
            case "might": Might++; break;
            case "agility": Agility++; break;
            case "vigour": Vigour++; break;
            case "spirit": Spirit++; break;
            default: return "no such attribute (might, agility, vigour, spirit)";
        }
        AttrPoints--;
        Changed();
        return null;
    }

    /// <summary>Why a skill cannot take another rank now, or null.</summary>
    public string CannotLearn(string id)
    {
        SkillDef s = Data.Skill(id);
        if (s == null || s.Cls != Class) return "not one of your skills";
        if (SkillPoints <= 0) return "no skill points";
        if (Level < s.Level) return $"needs level {s.Level}";
        if (BaseRank(id) >= Data.MaxRank) return "at its highest rank";
        return null;
    }

    public string Learn(string id)
    {
        string why = CannotLearn(id);
        if (why != null) return why;
        Skills[id] = BaseRank(id) + 1;
        SkillPoints--;
        if (Right == null && !Data.Skill(id).Passive) Right = id;
        Changed();
        return null;
    }
}
