using System;
using System.Collections.Generic;
using System.Linq;

namespace Emberdeep;

public enum Cls { Wanderer, Rogue, Sorcerer }
public enum Elem { Phys, Fire, Cold, Light }
public enum Rarity { Plain, Magic, Rare, Unique }

/// <summary>What an item is: decides where it can go and how it is used.</summary>
public enum Kind { Sword, Axe, Club, Bow, Staff, Body, Head, Hands, Feet, Shield, Ring, Amulet, PotLife, PotMana, Scroll, Gem, Gold }

/// <summary>The equipment slots, in the order of <see cref="Hero.Equip"/>.</summary>
public enum Slot { Weapon, Shield, Body, Head, Hands, Feet, Amulet, Ring1, Ring2 }

/// <summary>Everything an affix, a passive skill or an attribute can change.</summary>
public enum Stat
{
    Life, Mana, Might, Agility, Vigour, Spirit, Armour, ArmourPct, DmgPct, DmgMin, DmgMax, FireDmg, ColdDmg, LightDmg,
    AttackSpeed, CastSpeed, Accuracy, Crit, FireRes, ColdRes, LightRes, AllRes, LifeSteal, LifeRegen, ManaRegen,
    MoveSpeed, MagicFind, GoldFind, SpellDmg, Skills, Block,
}

/// <summary>An item base: one per icon. Weapons have damage, armour has armour, a shield has block too.</summary>
public sealed record Base(string Id, string Name, Kind Kind, int Level, int DMin = 0, int DMax = 0, double Speed = 1,
    int Armour = 0, int Block = 0, string Held = null, bool TwoHanded = false, int SpellPct = 0, int Price = 10)
{
    public bool Weapon => Kind is Kind.Sword or Kind.Axe or Kind.Club or Kind.Bow or Kind.Staff;
    public bool Ranged => Kind == Kind.Bow;
    public bool Wearable => Weapon || Kind is Kind.Body or Kind.Head or Kind.Hands or Kind.Feet or Kind.Shield or Kind.Ring or Kind.Amulet;
    public bool Stacks => Kind is Kind.PotLife or Kind.PotMana or Kind.Scroll or Kind.Gold;
    /// <summary>The icon's file name (assets/icons).</summary>
    public string Icon => Id;
}

/// <summary>A prefix or suffix: the stat it rolls, its range at the lowest and highest tier, where it can appear.</summary>
public sealed record Affix(string Name, bool Prefix, Stat Stat, int Lo, int Hi, int Level, string Where);

public sealed record SkillDef(string Id, string Name, Cls Cls, int Level, bool Passive, int Mana, string Icon, string Text,
    /// <summary>Melee, Ranged, Spell, Self, Point: how it is aimed and what clip it plays.</summary>
    string Aim);

public enum Ai { Melee, Ranged, Caster, Charger, Boss }

public sealed record MonDef(string Id, string Name, string Body, double Height, Ai Ai, double Speed, int Life, int DMin, int DMax,
    Elem Elem, double Range, double AttackTime, int Xp, int MinDepth, int MaxDepth, int PackMin, int PackMax,
    string Right = null, string Left = null, double Radius = 0.4, bool Quad = false, string Clip = "slash", string Missile = null,
    int Armour = 0, int Resist = 0)
{
    public bool Ranges => Ai is Ai.Ranged or Ai.Caster;
}

/// <summary>A floor of the Emberdeep: its name and look.</summary>
public sealed record FloorDef(int Depth, string Name, string Theme);

/// <summary>The game's tables: classes, skills, monsters, item bases, affixes, floors. All the numbers are here.</summary>
public static class Data
{
    public const int MaxLevel = 30, Bottom = 10, PackSize = 40;

    public static readonly FloorDef[] Floors =
    {
        new(0, "Emberwick", "village"),
        new(1, "The Barrow, Upper Graves", "barrow"), new(2, "The Barrow, Ossuary", "barrow"), new(3, "The Barrow, Hag's Hollow", "barrow"),
        new(4, "The Catacombs", "catacomb"), new(5, "The Catacombs, Bone Vaults", "catacomb"), new(6, "The Ember Chapel", "catacomb"),
        new(7, "The Ember Halls", "ember"), new(8, "The Ember Halls, Slag Works", "ember"), new(9, "The Ember Halls, Deep Forge", "ember"),
        new(10, "The Warden's Throne", "throne"),
    };

    public static FloorDef Floor(int depth) => Floors[Math.Clamp(depth, 0, Bottom)];

    // ---- classes ----------------------------------------------------------------------------------------------------

    public static (int might, int agility, int vigour, int spirit) StartAttrs(Cls c) => c switch
    {
        Cls.Wanderer => (25, 15, 25, 10),
        Cls.Rogue => (15, 25, 20, 15),
        _ => (10, 15, 15, 30),
    };

    /// <summary>A class's own figure, dressed as it is (used until the base bodies and their pieces exist).</summary>
    public static string Body(Cls c) => c switch { Cls.Wanderer => "body-hero", Cls.Rogue => "body-rogue", _ => "body-sorcerer" };

    /// <summary>
    /// The hero's body: the man's or the woman's base body, dressed piece by piece as equipped, when the view has it
    /// (<see cref="BaseBodies"/>); otherwise the class's own figure.
    /// </summary>
    public static string HeroBody(Hero h) => BaseBodies?.Invoke(h.Female ? "body-woman" : "body-man") == true ? (h.Female ? "body-woman" : "body-man") : Body(h.Class);

    /// <summary>Set by the view: is there such a body model? (The rules know nothing of files.)</summary>
    public static Func<string, bool> BaseBodies;

    public static string Blurb(Cls c) => c switch
    {
        Cls.Wanderer => "A sellsword come home to a dying village. Sword and shield, a hard skin and a louder voice.",
        Cls.Rogue => "A barrow-scout who knows the graves by name. A bow, quick feet and a cold eye.",
        _ => "A scholar of the ember-runes, sent from the city. Fire, frost and lightning, and little else between him and the dark.",
    };

    public static string[] StartItems(Cls c) => c switch
    {
        Cls.Wanderer => new[] { "short-sword", "buckler", "quilted-armour" },
        Cls.Rogue => new[] { "short-bow", "leather-cap", "quilted-armour" },
        _ => new[] { "ember-staff", "quilted-armour" },
    };

    // ---- skills -----------------------------------------------------------------------------------------------------

    public static readonly SkillDef[] Skills =
    {
        new("bash", "Bash", Cls.Wanderer, 1, false, 2, "skill-bash", "A crushing blow that throws the foe back.", "Melee"),
        new("ironskin", "Ironskin", Cls.Wanderer, 1, true, 0, "skill-ironskin", "Passive: more armour and resistance to the elements.", "Self"),
        new("cleave", "Cleave", Cls.Wanderer, 2, false, 4, "skill-cleave", "A wide sweep that strikes every foe in front of you.", "Melee"),
        new("warcry", "War Cry", Cls.Wanderer, 4, false, 6, "skill-warcry", "A roar that stuns and hurts everything around you.", "Self"),
        new("leap", "Leap", Cls.Wanderer, 6, false, 8, "skill-leap", "Leap to a place and crash down on whatever stands there.", "Point"),
        new("brawn", "Brawn", Cls.Wanderer, 8, true, 0, "skill-brawn", "Passive: more life and harder blows.", "Self"),
        new("quake", "Quake", Cls.Wanderer, 12, false, 14, "skill-quake", "Split the ground: great damage and a stun all around.", "Self"),

        new("power", "Power Shot", Cls.Rogue, 1, false, 2, "skill-power", "A heavy arrow; from rank 5 it pierces.", "Ranged"),
        new("precision", "Precision", Cls.Rogue, 1, true, 0, "skill-precision", "Passive: better aim and more critical hits.", "Self"),
        new("multi", "Multishot", Cls.Rogue, 2, false, 4, "skill-multi", "A fan of arrows.", "Ranged"),
        new("flurry", "Flurry", Cls.Rogue, 4, false, 5, "skill-flurry", "Three quick shots at one foe.", "Ranged"),
        new("step", "Shadow Step", Cls.Rogue, 6, false, 6, "skill-step", "Vanish into smoke and step out somewhere near.", "Point"),
        new("evasion", "Evasion", Cls.Rogue, 8, true, 0, "skill-evasion", "Passive: a chance to dodge any blow or missile.", "Self"),
        new("rain", "Arrow Rain", Cls.Rogue, 12, false, 12, "skill-rain", "A storm of arrows falls on a place for two seconds.", "Point"),

        new("firebolt", "Firebolt", Cls.Sorcerer, 1, false, 3, "skill-firebolt", "A bolt of fire.", "Spell"),
        new("meditation", "Meditation", Cls.Sorcerer, 1, true, 0, "skill-meditation", "Passive: mana returns faster.", "Self"),
        new("nova", "Frost Nova", Cls.Sorcerer, 2, false, 7, "skill-nova", "A ring of ice that hurts and slows everything near.", "Self"),
        new("chain", "Chain Lightning", Cls.Sorcerer, 4, false, 8, "skill-chain", "Lightning that leaps from foe to foe.", "Spell"),
        new("shield", "Arcane Shield", Cls.Sorcerer, 6, false, 12, "skill-shield", "A sphere of force that takes blows for you for a minute.", "Self"),
        new("arcane", "Arcane Mastery", Cls.Sorcerer, 8, true, 0, "skill-arcane", "Passive: stronger spells and more critical hits.", "Self"),
        new("meteor", "Meteor", Cls.Sorcerer, 12, false, 18, "skill-meteor", "Call down a burning stone; the ground burns after.", "Point"),
    };

    private static readonly Dictionary<string, SkillDef> _skill = Skills.ToDictionary(s => s.Id);
    public static SkillDef Skill(string id) => _skill.TryGetValue(id, out SkillDef s) ? s : null;
    public static IEnumerable<SkillDef> SkillsOf(Cls c) => Skills.Where(s => s.Cls == c);
    public const int MaxRank = 10;

    /// <summary>Mana for a skill at a rank: a little more each rank.</summary>
    public static int ManaCost(SkillDef s, int rank) => s.Passive ? 0 : s.Mana + (rank - 1) / 2;

    /// <summary>A line on what the skill does at a rank, for the skill panel and the agent.</summary>
    public static string SkillNumbers(string id, int r) => id switch
    {
        "bash" => $"{150 + 15 * r}% weapon damage, knock-back",
        "cleave" => $"{100 + 10 * r}% weapon damage in a wide arc",
        "warcry" => $"stun {1 + 0.15 * r:0.0} s and {50 + 10 * r}% weapon damage within 5 m",
        "leap" => $"leap up to 7 m, {120 + 15 * r}% weapon damage where you land",
        "quake" => $"{200 + 25 * r}% weapon damage within 6 m, stun 0.6 s",
        "ironskin" => $"+{10 * r}% armour, +{2 * r}% to every resistance",
        "brawn" => $"+{5 * r}% life, +{8 * r}% melee damage",
        "power" => $"{160 + 15 * r}% weapon damage{(r >= 5 ? ", pierces" : "")}",
        "multi" => $"{MultiArrows(r)} arrows at {75 + 5 * r}% weapon damage",
        "flurry" => $"3 shots at {70 + 8 * r}% weapon damage",
        "step" => $"step up to {6 + 0.3 * r:0.0} m away",
        "rain" => $"{250 + 30 * r}% weapon damage over 2 s within 3 m",
        "precision" => $"+{10 * r}% accuracy, +{2 * r}% critical chance",
        "evasion" => $"{EvadePct(r)}% chance to dodge",
        "firebolt" => $"{FireboltDmg(r).lo}-{FireboltDmg(r).hi} fire damage",
        "nova" => $"{5 + 4 * r}-{9 + 6 * r} cold damage within 6 m, slows 3 s",
        "chain" => $"1-{14 + 6 * r} lightning damage, leaps to {ChainJumps(r)} foes",
        "shield" => $"absorbs {20 + 12 * r} damage for 60 s",
        "meteor" => $"{30 + 15 * r}-{45 + 22 * r} fire damage within 3 m, burning ground",
        "meditation" => $"+{15 * r}% mana regeneration",
        "arcane" => $"+{6 * r}% spell damage, +{r}% critical chance",
        _ => "",
    };

    public static int MultiArrows(int r) => Math.Min(9, 3 + r / 2);
    public static int ChainJumps(int r) => 3 + r / 3;
    public static int EvadePct(int r) => r <= 0 ? 0 : Math.Min(40, 4 + 3 * r);
    public static (int lo, int hi) FireboltDmg(int r) => (3 + 3 * r, 6 + 4 * r);

    // ---- monsters ---------------------------------------------------------------------------------------------------

    public static readonly MonDef[] Monsters =
    {
        new("gnawer", "Gnawer", "body-gnawer", 0.75, Ai.Melee, 4.0, 8, 1, 2, Elem.Phys, 1.0, 0.9, 6, 1, 4, 3, 5, Radius: 0.35, Quad: true, Clip: "bite"),
        new("hollow", "Hollow", "body-hollow", 1.75, Ai.Melee, 1.5, 22, 2, 4, Elem.Phys, 1.2, 1.3, 10, 1, 5, 3, 5, Clip: "punch"),
        new("sentry", "Bone Sentry", "body-bone-sentry", 1.8, Ai.Melee, 2.6, 18, 1, 4, Elem.Phys, 1.4, 1.0, 12, 1, 6, 3, 5, Right: "sword", Left: "shield", Armour: 12),
        new("archer", "Grave Archer", "body-archer", 1.8, Ai.Ranged, 2.6, 14, 1, 4, Elem.Phys, 9, 1.4, 12, 2, 7, 2, 4, Right: "bow", Clip: "shoot", Missile: "arrow"),
        new("ghoul", "Barrow Ghoul", "body-ghoul", 1.6, Ai.Melee, 3.4, 26, 3, 7, Elem.Phys, 1.3, 0.9, 16, 2, 8, 2, 4, Clip: "punch"),
        new("hag", "Bog Hag", "body-bog-hag", 1.5, Ai.Caster, 2.0, 24, 3, 7, Elem.Cold, 8, 1.8, 20, 2, 6, 1, 2, Clip: "point", Missile: "frost", Resist: 30),
        new("shade", "Wailing Shade", "body-shade", 1.9, Ai.Caster, 3.0, 20, 4, 8, Elem.Cold, 8, 1.6, 20, 4, 9, 2, 4, Clip: "point", Missile: "frost", Resist: 50),
        new("cultist", "Ember Cultist", "body-cultist", 1.75, Ai.Caster, 2.4, 26, 4, 9, Elem.Fire, 9, 1.6, 22, 4, 9, 2, 4, Right: "staff", Clip: "point", Missile: "ember", Resist: 30),
        new("imp", "Cinder Imp", "body-imp", 1.1, Ai.Melee, 4.0, 16, 3, 7, Elem.Fire, 1.0, 0.7, 14, 5, 10, 4, 7, Clip: "punch", Radius: 0.3, Resist: 50),
        new("brute", "Ironhide Brute", "body-brute", 2.4, Ai.Charger, 2.2, 70, 8, 16, Elem.Phys, 1.8, 1.6, 45, 5, 10, 1, 2, Right: "club", Clip: "overhead", Radius: 0.6, Armour: 30),
        new("knight", "Dread Knight", "body-dread-knight", 1.95, Ai.Melee, 2.8, 55, 7, 14, Elem.Phys, 1.5, 1.1, 40, 7, 10, 1, 3, Right: "axe", Left: "shield", Radius: 0.45, Armour: 40, Resist: 20),
        new("golem", "Slag Golem", "body-slag-golem", 2.6, Ai.Charger, 1.8, 90, 9, 18, Elem.Fire, 1.8, 1.7, 60, 8, 10, 1, 1, Clip: "overhead", Radius: 0.65, Armour: 40, Resist: 60),
        new("vorgath", "Vorgath the Ashen Warden", "body-boss", 3.0, Ai.Boss, 2.6, 650, 10, 20, Elem.Fire, 2.2, 1.4, 2500, 10, 10, 1, 1, Clip: "overhead", Missile: "ember", Radius: 0.9, Armour: 60, Resist: 40),
    };

    /// <summary>The named foes the elder's quests send you after: a monster made stronger, with its own name.</summary>
    public static readonly (string quest, string title, string mon, int depth, double lifeX, double dmgX)[] Uniques =
    {
        ("hag", "Mother Wort", "hag", 3, 6, 1.6),
        ("rite", "Ember-Priest Kael", "cultist", 6, 7, 1.7),
        ("warden", "Vorgath the Ashen Warden", "vorgath", 10, 1, 1),
    };

    public static readonly (string id, string name, string giver, string text, string done)[] Quests =
    {
        ("hag", "The Hag in the Barrow", "elder",
            "The graves under Emberwick have woken, and a hag nests in the deepest of them, on the third floor down. Her name was Wort, once. Put her in the ground for good.",
            "Wort is dead? Then the barrow may sleep again. Take this: you will need it more than an old man does."),
        ("rite", "The Ember Rite", "elder",
            "Below the barrow, the cultists keep a chapel to the fire that sleeps under us. Their priest, Kael, feeds it. On the sixth floor. Stop him.",
            "Kael's fire is out. I can feel the cold coming back into the stones. Here: the village owes you this."),
        ("warden", "The Ashen Warden", "elder",
            "There is a thing at the bottom of the Emberdeep that was set there to guard the fire, and has become it. Vorgath. Ten floors down. End it.",
            "It is over. The embers are only embers now. Emberwick will remember you."),
    };

    private static readonly Dictionary<string, MonDef> _mon = Monsters.ToDictionary(m => m.Id);
    public static MonDef Mon(string id) => _mon.TryGetValue(id, out MonDef m) ? m : null;

    /// <summary>A monster's level on a floor: 1 on the first, 19 at the bottom.</summary>
    public static int MonLevel(int depth) => Math.Max(1, 1 + (depth - 1) * 2);

    public static IEnumerable<MonDef> MonstersFor(int depth) =>
        Monsters.Where(m => m.Ai != Ai.Boss && depth >= m.MinDepth && depth <= m.MaxDepth);

    // ---- the hero's growth ------------------------------------------------------------------------------------------

    /// <summary>Experience to go from a level to the next.</summary>
    public static long XpToNext(int level) => (long)Math.Round(60 * Math.Pow(level, 1.7));

    // ---- items ------------------------------------------------------------------------------------------------------

    public static readonly Base[] Bases =
    {
        new("short-sword", "Short Sword", Kind.Sword, 1, 2, 7, 1.0, Held: "sword", Price: 30),
        new("long-sword", "Long Sword", Kind.Sword, 7, 5, 14, 1.0, Held: "sword", Price: 120),
        new("bastard-sword", "Bastard Sword", Kind.Sword, 14, 10, 24, 1.1, Held: "bastard-sword", Price: 400),
        new("hatchet", "Hatchet", Kind.Axe, 1, 3, 6, 0.95, Held: "axe", Price: 25),
        new("war-axe", "War Axe", Kind.Axe, 9, 7, 17, 1.05, Held: "axe", Price: 200),
        new("spiked-club", "Spiked Club", Kind.Club, 4, 4, 10, 1.1, Held: "club", Price: 70),
        new("short-bow", "Short Bow", Kind.Bow, 1, 2, 6, 1.0, Held: "bow", TwoHanded: true, Price: 35),
        new("long-bow", "Long Bow", Kind.Bow, 7, 4, 13, 1.05, Held: "bow", TwoHanded: true, Price: 150),
        new("war-bow", "War Bow", Kind.Bow, 14, 8, 21, 1.1, Held: "bow", TwoHanded: true, Price: 450),
        new("ember-staff", "Ember Staff", Kind.Staff, 1, 2, 6, 1.1, Held: "staff", TwoHanded: true, SpellPct: 10, Price: 40),
        new("rune-staff", "Rune Staff", Kind.Staff, 10, 5, 12, 1.1, Held: "staff", TwoHanded: true, SpellPct: 30, Price: 350),

        new("quilted-armour", "Quilted Armour", Kind.Body, 1, Armour: 8, Price: 20),
        new("leather-armour", "Leather Armour", Kind.Body, 4, Armour: 15, Price: 60),
        new("chain-hauberk", "Chain Hauberk", Kind.Body, 9, Armour: 30, Price: 220),
        new("plate-armour", "Plate Armour", Kind.Body, 15, Armour: 50, Price: 600),
        new("leather-cap", "Leather Cap", Kind.Head, 1, Armour: 3, Price: 15),
        new("iron-helm", "Iron Helm", Kind.Head, 6, Armour: 8, Price: 80),
        new("great-helm", "Great Helm", Kind.Head, 13, Armour: 16, Price: 300),
        new("leather-gloves", "Leather Gloves", Kind.Hands, 1, Armour: 2, Price: 12),
        new("chain-gloves", "Chain Gloves", Kind.Hands, 7, Armour: 5, Price: 70),
        new("gauntlets", "Gauntlets", Kind.Hands, 13, Armour: 10, Price: 250),
        new("leather-boots", "Leather Boots", Kind.Feet, 1, Armour: 2, Price: 12),
        new("heavy-boots", "Heavy Boots", Kind.Feet, 7, Armour: 5, Price: 70),
        new("greaves", "Greaves", Kind.Feet, 13, Armour: 10, Price: 250),
        new("buckler", "Buckler", Kind.Shield, 1, Armour: 4, Block: 12, Held: "shield", Price: 25),
        new("heater-shield", "Heater Shield", Kind.Shield, 7, Armour: 10, Block: 20, Held: "shield", Price: 140),
        new("tower-shield", "Tower Shield", Kind.Shield, 13, Armour: 18, Block: 28, Held: "shield", Price: 380),
        new("ring", "Ring", Kind.Ring, 1, Price: 100),
        new("amulet", "Amulet", Kind.Amulet, 1, Price: 150),

        new("pot-hp", "Healing Draught", Kind.PotLife, 1, Price: 25),
        new("pot-mp", "Mana Draught", Kind.PotMana, 1, Price: 25),
        new("scroll", "Waystone Scroll", Kind.Scroll, 1, Price: 50),
        new("gold", "Gold", Kind.Gold, 1, Price: 1),
    };

    private static readonly Dictionary<string, Base> _base = Bases.ToDictionary(b => b.Id);
    public static Base BaseOf(string id) => _base.TryGetValue(id, out Base b) ? b : null;

    /// <summary>What heals: life or mana restored by a draught, over a second.</summary>
    public const int PotionHeal = 60;

    // Where an affix may appear: w weapon, a armour (body, head, hands, feet), s shield, j jewellery (ring, amulet).
    public static readonly Affix[] Affixes =
    {
        new("Cruel", true, Stat.DmgPct, 15, 80, 1, "w"),
        new("Jagged", true, Stat.DmgMax, 2, 12, 1, "wj"),
        new("Smouldering", true, Stat.FireDmg, 3, 24, 3, "wj"),
        new("Frostbitten", true, Stat.ColdDmg, 3, 20, 3, "wj"),
        new("Storm-touched", true, Stat.LightDmg, 4, 30, 5, "wj"),
        new("Keen", true, Stat.Accuracy, 15, 120, 1, "wj"),
        new("Hardened", true, Stat.ArmourPct, 15, 80, 1, "as"),
        new("Riveted", true, Stat.Armour, 4, 40, 1, "asj"),
        new("Sturdy", true, Stat.Life, 5, 45, 1, "asj"),
        new("Glowing", true, Stat.Mana, 5, 40, 1, "wasj"),
        new("Runed", true, Stat.SpellDmg, 5, 40, 3, "wj"),
        new("Ashen", true, Stat.AllRes, 3, 15, 8, "asj"),
        new("Lucky", true, Stat.MagicFind, 5, 30, 2, "aj"),
        new("Warded", true, Stat.Block, 3, 12, 3, "s"),

        new("of the Bear", false, Stat.Might, 2, 15, 1, "wasj"),
        new("of the Fox", false, Stat.Agility, 2, 15, 1, "wasj"),
        new("of the Ox", false, Stat.Vigour, 2, 15, 1, "asj"),
        new("of the Owl", false, Stat.Spirit, 2, 15, 1, "wasj"),
        new("of Haste", false, Stat.AttackSpeed, 10, 30, 3, "wh"),
        new("of Sorcery", false, Stat.CastSpeed, 10, 30, 3, "wj"),
        new("of the Leech", false, Stat.LifeSteal, 2, 8, 4, "wj"),
        new("of Mending", false, Stat.LifeRegen, 1, 6, 1, "asj"),
        new("of Focus", false, Stat.ManaRegen, 10, 60, 1, "asj"),
        new("of the Hearth", false, Stat.FireRes, 8, 40, 1, "asj"),
        new("of Winter", false, Stat.ColdRes, 8, 40, 1, "asj"),
        new("of Grounding", false, Stat.LightRes, 8, 40, 1, "asj"),
        new("of Striding", false, Stat.MoveSpeed, 5, 25, 1, "f"),
        new("of Precision", false, Stat.Crit, 2, 10, 5, "wj"),
        new("of Greed", false, Stat.GoldFind, 15, 80, 1, "aj"),
        new("of Mastery", false, Stat.Skills, 1, 2, 10, "wj"),
    };

    /// <summary>Two-word names for rare items: a first word and a second one that fits the kind.</summary>
    public static readonly string[] RareFirst = { "Ash", "Ember", "Grave", "Cinder", "Bone", "Gloom", "Soot", "Wraith", "Rust", "Dusk", "Hollow", "Blood", "Pyre", "Barrow", "Kiln" };

    public static string RareSecond(Kind k, int roll) => (k switch
    {
        Kind.Sword or Kind.Axe or Kind.Club => new[] { "Bite", "Edge", "Song", "Maul", "Fang", "Reaver" },
        Kind.Bow => new[] { "Whisper", "Strand", "Flight", "Thorn", "Sting", "Wing" },
        Kind.Staff => new[] { "Spire", "Branch", "Rod", "Brand", "Call", "Torch" },
        Kind.Body => new[] { "Shroud", "Coat", "Hide", "Mantle", "Carapace", "Wrap" },
        Kind.Head => new[] { "Crown", "Visage", "Brow", "Cowl", "Mask", "Veil" },
        Kind.Hands => new[] { "Grip", "Grasp", "Fist", "Clutch", "Touch", "Claw" },
        Kind.Feet => new[] { "Stride", "Tread", "Track", "Trail", "March", "Step" },
        Kind.Shield => new[] { "Ward", "Wall", "Bulwark", "Guard", "Aegis", "Door" },
        Kind.Ring => new[] { "Loop", "Coil", "Band", "Eye", "Knot", "Circle" },
        _ => new[] { "Heart", "Charm", "Token", "Star", "Talisman", "Mark" },
    })[roll % 6];

    /// <summary>The letters for <see cref="Affix.Where"/> that fit a base.</summary>
    public static string Where(Base b) => b.Kind switch
    {
        Kind.Sword or Kind.Axe or Kind.Club or Kind.Bow or Kind.Staff => "w",
        Kind.Shield => "s",
        Kind.Ring or Kind.Amulet => "j",
        Kind.Hands => "ah",
        Kind.Feet => "af",
        _ => "a",
    };

    /// <summary>Text for a stat's value, as a tooltip line.</summary>
    public static string Describe(Stat s, int v) => s switch
    {
        Stat.Life => $"+{v} to life",
        Stat.Mana => $"+{v} to mana",
        Stat.Might => $"+{v} to might",
        Stat.Agility => $"+{v} to agility",
        Stat.Vigour => $"+{v} to vigour",
        Stat.Spirit => $"+{v} to spirit",
        Stat.Armour => $"+{v} armour",
        Stat.ArmourPct => $"+{v}% armour",
        Stat.DmgPct => $"+{v}% damage",
        Stat.DmgMin => $"+{v} to minimum damage",
        Stat.DmgMax => $"+{v} to maximum damage",
        Stat.FireDmg => $"adds {Math.Max(1, v / 2)}-{v} fire damage",
        Stat.ColdDmg => $"adds {Math.Max(1, v / 2)}-{v} cold damage",
        Stat.LightDmg => $"adds 1-{v} lightning damage",
        Stat.AttackSpeed => $"+{v}% attack speed",
        Stat.CastSpeed => $"+{v}% casting speed",
        Stat.Accuracy => $"+{v} accuracy",
        Stat.Crit => $"+{v}% critical chance",
        Stat.FireRes => $"fire resistance +{v}%",
        Stat.ColdRes => $"cold resistance +{v}%",
        Stat.LightRes => $"lightning resistance +{v}%",
        Stat.AllRes => $"all resistances +{v}%",
        Stat.LifeSteal => $"{v}% of damage dealt returns as life",
        Stat.LifeRegen => $"regenerate {v} life a second",
        Stat.ManaRegen => $"+{v}% mana regeneration",
        Stat.MoveSpeed => $"+{v}% walking speed",
        Stat.MagicFind => $"+{v}% better chance of finding magic items",
        Stat.GoldFind => $"+{v}% more gold",
        Stat.SpellDmg => $"+{v}% spell damage",
        Stat.Skills => $"+{v} to all skills",
        Stat.Block => $"+{v}% chance to block",
        _ => $"{s} {v}",
    };
}
