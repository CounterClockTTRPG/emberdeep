using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Emberdeep;

/// <summary>Anything that moves and fights: the hero, a monster, a village folk.</summary>
public sealed class Actor
{
    public int Id;
    /// <summary>"hero", a monster id (Data.Monsters), or a village folk ("elder", "healer", "smith").</summary>
    public string Def;
    public bool Hero, Npc;
    public int Level = 1;
    public double X, Y, Facing;
    public double Life, MaxLife, Mana, MaxMana;
    /// <summary>What it is doing: idle, walk, attack, cast, hit, leap, dead.</summary>
    public string Act = "idle";
    public double ActT, ActDur, ActAt;
    public bool ActDone;
    public string ActSkill;
    public int ActTarget = -1;
    public double ActX, ActY;
    /// <summary>Timers: stunned, slowed (chilled), the arcane shield, invisible after a shadow step.</summary>
    public double Stun, Slow, ShieldT, Shield, Hidden;
    // Monsters.
    public bool Awake, Champion;
    public string Title;
    /// <summary>A champion's traits (Data.Traits ids), shared by its pack.</summary>
    public List<string> Traits = new();
    public bool Has(string trait) => Traits.Contains(trait);
    /// <summary>A boss (Vorgath, a lair's): calls its kind at two thirds and one third of its life, cannot be stunned or
    /// thrown back; drawn larger by <see cref="Scale"/>.</summary>
    public bool Boss;
    public double Scale = 1;
    /// <summary>Lasting harm: burning and poisoned (seconds left, damage a second), frozen solid (seconds left).</summary>
    public double Burn, BurnDps, Poison, PoisonDps, Frozen, DotT;
    /// <summary>What ails it, in words (burning, poisoned, frozen, chilled, stunned).</summary>
    public IEnumerable<string> Ailments()
    {
        if (Burn > 0) yield return "burning";
        if (Poison > 0) yield return "poisoned";
        if (Frozen > 0) yield return "frozen";
        else if (Slow > 0) yield return "chilled";
        if (Stun > 0) yield return "stunned";
    }
    public int Pack;
    public double Think, DmgX = 1, Accuracy, Defense;
    public int Resist;
    /// <summary>Phases a boss has gone through (summons at two thirds and one third of its life).</summary>
    public int Phase;
    public double DeadT;
    /// <summary>Where a leap began.</summary>
    public double LX, LY;
    [JsonIgnore] public List<(double x, double y)> Path = new();
    [JsonIgnore] public double PathT;

    public bool Alive => Act != "dead";
    public MonDef M => Data.Mon(Def);
    public double Radius => Hero || Npc ? 0.35 : M?.Radius ?? 0.4;
    public string Name => Title ?? (Hero ? "you" : M?.Name ?? Def);
}

public sealed class Missile
{
    public int Id;
    /// <summary>What it looks like: arrow, firebolt, frost, ember.</summary>
    public string Kind;
    public double X, Y, VX, VY, Left;
    public bool FromHero;
    public int Owner;
    public double Dmg, Accuracy;
    public Elem Elem;
    public bool Crit, Pierce;
    public double Slow;
    public List<int> Struck = new();
}

/// <summary>Something that happens to a place over time: a falling meteor, burning ground, a rain of arrows.</summary>
public sealed class Zone
{
    public int Id;
    public string Kind;
    public double X, Y, R, Delay, Left, Every, Next;
    public bool FromHero;
    public double Dmg;
    public Elem Elem;
}

public sealed class LairState
{
    public int Kind, Level, FromDepth;
    public ulong Seed;
    public double X, Y;
}

public sealed class Drop
{
    public int Id;
    public double X, Y;
    public Item Item;
}

/// <summary>Something the screen should show or sound: a blow, a death, a level gained. Not saved.</summary>
public sealed class Ev
{
    public string Kind;
    public double X, Y, X2, Y2;
    public int A = -1, B = -1;
    public int V;
    public Elem E;
    public bool Crit;
    public string Text;
}

/// <summary>A floor as it is now: who is alive on it, what lies on the ground, what has been opened and seen.</summary>
public sealed class FloorState
{
    public int Depth;
    public List<Actor> Actors = new();
    public List<Drop> Drops = new();
    public List<Missile> Missiles = new();
    public List<Zone> Zones = new();
    public HashSet<int> Opened = new();
    /// <summary>Cells seen, for the map (one byte a cell).</summary>
    public byte[] Seen;
    /// <summary>When the hero last left it (game time); a floor left long enough fills up again.</summary>
    public double LeftAt = -1;
}

/// <summary>What the hero was told to do: walk somewhere, fight someone, use a skill, pick up, use, talk.</summary>
public sealed class Order
{
    public string Kind = "none";
    public double X, Y;
    public int Target = -1;
    public string Skill;
    /// <summary>Keep attacking until the target dies (a click on a monster) or do it once (a skill on a place).</summary>
    public bool Repeat;
}

/// <summary>
/// The whole game as plain data, with no Godot types: what a save holds, what the self-check tests, what the screens
/// draw. Time advances in fixed ticks (<see cref="Tick"/>), so the same seed and the same orders replay the same.
/// </summary>
public sealed partial class Game
{
    public const double Tick = 1.0 / 30;
    /// <summary>What the stash holds (the panel shows every place).</summary>
    public const int StashSize = 60;

    public ulong Seed;
    public double Time;
    public Hero Hero;
    public Actor Me;
    public int Depth;
    public Dictionary<int, FloorState> Floors = new();
    public Order Order = new();
    public int NextId = 1;
    public ulong Rolls;
    /// <summary>The waystone scroll's way back: the floor and place it was read, or depth 0 for none.</summary>
    public int PortalDepth;
    public double PortalX, PortalY;
    public List<Item> SmithWares = new(), PeddlerWares = new();
    public bool Won;
    public double Accum;

    [JsonIgnore] public List<Ev> Events = new();
    [JsonIgnore] private Floor _map;
    [JsonIgnore] private int _mapDepth = -1;

    public Floor Map
    {
        get
        {
            if (_map == null || _mapDepth != Depth)
            {
                _map = Floor.Make(Depth >= Data.LairBase ? Lair?.Seed ?? Seed : Seed, Depth);
                _mapDepth = Depth;
                // What was broken here stays broken: its cell is open again.
                if (Floors.TryGetValue(Depth, out FloorState st))
                    foreach (Prop p in _map.Props.Where(p => p.Breaks && st.Opened.Contains(p.Id)).ToList()) _map.Broken(p);
            }
            return _map;
        }
    }

    public FloorState Here => Floors[Depth];

    /// <summary>The level of what lives here: the floor's, or in a lair the map's.</summary>
    public int AreaLevel => Depth >= Data.LairBase ? Lair?.Level ?? 1 : LevelOf(Depth);

    /// <summary>The tier the game is in (Data.Tiers): 0 until Vorgath has fallen and the hero goes down again.</summary>
    public int Tier;

    /// <summary>A floor's monster level in this tier.</summary>
    public int LevelOf(int depth) => Data.MonLevel(depth) + Tier * Data.TierLevels;

    /// <summary>The place's name, with the tier after the first ("The Barrow, Upper Graves (Smouldering)").</summary>
    public string PlaceName => Map.Def.Name + (Tier > 0 && Depth > 0 && Depth < Data.LairBase ? $" ({Data.Tiers[Tier]})" : "");

    /// <summary>Can the hero go down again, harder (Vorgath slain, a tier left)?</summary>
    public bool CanGoDeeper => Won && Tier < Data.Tiers.Length - 1;

    /// <summary>
    /// Down again, a tier harder: the deep fills up afresh with monsters 20 levels stronger, the hero's resistances
    /// drop by 20, the quests and the waystones start over; the hero keeps level, items, gold and stash.
    /// </summary>
    public string GoDeeper()
    {
        if (!CanGoDeeper) return "not yet";
        Tier++;
        Hero.Tier = Tier;
        Hero.Changed();
        FloorState village = Floors.TryGetValue(0, out FloorState v) ? v : null;
        Floors.Clear();
        if (village != null) Floors[0] = village;
        Hero.Quests.Clear();
        Hero.Waystones = new HashSet<int> { 0 };
        Hero.Deepest = 0;
        Won = false;
        if (Depth >= Data.LairBase) Lair = null;
        PortalDepth = 0;
        if (Depth != 0) Enter(0, null);
        Emit(new Ev { Kind = "tier", V = Tier, Text = Data.Tiers[Tier] });
        return null;
    }

    /// <summary>A lair whose portal a treasure map has opened (null: none): which, from what, to where it leads back.</summary>
    public LairState Lair;
    public IEnumerable<Actor> Foes => Here.Actors.Where(a => !a.Npc && !a.Hero);

    /// <summary>The way back in the village, beside the waystone.</summary>
    public static (double x, double y) PortalSpot => (Floor.VillageSize / 2 + 3.5, Floor.VillageSize / 2 + 1.5);

    public static Game New(ulong seed, Cls cls = Cls.Wanderer, bool female = false, string name = null)
    {
        var g = new Game { Seed = seed, Hero = Hero.New(cls, female, name) };
        g.Me = new Actor { Id = 0, Def = "hero", Hero = true };
        g.Hero.Changed();
        g.Me.MaxLife = g.Me.Life = g.Hero.S.MaxLife;
        g.Me.MaxMana = g.Me.Mana = g.Hero.S.MaxMana;
        g.Enter(0, null);
        g.Events.Clear();
        return g;
    }

    /// <summary>A random source for one roll, from the seed and how many rolls came before (so saves replay too).</summary>
    public Rng R() => new(Seed ^ (++Rolls * 0x2545F4914F6CDD1DUL));

    // ---- floors -----------------------------------------------------------------------------------------------------

    /// <summary>How long a floor must be left (seconds of play) before its monsters, chests and barrels come back.</summary>
    public const double RestockAfter = 600;

    /// <summary>Go to a floor, arriving at a place (null: where the floor's arrivals arrive).</summary>
    public void Enter(int depth, (double x, double y)? at)
    {
        if (Floors.TryGetValue(Depth, out FloorState leaving)) leaving.LeftAt = Time;
        Depth = depth >= Data.LairBase ? depth : Math.Clamp(depth, 0, Data.Bottom);
        // A floor left for long enough fills up again (what was seen of it stays on the map).
        if (Depth > 0 && Floors.TryGetValue(Depth, out FloorState old) && old.LeftAt >= 0 && Time - old.LeftAt >= RestockAfter)
        {
            Floors.Remove(Depth);
            _map = null;
            Floors[Depth] = Populate(Map);
            Floors[Depth].Seen = old.Seen;
            Emit(new Ev { Kind = "restock", V = Depth });
        }
        Floor f = Map;
        if (!Floors.ContainsKey(Depth)) Floors[Depth] = Populate(f);
        FloorState st = Here;
        st.Seen ??= new byte[f.W * f.H];
        (double x, double y) p = at ?? (f.Start.x + 0.5, f.Start.y + 0.5);
        Me.X = p.x; Me.Y = p.y;
        Me.Act = "idle"; Me.Path.Clear();
        Order = new Order();
        st.Missiles.Clear();
        st.Zones.Clear();
        if (Depth > Hero.Deepest && Depth < Data.LairBase) Hero.Deepest = Depth;
        if (Depth == 0) { SmithWares = Loot.SmithStock(Hero.Level, R()); PeddlerWares = Loot.PeddlerStock(LevelOf(Math.Max(1, Hero.Deepest)) + 2, R()); }
        Reveal();
        Emit(new Ev { Kind = "enter", V = Depth, Text = f.Def.Name });
    }

    private FloorState Populate(Floor f)
    {
        var st = new FloorState { Depth = f.Depth };
        if (f.Village)
        {
            foreach (var (who, x, y) in Floor.VillageFolk)
                st.Actors.Add(new Actor { Id = NextId++, Def = who, Npc = true, X = x, Y = y, Life = 1, MaxLife = 1, Facing = Math.PI });
            return st;
        }
        int level = f.Depth >= Data.LairBase ? Lair?.Level ?? 1 : 0;
        foreach (Spawn s in f.Spawns) st.Actors.Add(MakeMonster(s.Mon, s.X, s.Y, f.Depth, s.Champion, s.Unique, s.Pack, level));
        return st;
    }

    /// <summary>
    /// A saved game taken up again: every floor but the one the hero stands on fills up again when next entered, as
    /// the deep does between visits (the quests stay as they are).
    /// </summary>
    public void Restock()
    {
        foreach (var (depth, st) in Floors)
            if (depth != Depth && depth > 0) st.LeftAt = Time - RestockAfter;
    }

    public Actor MakeMonster(string id, double x, double y, int depth, bool champion = false, string unique = null, int pack = 0, int level = 0)
    {
        MonDef m = Data.Mon(id);
        int lvl = (level > 0 ? level : LevelOf(depth)) + (champion ? 2 : 0) + (unique != null ? 3 : 0);
        double life = m.Life * (1 + 0.2 * (lvl - 1)), dmg = 0.8 * (1 + 0.1 * (lvl - 1));
        var u = Data.Uniques.FirstOrDefault(q => q.title == unique);
        Data.LairDef lb = unique == null ? null : Data.Lairs.FirstOrDefault(l => l.BossTitle == unique);
        if (champion) { life *= 2.5; dmg *= 1.3; }
        // A champion pack shares one or two traits, rolled from the seed, the floor and the pack.
        var traits = new List<string>();
        if (champion)
        {
            var tr = new Rng(Seed ^ ((ulong)depth << 20) ^ (ulong)pack * 2654435761UL);
            int n = tr.Chance(0.45) ? 2 : 1;
            while (traits.Count < n) { string t = Data.Traits[tr.Int(0, Data.Traits.Length)].id; if (!traits.Contains(t)) traits.Add(t); }
            if (traits.Contains("mighty")) dmg *= 1.35;
        }
        if (lb != null) { life *= lb.BossLife; dmg *= lb.BossDmg; }
        else if (unique != null && u.title != null) { life *= u.lifeX; dmg *= u.dmgX; }
        var a = new Actor
        {
            Id = NextId++, Def = id, Level = lvl, X = x, Y = y, MaxLife = Math.Round(life), Life = Math.Round(life), DmgX = dmg,
            Champion = champion, Title = unique ?? (champion ? $"{m.Name} Champion" : null), Pack = pack, Traits = traits,
            Accuracy = 10 + 9 * lvl, Defense = m.Armour + 5 * lvl, Resist = m.Resist, Facing = R().Float() * Math.Tau,
            Boss = m.Ai == Ai.Boss || lb != null, Scale = lb?.BossScale ?? 1,
        };
                return a;
    }

    // ---- time -------------------------------------------------------------------------------------------------------

    /// <summary>Let time pass: whole ticks of <see cref="Tick"/>, the rest kept for the next call.</summary>
    public void Step(double dt)
    {
        Accum += Math.Min(dt, 0.25);
        while (Accum >= Tick)
        {
            Accum -= Tick;
            Update(Tick);
        }
    }

    private void Update(double dt)
    {
        Time += dt;
        FloorState st = Here;
        Stats s = Hero.S;
        Me.MaxLife = s.MaxLife;
        Me.MaxMana = s.MaxMana;
        if (Me.Alive)
        {
            Me.Life = Math.Min(Me.MaxLife, Me.Life + s.LifeRegen * dt);
            Me.Mana = Math.Min(Me.MaxMana, Me.Mana + s.ManaRegen * dt);
            if (Depth == 0) { Me.Life = Math.Min(Me.MaxLife, Me.Life + Me.MaxLife * 0.05 * dt); }
        }
        Timers(Me, dt);
        if (Me.Alive) Dots(Me, dt);
        if (Hero.BlessingLeft > 0)
        {
            Hero.BlessingLeft -= dt;
            if (Hero.BlessingLeft <= 0) { Hero.Changed(); Emit(new Ev { Kind = "blessing-end", Text = Hero.Blessing }); }
        }
        HeroThink(dt);
        // Gold is picked up just by walking over it.
        if (Me.Alive)
            foreach (Drop d in st.Drops.Where(d => d.Item.Base == "gold" && Dist(d.X, d.Y, Me.X, Me.Y) < 0.7).ToList()) Pick(d);
        Act(Me, dt);
        foreach (Actor a in st.Actors.ToList())
        {
            if (!a.Alive) { a.DeadT += dt; continue; }
            Timers(a, dt);
            Dots(a, dt);
            if (!a.Alive) continue;
            if (!a.Npc) MonsterThink(a, dt);
            Act(a, dt);
        }
        Separate(dt);
        Missiles(dt);
        Zones(dt);
        if (!Me.Alive)
        {
            Me.DeadT += dt;
            if (Me.DeadT > 3) Revive();
        }
        Reveal();
    }

    private static void Timers(Actor a, double dt)
    {
        a.Stun = Math.Max(0, a.Stun - dt);
        a.Slow = Math.Max(0, a.Slow - dt);
        a.Hidden = Math.Max(0, a.Hidden - dt);
        a.ShieldT = Math.Max(0, a.ShieldT - dt);
        if (a.ShieldT <= 0) a.Shield = 0;
        a.Frozen = Math.Max(0, a.Frozen - dt);
        a.Burn = Math.Max(0, a.Burn - dt);
        a.Poison = Math.Max(0, a.Poison - dt);
    }

    /// <summary>Burning and poison bite every half second (fire through the resistance to it; poison through nothing).</summary>
    private void Dots(Actor a, double dt)
    {
        if (a.Burn <= 0 && a.Poison <= 0) { a.DotT = 0; return; }
        a.DotT -= dt;
        if (a.DotT > 0) return;
        a.DotT = 0.5;
        if (a.Hero)
        {
            if (Depth == 0) return;
            double d = (a.Burn > 0 ? a.BurnDps * 0.5 * (1 - Hero.S.FireRes / 100.0) : 0) + (a.Poison > 0 ? a.PoisonDps * 0.5 : 0);
            d = Math.Round(d);
            if (d <= 0) return;
            Me.Life -= d;
            Emit(new Ev { Kind = "hurt", A = 0, X = Me.X, Y = Me.Y, V = (int)d, E = a.Burn > 0 ? Elem.Fire : Elem.Phys });
            if (Me.Life <= 0) { Me.Life = 0; HeroDies(null); }
            return;
        }
        if (a.Burn > 0) HurtFoe(a, a.BurnDps * 0.5, Elem.Fire, false, 0, true);
        if (a.Poison > 0 && a.Alive) HurtFoe(a, a.PoisonDps * 0.5, Elem.Phys, false, 0, true);
    }

    /// <summary>The cells around the hero are seen (for the map).</summary>
    private void Reveal()
    {
        Floor f = Map;
        byte[] seen = Here.Seen;
        if (seen == null) return;
        int cx = (int)Me.X, cy = (int)Me.Y, rr = Depth == 0 ? 14 : 9;
        for (int y = cy - rr; y <= cy + rr; y++)
            for (int x = cx - rr; x <= cx + rr; x++)
                if (f.In(x, y) && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= rr * rr) seen[y * f.W + x] = 1;
    }

    public void Emit(Ev e) => Events.Add(e);

    public Actor Find(int id) => id == 0 ? Me : Here.Actors.FirstOrDefault(a => a.Id == id);

    public static double Dist(double x0, double y0, double x1, double y1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
    public static double Dist(Actor a, Actor b) => Dist(a.X, a.Y, b.X, b.Y);

    // ---- moving -----------------------------------------------------------------------------------------------------

    private double Speed(Actor a) =>
        (a.Hero ? Hero.S.MoveSpeed : a.M?.Speed ?? 2) * (a.Slow > 0 ? 0.5 : 1) * (a.Champion ? 1.15 : 1) * (a.Has("swift") ? 1.5 : 1);

    /// <summary>Walk an actor toward a point along a path (found again now and then), at its speed. True when there.</summary>
    private bool WalkTo(Actor a, double tx, double ty, double within, double dt)
    {
        double d = Dist(a.X, a.Y, tx, ty);
        if (d <= within) { a.Path.Clear(); return true; }
        Floor f = Map;
        a.PathT -= dt;
        bool direct = d < 6 && f.Sight(a.X, a.Y, tx, ty) && ClearLine(a.X, a.Y, tx, ty);
        if (!direct && (a.Path.Count == 0 || a.PathT <= 0))
        {
            a.Path = f.Path(a.X, a.Y, tx, ty, a.Hero ? 8000 : 2500);
            a.PathT = a.Hero ? 0.6 : 0.5 + (a.Id % 5) * 0.1;
            if (a.Path.Count == 0) { a.Act = "idle"; return true; }
        }
        double nx = tx, ny = ty;
        if (!direct && a.Path.Count > 0)
        {
            (nx, ny) = a.Path[0];
            if (Dist(a.X, a.Y, nx, ny) < 0.25) { a.Path.RemoveAt(0); if (a.Path.Count > 0) (nx, ny) = a.Path[0]; else (nx, ny) = (tx, ty); }
        }
        double dx = nx - a.X, dy = ny - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return d <= within + 0.3;
        double step = Math.Min(len, Speed(a) * dt);
        Move(a, dx / len * step, dy / len * step);
        a.Facing = Math.Atan2(dx, dy);
        a.Act = "walk";
        return false;
    }

    /// <summary>Is a straight walk free of blocked cells (rock and props)?</summary>
    private bool ClearLine(double x0, double y0, double x1, double y1)
    {
        Floor f = Map;
        double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
        int n = (int)Math.Ceiling(len * 3);
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n;
            if (!f.Walk(x0 + dx * t, y0 + dy * t)) return false;
        }
        return true;
    }

    /// <summary>Move by a step, sliding along walls rather than into them.</summary>
    private void Move(Actor a, double dx, double dy)
    {
        Floor f = Map;
        double r = a.Radius * 0.8;
        bool Ok(double x, double y) => f.Walk(x, y) && f.Walk(x + r, y) && f.Walk(x - r, y) && f.Walk(x, y + r) && f.Walk(x, y - r);
        if (Ok(a.X + dx, a.Y + dy)) { a.X += dx; a.Y += dy; return; }
        if (Ok(a.X + dx, a.Y)) { a.X += dx; return; }
        if (Ok(a.X, a.Y + dy)) a.Y += dy;
    }

    /// <summary>Living actors push each other apart (the hero is heavy, a boss heavier).</summary>
    private void Separate(double dt)
    {
        var all = Here.Actors.Where(a => a.Alive).Append(Me).Where(a => a.Alive && a.Act != "leap").ToList();
        for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                Actor a = all[i], b = all[j];
                double dx = b.X - a.X, dy = b.Y - a.Y, d = Math.Sqrt(dx * dx + dy * dy), min = a.Radius + b.Radius;
                if (d >= min || d < 1e-5) continue;
                double push = (min - d) * 0.5;
                double wa = Mass(a), wb = Mass(b), ta = wb / (wa + wb), tb = wa / (wa + wb);
                Move(a, -dx / d * push * ta * 2, -dy / d * push * ta * 2);
                Move(b, dx / d * push * tb * 2, dy / d * push * tb * 2);
            }
    }

    private static double Mass(Actor a) => a.Npc ? 1000 : a.Hero ? 3 : a.Boss ? 20 : a.Radius > 0.55 ? 4 : 1;

    // ---- the hero's orders ------------------------------------------------------------------------------------------

    /// <summary>Walk to a place.</summary>
    public void OrderMove(double x, double y) => Order = new Order { Kind = "move", X = x, Y = y };

    /// <summary>Fight a monster with a skill (or the weapon), again and again until it falls.</summary>
    public void OrderAttack(int target, string skill = "attack") => Order = new Order { Kind = "attack", Target = target, Skill = skill ?? "attack", Repeat = true };

    /// <summary>Use a skill toward a place, once (a firebolt at the floor, a leap, a meteor, a war cry).</summary>
    public void OrderSkill(string skill, double x, double y) => Order = new Order { Kind = "skill", Skill = skill, X = x, Y = y };

    public void OrderPick(int drop) => Order = new Order { Kind = "pick", Target = drop };
    public void OrderUse(int prop) => Order = new Order { Kind = "use", Target = prop };
    public void OrderTalk(int npc) => Order = new Order { Kind = "talk", Target = npc };
    public void Halt() { Order = new Order(); if (Me.Act == "walk") Me.Act = "idle"; Me.Path.Clear(); }

    private bool Busy(Actor a) => a.Act is "attack" or "cast" or "hit" or "leap" or "dead" || a.Stun > 0 || a.Frozen > 0;

    private void HeroThink(double dt)
    {
        if (!Me.Alive || Busy(Me)) return;
        Order o = Order;
        switch (o.Kind)
        {
            case "move":
                if (WalkTo(Me, o.X, o.Y, 0.15, dt)) { Halt(); }
                break;
            case "attack":
            {
                Actor t = Find(o.Target);
                if (t == null || !t.Alive) { Halt(); break; }
                SkillDef sk = Data.Skill(o.Skill);
                string aim = sk?.Aim ?? (Hero.S.Ranged ? "Ranged" : "Melee");
                if (o.Skill == "attack") aim = Hero.S.Ranged ? "Ranged" : "Melee";
                double reach = aim switch { "Melee" => Me.Radius + t.Radius + 0.7, "Self" => 2.5, _ => 13 };
                bool see = Map.Sight(Me.X, Me.Y, t.X, t.Y);
                if (Dist(Me, t) > reach || (aim != "Melee" && !see))
                {
                    WalkTo(Me, t.X, t.Y, aim == "Melee" ? reach * 0.9 : 0.5, dt);
                    break;
                }
                Me.Path.Clear();
                if (!Begin(Me, o.Skill, t.X, t.Y, t.Id)) Halt();
                break;
            }
            case "skill":
            {
                SkillDef sk = Data.Skill(o.Skill);
                if (sk == null) { Halt(); break; }
                // A meteor or a rain needs the place in sight and in reach; a leap or a step goes as far as it can.
                if (sk.Id is "rain" or "meteor")
                {
                    bool see = Map.Sight(Me.X, Me.Y, o.X, o.Y);
                    // Out of reach: walk into it; in reach but out of sight: keep walking until it can be seen.
                    if (Dist(Me.X, Me.Y, o.X, o.Y) > 13 || !see)
                    {
                        WalkTo(Me, o.X, o.Y, see ? 10 : 1, dt);
                        break;
                    }
                }
                Begin(Me, o.Skill, o.X, o.Y, -1);
                Halt();
                break;
            }
            case "pick":
            {
                Drop d = Here.Drops.FirstOrDefault(x => x.Id == o.Target);
                if (d == null) { Halt(); break; }
                if (!WalkTo(Me, d.X, d.Y, 1.0, dt)) break;
                Pick(d);
                Halt();
                break;
            }
            case "use":
            {
                Prop p = Map.Props.FirstOrDefault(x => x.Id == o.Target);
                if (p == null && !(o.Target == -2 || o.Target == -3 && Lair != null && Lair.FromDepth == Depth)) { Halt(); break; }
                (double px, double py) = p != null ? (p.X + p.W / 2.0, p.Y + p.H / 2.0) : o.Target == -3 ? (Lair.X, Lair.Y) : PortalSpot;
                double reach = p == null ? 1.2 : Math.Max(p.W, p.H) / 2.0 + 1.0;
                if (!WalkTo(Me, px, py, reach, dt)) break;
                Halt();
                // A barrel or an urn is broken with a swing: the blow breaks it.
                if (p != null && p.Breaks && !Here.Opened.Contains(p.Id)) Begin(Me, "smash", px, py, -p.Id - 10);
                else if (p == null && o.Target == -3) EnterLair();
                else Use(p);
                break;
            }
            case "talk":
            {
                Actor n = Find(o.Target);
                if (n == null || !n.Npc) { Halt(); break; }
                if (!WalkTo(Me, n.X, n.Y, 1.8, dt)) break;
                Me.Facing = Math.Atan2(n.X - Me.X, n.Y - Me.Y);
                n.Facing = Math.Atan2(Me.X - n.X, Me.Y - n.Y);
                Emit(new Ev { Kind = "talk", A = n.Id, Text = n.Def });
                Halt();
                break;
            }
            default:
                if (Me.Act == "walk") Me.Act = "idle";
                break;
        }
    }

    // ---- monsters ---------------------------------------------------------------------------------------------------

    private void MonsterThink(Actor a, double dt)
    {
        if (Busy(a)) return;
        MonDef m = a.M;
        if (m == null) return;
        double d = Dist(a, Me);
        if (!a.Awake)
        {
            if (Me.Alive && Me.Hidden <= 0 && d < (a.Boss ? 12 : 9) && Map.Sight(a.X, a.Y, Me.X, Me.Y)) Wake(a);
            else { if (a.Act == "walk") a.Act = "idle"; return; }
        }
        if (!Me.Alive || Me.Hidden > 0)
        {
            if (a.Act == "walk") a.Act = "idle";
            if (!Me.Alive) a.Awake = false;
            return;
        }
        if (a.Boss) BossPhases(a);
        double reach = a.Radius + Me.Radius + m.Range * (m.Ranges ? 1 : 0.6);
        bool see = Map.Sight(a.X, a.Y, Me.X, Me.Y);
        if (m.Ranges)
        {
            // Keep at a distance: come closer when too far or out of sight, back away when the hero is in its face.
            if (d > m.Range || !see) { WalkTo(a, Me.X, Me.Y, m.Range * 0.7, dt); return; }
            if (d < 2.5 && a.Think <= 0)
            {
                a.Think = 1.2;
                double ax = a.X - Me.X, ay = a.Y - Me.Y, len = Math.Max(0.01, Math.Sqrt(ax * ax + ay * ay));
                a.Path = Map.Path(a.X, a.Y, a.X + ax / len * 4, a.Y + ay / len * 4, 400);
                a.PathT = 1;
            }
            a.Think -= dt;
            if (a.Path.Count > 0 && a.Think > 0.6) { WalkTo(a, a.Path[^1].x, a.Path[^1].y, 0.2, dt); return; }
            Begin(a, "monster", Me.X, Me.Y, 0);
            return;
        }
        if (m.Ai == Ai.Boss && d > 4 && see && a.Think <= 0)
        {
            a.Think = 3.5;
            Begin(a, "volley", Me.X, Me.Y, 0);
            return;
        }
        a.Think -= dt;
        if (d > reach) { WalkTo(a, Me.X, Me.Y, reach * 0.85, dt); return; }
        Begin(a, "monster", Me.X, Me.Y, 0);
    }

    private void Wake(Actor a)
    {
        bool voiced = false;
        foreach (Actor o in Here.Actors.Where(o => o.Alive && !o.Npc && (o == a || (a.Pack > 0 && o.Pack == a.Pack))))
        {
            if (!o.Awake && o.Boss) Emit(new Ev { Kind = "boss", A = o.Id, Text = o.Name });
            // One voice for a pack waking: a growl, a groan, a rattle, a shriek.
            else if (!o.Awake && !voiced) { voiced = true; Emit(new Ev { Kind = "wake", A = o.Id, X = o.X, Y = o.Y, Text = o.Def }); }
            o.Awake = true;
        }
    }

    /// <summary>Vorgath calls imps out of the fire at two thirds and at one third of his life.</summary>
    private void BossPhases(Actor a)
    {
        int phase = a.Life < a.MaxLife / 3 ? 2 : a.Life < a.MaxLife * 2 / 3 ? 1 : 0;
        if (phase <= a.Phase) return;
        a.Phase = phase;
        bool lair = Depth >= Data.LairBase;
        string minion = lair ? Data.Lairs[Depth - Data.LairBase].Roster[0] : "imp";
        Emit(new Ev { Kind = "say", A = a.Id, Text = lair ? (phase == 1 ? "You came for my hoard. Stay for my hunger." : "Rise, all of you! Rise!")
            : phase == 1 ? "Burn, little light." : "The deep remembers its fire!" });
        Rng r = R();
        for (int k = 0; k < 2 + 2 * phase; k++)
        {
            double ang = r.Float() * Math.Tau;
            double x = a.X + Math.Sin(ang) * 3, y = a.Y + Math.Cos(ang) * 3;
            if (!Map.Walk(x, y)) continue;
            Actor imp = MakeMonster(minion, x, y, Depth, false, null, a.Pack, AreaLevel);
            imp.Awake = true;
            Here.Actors.Add(imp);
            Emit(new Ev { Kind = "blast", X = x, Y = y, V = 1, E = Elem.Fire });
        }
    }

    // ---- actions ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Start an action with a skill toward a place or actor: pays the mana, sets the timing (the blow lands part-way
    /// through). False when it cannot (no mana, unknown skill).
    /// </summary>
    public bool Begin(Actor a, string skill, double tx, double ty, int target)
    {
        double dur, at = 0.5;
        string act = "attack";
        if (a.Hero)
        {
            SkillDef sk = Data.Skill(skill);
            if (skill != "attack" && skill != "smash")
            {
                if (sk == null || sk.Passive || Hero.Rank(skill) <= 0) return false;
                int cost = Data.ManaCost(sk, Hero.BaseRank(skill));
                if (Me.Mana < cost) { Emit(new Ev { Kind = "nomana" }); return false; }
                Me.Mana -= cost;
            }
            Stats s = Hero.S;
            bool spell = sk != null && (sk.Aim is "Spell" || (sk.Cls == Cls.Sorcerer && sk.Aim is "Self" or "Point")) || skill is "warcry";
            dur = skill switch
            {
                "leap" => 0.7,
                "step" => 0.3,
                "flurry" => s.AttackTime * 1.3,
                "quake" or "warcry" => 0.9,
                _ => spell ? s.CastTime : s.AttackTime,
            };
            act = skill == "leap" ? "leap" : spell ? "cast" : "attack";
            if (skill is "leap" or "step")
            {
                // Only as far as the skill reaches, and only to open floor along the way.
                double range = skill == "leap" ? 7 : 6 + 0.3 * Hero.Rank("step");
                (tx, ty) = Reach(a.X, a.Y, tx, ty, range);
                a.LX = a.X; a.LY = a.Y;
            }
            if (skill == "leap") at = 0.85;
        }
        else
        {
            MonDef m = a.M;
            dur = skill == "volley" ? 1.2 : m.AttackTime * 1.25 / (a.Slow > 0 ? 0.6 : 1) / (a.Has("swift") ? 1.6 : 1);
            act = m.Ai == Ai.Caster || skill == "volley" ? "cast" : "attack";
            at = m.Ai == Ai.Charger ? 0.6 : 0.5;
        }
        a.Act = act;
        a.ActSkill = skill;
        a.ActT = 0;
        a.ActDur = dur;
        a.ActAt = dur * at;
        a.ActDone = false;
        a.ActTarget = target;
        a.ActX = tx; a.ActY = ty;
        if (Dist(a.X, a.Y, tx, ty) > 0.05) a.Facing = Math.Atan2(tx - a.X, ty - a.Y);
        Emit(new Ev { Kind = act == "cast" ? "cast" : a.Hero && Hero.S.Ranged && skill is "attack" or "power" or "multi" or "flurry" ? "shoot" : "swing", A = a.Id, Text = skill });
        return true;
    }

    /// <summary>The farthest open point toward a target, within a range.</summary>
    public (double x, double y) Reach(double x0, double y0, double tx, double ty, double range)
    {
        double dx = tx - x0, dy = ty - y0, len = Math.Sqrt(dx * dx + dy * dy);
        if (len > range) { tx = x0 + dx / len * range; ty = y0 + dy / len * range; len = range; }
        int n = Math.Max(1, (int)Math.Ceiling(len * 4));
        (double x, double y) best = (x0, y0);
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n, x = x0 + (tx - x0) * t, y = y0 + (ty - y0) * t;
            if (!Map.Floor_((int)Math.Floor(x), (int)Math.Floor(y))) break;
            if (Map.Walk(x, y)) best = (x, y);
        }
        return best;
    }

    private void Act(Actor a, double dt)
    {
        if (a.Act is "idle" or "walk" or "dead") return;
        a.ActT += dt;
        if (a.Act == "leap" && !a.ActDone)
        {
            // Through the air from where it was to the target, landing at the blow's moment.
            double t = Math.Min(1, a.ActT / a.ActAt);
            a.X = a.LX + (a.ActX - a.LX) * t;
            a.Y = a.LY + (a.ActY - a.LY) * t;
        }
        if (!a.ActDone && a.ActT >= a.ActAt)
        {
            a.ActDone = true;
            if (a.Hero) HeroBlow(a.ActSkill, a.ActX, a.ActY, a.ActTarget);
            else MonsterBlow(a);
        }
        if (a.ActT >= a.ActDur) a.Act = "idle";
    }

    // ---- what happens when a blow lands ----------------------------------------------------------------------------

    /// <summary>A roll of the hero's weapon damage, times a multiplier, plus its elemental adds.</summary>
    private (double phys, double fire, double cold, double light) WeaponRoll(double mult, Rng r)
    {
        Stats s = Hero.S;
        double phys = (s.DMin + r.Float() * (s.DMax - s.DMin)) * mult;
        double fire = s.FireMax > 0 ? (s.FireMin + r.Float() * (s.FireMax - s.FireMin)) * mult : 0;
        double cold = s.ColdMax > 0 ? (s.ColdMin + r.Float() * (s.ColdMax - s.ColdMin)) * mult : 0;
        double light = s.LightMax > 0 ? (s.LightMin + r.Float() * (s.LightMax - s.LightMin)) * mult : 0;
        return (phys, fire, cold, light);
    }

    private static double HitChance(double accuracy, double defense, int alvl, int dlvl) =>
        Math.Clamp(2 * accuracy / Math.Max(1, accuracy + defense) * alvl / (double)(alvl + dlvl), 0.05, 0.95);

    /// <summary>The hero's weapon strikes a monster (a hit roll, then the damage). True when it hit.</summary>
    private bool Strike(Actor t, double mult, Rng r, double knock = 0)
    {
        if (!t.Alive || t.Npc) return false;
        Stats s = Hero.S;
        if (!r.Chance(HitChance(s.Accuracy, t.Defense, Hero.Level, t.Level)))
        {
            Emit(new Ev { Kind = "miss", A = t.Id, X = t.X, Y = t.Y });
            return false;
        }
        bool crit = r.Chance(s.Crit / 100.0);
        double c = crit ? 1.5 : 1;
        var (phys, fire, cold, light) = WeaponRoll(mult * c, r);
        double dealt = HurtFoe(t, phys, Elem.Phys, crit, 0);
        if (fire > 0) dealt += HurtFoe(t, fire, Elem.Fire, false, 0);
        if (cold > 0) dealt += HurtFoe(t, cold, Elem.Cold, false, 1.5);
        if (light > 0) dealt += HurtFoe(t, light, Elem.Light, false, 0);
        if (s.LifeSteal > 0) Me.Life = Math.Min(Me.MaxLife, Me.Life + dealt * s.LifeSteal / 100.0);
        if (knock > 0 && t.Alive && !t.Boss)
        {
            double dx = t.X - Me.X, dy = t.Y - Me.Y, len = Math.Max(0.01, Math.Sqrt(dx * dx + dy * dy));
            for (int k = 0; k < 6; k++) Move(t, dx / len * knock / 6, dy / len * knock / 6);
        }
        return true;
    }

    /// <summary>A spell's damage, rolled and strengthened by the hero's spell bonus.</summary>
    private double SpellRoll(double lo, double hi, Rng r, out bool crit)
    {
        Stats s = Hero.S;
        crit = r.Chance(s.Crit / 100.0);
        return (lo + r.Float() * (hi - lo)) * (1 + s.SpellPct / 100.0) * (crit ? 1.5 : 1);
    }

    private IEnumerable<Actor> FoesWithin(double x, double y, double radius) =>
        Here.Actors.Where(a => a.Alive && !a.Npc && Dist(a.X, a.Y, x, y) <= radius + a.Radius).ToList();

    private void HeroBlow(string skill, double tx, double ty, int target)
    {
        Rng r = R();
        Stats s = Hero.S;
        int rank = Hero.Rank(skill);
        Actor t = target >= 0 ? Find(target) : null;
        double fx = Math.Sin(Me.Facing), fy = Math.Cos(Me.Facing);
        switch (skill)
        {
            case "smash":
            {
                Prop p = Map.Props.FirstOrDefault(x => x.Id == -target - 10);
                if (p != null) Use(p);
                break;
            }
            case "attack":
                if (s.Ranged) Shoot("arrow", tx, ty, 1, r, false);
                else if (t != null && Dist(Me, t) <= Me.Radius + t.Radius + 1.1) Strike(t, 1, r);
                else MeleeAt(tx, ty, 1, r);
                break;
            case "bash":
                if (t != null) Strike(t, 1.5 + 0.15 * rank, r, 1.8);
                else MeleeAt(tx, ty, 1.5 + 0.15 * rank, r, 1.8);
                break;
            case "cleave":
                foreach (Actor a in FoesWithin(Me.X, Me.Y, 2.2))
                {
                    double dx = a.X - Me.X, dy = a.Y - Me.Y, len = Math.Max(0.01, Math.Sqrt(dx * dx + dy * dy));
                    if ((dx * fx + dy * fy) / len > 0.4) Strike(a, 1.0 + 0.1 * rank, r);
                }
                Emit(new Ev { Kind = "arc", X = Me.X, Y = Me.Y, A = 0 });
                break;
            case "warcry":
                foreach (Actor a in FoesWithin(Me.X, Me.Y, 5))
                {
                    var (phys, _, _, _) = WeaponRoll(0.5 + 0.1 * rank, r);
                    HurtFoe(a, phys, Elem.Phys, false, 0);
                    if (!a.Boss) a.Stun = Math.Max(a.Stun, 1 + 0.15 * rank);
                    Wake(a);
                }
                Emit(new Ev { Kind = "warcry", X = Me.X, Y = Me.Y });
                break;
            case "leap":
                foreach (Actor a in FoesWithin(Me.X, Me.Y, 2.5)) Strike(a, 1.2 + 0.15 * rank, r, 1.5);
                Emit(new Ev { Kind = "blast", X = Me.X, Y = Me.Y, V = 2, E = Elem.Phys });
                break;
            case "quake":
                foreach (Actor a in FoesWithin(Me.X, Me.Y, 6))
                {
                    var (phys, _, _, _) = WeaponRoll(2 + 0.25 * rank, r);
                    HurtFoe(a, phys, Elem.Phys, false, 0);
                    if (!a.Boss) a.Stun = Math.Max(a.Stun, 0.6);
                    Wake(a);
                }
                Emit(new Ev { Kind = "quake", X = Me.X, Y = Me.Y, V = 6 });
                break;
            case "power":
                Shoot("arrow", tx, ty, 1.6 + 0.15 * rank, r, rank >= 5);
                break;
            case "multi":
            {
                int n = Data.MultiArrows(rank);
                double ang = Math.Atan2(tx - Me.X, ty - Me.Y);
                for (int k = 0; k < n; k++)
                {
                    double a = ang + (k - (n - 1) / 2.0) * 0.14;
                    Shoot("arrow", Me.X + Math.Sin(a) * 10, Me.Y + Math.Cos(a) * 10, 0.75 + 0.05 * rank, r, false);
                }
                break;
            }
            case "flurry":
                for (int k = 0; k < 3; k++)
                {
                    double jx = (r.Float() - 0.5) * 0.6, jy = (r.Float() - 0.5) * 0.6;
                    Shoot("arrow", tx + jx, ty + jy, 0.7 + 0.08 * rank, r, false, k * 0.08);
                }
                break;
            case "step":
                if (Map.Walk(tx, ty))
                {
                    Emit(new Ev { Kind = "step", X = Me.X, Y = Me.Y, X2 = tx, Y2 = ty });
                    Me.X = tx; Me.Y = ty;
                    Me.Hidden = 1.5;
                    foreach (Actor a in Here.Actors.Where(a => a.Awake && Dist(a, Me) > 4)) a.Path.Clear();
                }
                break;
            case "rain":
            {
                var (lo, hi) = (s.DMin, s.DMax);
                double total = (lo + hi) / 2 * (2.5 + 0.3 * rank);
                Here.Zones.Add(new Zone { Id = NextId++, Kind = "rain", X = tx, Y = ty, R = 3, Left = 2, Every = 0.25, Dmg = total / 8, Elem = Elem.Phys, FromHero = true });
                break;
            }
            case "firebolt":
            {
                var (lo, hi) = Data.FireboltDmg(rank);
                double dmg = SpellRoll(lo, hi, r, out bool crit);
                Launch("firebolt", tx, ty, 12, dmg, Elem.Fire, true, crit);
                break;
            }
            case "nova":
                foreach (Actor a in FoesWithin(Me.X, Me.Y, 6))
                {
                    double dmg = SpellRoll(5 + 4 * rank, 9 + 6 * rank, r, out bool crit);
                    HurtFoe(a, dmg, Elem.Cold, crit, 3);
                    Wake(a);
                }
                Emit(new Ev { Kind = "nova", X = Me.X, Y = Me.Y, V = 6, E = Elem.Cold });
                break;
            case "chain":
            {
                var hit = new List<int>();
                double x = Me.X, y = Me.Y;
                Actor next = t ?? FoesWithin(tx, ty, 2.5).OrderBy(a => Dist(a.X, a.Y, tx, ty)).FirstOrDefault();
                if (next == null) { Emit(new Ev { Kind = "bolt", X = x, Y = y, X2 = tx, Y2 = ty }); break; }
                for (int k = 0; k <= Data.ChainJumps(rank) && next != null; k++)
                {
                    Emit(new Ev { Kind = "bolt", X = x, Y = y, X2 = next.X, Y2 = next.Y });
                    double dmg = SpellRoll(1, 14 + 6 * rank, r, out bool crit);
                    HurtFoe(next, dmg, Elem.Light, crit, 0);
                    Wake(next);
                    hit.Add(next.Id);
                    x = next.X; y = next.Y;
                    Actor from = next;
                    next = Here.Actors.Where(a => a.Alive && !a.Npc && !hit.Contains(a.Id) && Dist(a, from) < 6 && Map.Sight(from.X, from.Y, a.X, a.Y))
                        .OrderBy(a => Dist(a, from)).FirstOrDefault();
                }
                break;
            }
            case "shield":
                Me.Shield = 20 + 12 * rank;
                Me.ShieldT = 60;
                Emit(new Ev { Kind = "shield", A = 0 });
                break;
            case "meteor":
            {
                double dmg = SpellRoll(30 + 15 * rank, 45 + 22 * rank, r, out bool crit);
                Here.Zones.Add(new Zone { Id = NextId++, Kind = "meteor", X = tx, Y = ty, R = 3, Delay = 1, Left = 0, Dmg = dmg, Elem = Elem.Fire, FromHero = true });
                Here.Zones.Add(new Zone { Id = NextId++, Kind = "burn", X = tx, Y = ty, R = 2.5, Delay = 1, Left = 3, Every = 0.5, Dmg = (5 + 3 * rank) * (1 + s.SpellPct / 100.0) / 2, Elem = Elem.Fire, FromHero = true });
                break;
            }
        }
    }

    /// <summary>A melee swing at a place: the nearest foe in front, if any.</summary>
    private void MeleeAt(double tx, double ty, double mult, Rng r, double knock = 0)
    {
        Actor t = FoesWithin(Me.X, Me.Y, 1.6).OrderBy(a => Dist(a.X, a.Y, tx, ty)).FirstOrDefault();
        if (t != null) Strike(t, mult, r, knock);
    }

    /// <summary>Loose an arrow toward a place with the weapon's damage (rolled now, the hit decided when it lands).</summary>
    private void Shoot(string kind, double tx, double ty, double mult, Rng r, bool pierce, double delay = 0)
    {
        Stats s = Hero.S;
        bool crit = r.Chance(s.Crit / 100.0);
        var (phys, fire, cold, light) = WeaponRoll(mult * (crit ? 1.5 : 1), r);
        Missile m = Launch(kind, tx, ty, 18, phys + fire + cold + light, Elem.Phys, true, crit);
        m.Accuracy = s.Accuracy;
        m.Pierce = pierce;
        if (cold > 0) m.Slow = 1.5;
        if (delay > 0) { m.X -= m.VX * delay; m.Y -= m.VY * delay; m.Left += 18 * delay; }
    }

    private Missile Launch(string kind, double tx, double ty, double speed, double dmg, Elem e, bool fromHero, bool crit, Actor from = null)
    {
        from ??= Me;
        double dx = tx - from.X, dy = ty - from.Y, len = Math.Max(0.01, Math.Sqrt(dx * dx + dy * dy));
        var m = new Missile
        {
            Id = NextId++, Kind = kind, X = from.X + dx / len * 0.3, Y = from.Y + dy / len * 0.3, VX = dx / len * speed, VY = dy / len * speed,
            Left = 16, FromHero = fromHero, Owner = from.Id, Dmg = dmg, Elem = e, Crit = crit,
        };
        Here.Missiles.Add(m);
        return m;
    }

    private void MonsterBlow(Actor a)
    {
        MonDef m = a.M;
        Rng r = R();
        double dmg = (m.DMin + r.Float() * (m.DMax - m.DMin)) * a.DmgX;
        if (a.ActSkill == "volley")
        {
            double ang = Math.Atan2(a.ActX - a.X, a.ActY - a.Y);
            for (int k = -2; k <= 2; k++)
            {
                double an = ang + k * 0.22;
                Missile mm = Launch("ember", a.X + Math.Sin(an) * 10, a.Y + Math.Cos(an) * 10, 9, dmg * 0.7, Elem.Fire, false, false, a);
                mm.Accuracy = a.Accuracy;
            }
            return;
        }
        if (m.Missile != null)
        {
            Missile mm = Launch(m.Missile, Me.X, Me.Y, m.Missile == "arrow" ? 15 : 9, dmg, m.Elem, false, false, a);
            mm.Accuracy = a.Accuracy;
            if (m.Elem == Elem.Cold) mm.Slow = 2;
            return;
        }
        double reach = a.Radius + Me.Radius + m.Range * 0.6 + 0.6;
        if (!Me.Alive || Dist(a, Me) > reach) return;
        double before = Me.Life;
        HurtHero(a, dmg, m.Elem, a.Accuracy, true);
        if (Me.Life < before)
        {
            // A champion's traits on a blow that landed.
            if (a.Has("fire")) HurtHero(a, dmg * 0.35, Elem.Fire, 0, true);
            if (a.Has("frost")) Me.Slow = Math.Max(Me.Slow, 2.5);
            if (a.Has("vampiric")) a.Life = Math.Min(a.MaxLife, a.Life + (before - Me.Life));
        }
        if (m.Ai == Ai.Charger && Me.Alive)
        {
            double dx = Me.X - a.X, dy = Me.Y - a.Y, len = Math.Max(0.01, Math.Sqrt(dx * dx + dy * dy));
            for (int k = 0; k < 4; k++) Move(Me, dx / len * 0.3, dy / len * 0.3);
        }
    }

    // ---- damage -----------------------------------------------------------------------------------------------------

    /// <summary>Damage to a monster after its resistance; it wakes, may stagger, may die. Returns what it took.</summary>
    private double HurtFoe(Actor t, double amount, Elem e, bool crit, double slow, bool dot = false)
    {
        if (!t.Alive) return 0;
        double res = e == Elem.Phys ? (t.Has("stone") && !dot ? 0.4 : 0) : t.Resist / 100.0;
        double dmg = Math.Max(1, Math.Round(amount * (1 - res)));
        t.Life -= dmg;
        if (slow > 0) t.Slow = Math.Max(t.Slow, slow);
        if (!dot)
        {
            // Fire keeps burning for three seconds (a fifth of the hit a second); a cold critical freezes it solid.
            if (e == Elem.Fire && dmg >= 2)
            {
                if (t.Burn <= 0) Emit(new Ev { Kind = "ailing", A = t.Id, Text = "burning" });
                t.BurnDps = Math.Max(t.Burn > 0 ? t.BurnDps : 0, dmg * 0.2);
                t.Burn = 3;
            }
            if (e == Elem.Cold && crit && !t.Boss && t.Life > 0)
            {
                t.Frozen = 1.2;
                // Frozen mid-blow, the blow is lost.
                if (t.Act is "attack" or "cast" or "walk") { t.Act = "idle"; t.Path.Clear(); }
                Emit(new Ev { Kind = "ailing", A = t.Id, Text = "frozen" });
            }
        }
        Emit(new Ev { Kind = "hit", A = t.Id, X = t.X, Y = t.Y, V = (int)dmg, E = e, Crit = crit });
        if (!t.Awake) Wake(t);
        if (t.Life <= 0) { Die(t); return dmg; }
        // A heavy blow staggers (not a boss, not twice in a row).
        if (dmg > t.MaxLife / 6 && t.Act is not ("hit" or "dead") && !t.Boss)
        {
            t.Act = "hit"; t.ActT = 0; t.ActDur = 0.3; t.ActAt = 99; t.ActDone = true;
        }
        return dmg;
    }

    /// <summary>
    /// A monster's blow or missile against the hero: hit roll against armour, then dodge, block, the arcane shield,
    /// resistances; a hard blow staggers.
    /// </summary>
    private void HurtHero(Actor from, double amount, Elem e, double accuracy, bool melee)
    {
        if (!Me.Alive || Depth == 0) return;
        Stats s = Hero.S;
        Rng r = R();
        int alvl = from?.Level ?? AreaLevel;
        if (accuracy > 0 && !r.Chance(HitChance(accuracy, s.Armour, alvl, Hero.Level))) { Emit(new Ev { Kind = "miss", A = 0, X = Me.X, Y = Me.Y }); return; }
        if (s.Evade > 0 && r.Chance(s.Evade / 100.0)) { Emit(new Ev { Kind = "dodge", A = 0, X = Me.X, Y = Me.Y }); return; }
        if (s.Block > 0 && Me.Act is "idle" or "walk" or "attack" && r.Chance(s.Block / 100.0)) { Emit(new Ev { Kind = "block", A = 0, X = Me.X, Y = Me.Y }); return; }
        double dmg = amount * (1 - s.Res(e) / 100.0);
        if (Me.Shield > 0)
        {
            double take = Math.Min(Me.Shield, dmg);
            Me.Shield -= take;
            dmg -= take;
            if (Me.Shield <= 0) { Me.ShieldT = 0; Emit(new Ev { Kind = "shieldbreak", A = 0 }); }
        }
        dmg = Math.Round(dmg);
        if (dmg <= 0) return;
        Me.Life -= dmg;
        Emit(new Ev { Kind = "hurt", A = 0, X = Me.X, Y = Me.Y, V = (int)dmg, E = e });
        // Fire clings for two seconds; a hag's or a ghoul's blow poisons for four.
        if (e == Elem.Fire && dmg >= 2) { Me.BurnDps = Math.Max(Me.Burn > 0 ? Me.BurnDps : 0, dmg * 0.15); Me.Burn = 2; }
        if (melee && from?.Def is "hag" or "ghoul") { Me.PoisonDps = Math.Max(Me.Poison > 0 ? Me.PoisonDps : 0, dmg * 0.2); Me.Poison = 4; }
        if (Me.Life <= 0) { Me.Life = 0; HeroDies(from); return; }
        if (dmg > Me.MaxLife * 0.2 && Me.Act is "idle" or "walk")
        {
            Me.Act = "hit"; Me.ActT = 0; Me.ActDur = 0.25; Me.ActAt = 99; Me.ActDone = true;
        }
    }

    private void Die(Actor t)
    {
        t.Life = 0;
        t.Act = "dead";
        t.DeadT = 0;
        t.Path.Clear();
        Emit(new Ev { Kind = "die", A = t.Id, X = t.X, Y = t.Y, Text = t.Name });
        if (t.Has("fire"))
        {
            // A fire-touched champion bursts into flame as it falls: stand clear.
            Here.Zones.Add(new Zone { Id = NextId++, Kind = "burst", X = t.X, Y = t.Y, R = 2.2, Delay = 0.35, Dmg = t.DmgX * (t.M?.DMax ?? 5) * 0.7, Elem = Elem.Fire, FromHero = false });
        }
        Hero.Kills++;
        MonDef m = t.M;
        double xp = 4 * m.Xp * (1 + 0.3 * (t.Level - 1)) * (t.Champion ? 3 : 1) * (t.Title != null && !t.Champion ? 4 : 1);
        // Little for monsters far below the hero.
        if (Hero.Level > t.Level + 5) xp *= Math.Max(0.1, 1 - (Hero.Level - t.Level - 5) * 0.15);
        if (Hero.BlessingLeft > 0 && Hero.Blessing == "wisdom") xp *= 1.5;
        GainXp((long)Math.Round(xp));
        double quality = t.Boss ? 10 : t.Title != null && !t.Champion ? 7 : t.Champion ? 2.5 : 1;
        foreach (Item it in Loot.Drop(t.Level, quality, Hero.S.MagicFind, Hero.S.GoldFind, R())) Place(it, t.X, t.Y);
        var u = Data.Uniques.FirstOrDefault(q => q.title == t.Title);
        if (u.quest != null)
        {
            if (Hero.Quests.TryGetValue(u.quest, out int qs) ? qs < 2 : true)
            {
                Hero.Quests[u.quest] = 2;
                Emit(new Ev { Kind = "quest", Text = $"{t.Title} is slain. Return to the elder." });
            }
            if (t.Def == "vorgath") { Won = true; Emit(new Ev { Kind = "won", Text = "Vorgath falls. The Emberdeep goes quiet." }); }
        }
    }

    public void GainXp(long xp)
    {
        if (Hero.Level >= Data.MaxLevel) return;
        Hero.Xp += xp;
        while (Hero.Level < Data.MaxLevel && Hero.Xp >= Data.XpToNext(Hero.Level))
        {
            Hero.Xp -= Data.XpToNext(Hero.Level);
            Hero.Level++;
            Hero.AttrPoints += 5;
            Hero.SkillPoints += 1;
            Hero.Changed();
            Me.Life = Me.MaxLife = Hero.S.MaxLife;
            Me.Mana = Me.MaxMana = Hero.S.MaxMana;
            Emit(new Ev { Kind = "level", V = Hero.Level, X = Me.X, Y = Me.Y });
        }
    }

    private void HeroDies(Actor by)
    {
        Me.Act = "dead";
        Me.DeadT = 0;
        Hero.Deaths++;
        int lost = Hero.Gold / 10;
        Hero.Gold -= lost;
        Order = new Order();
        Emit(new Ev { Kind = "death", A = 0, X = Me.X, Y = Me.Y, V = lost, Text = by?.Name ?? "the dark" });
    }

    /// <summary>Back in the village with full life and mana; the floor's monsters forget and heal.</summary>
    private void Revive()
    {
        foreach (Actor a in Here.Actors.Where(a => a.Alive)) { a.Awake = false; a.Life = a.MaxLife; }
        Me.Life = Me.MaxLife;
        Me.Mana = Me.MaxMana;
        Me.Act = "idle";
        Me.DeadT = 0;
        // Dying in a lair loses it: the portal closes behind the dead.
        if (Depth >= Data.LairBase && Lair != null)
        {
            Floors.Remove(Depth);
            Lair = null;
            Emit(new Ev { Kind = "lairlost" });
        }
        Enter(0, null);
    }

    // ---- missiles and zones -----------------------------------------------------------------------------------------

    private void Missiles(double dt)
    {
        Floor f = Map;
        foreach (Missile m in Here.Missiles.ToList())
        {
            double step = Math.Sqrt(m.VX * m.VX + m.VY * m.VY) * dt;
            int sub = Math.Max(1, (int)Math.Ceiling(step / 0.1));
            bool gone = false;
            for (int k = 0; k <= sub && !gone; k++)
            {
                // Where it is now first (a foe right beside the shooter is struck), then a step on.
                if (k > 0)
                {
                    m.X += m.VX * dt / sub;
                    m.Y += m.VY * dt / sub;
                    m.Left -= step / sub;
                }
                if (!f.Floor_((int)Math.Floor(m.X), (int)Math.Floor(m.Y)) || m.Left <= 0) { gone = true; break; }
                if (m.FromHero)
                {
                    Actor t = Here.Actors.FirstOrDefault(a => a.Alive && !a.Npc && !m.Struck.Contains(a.Id) && Dist(a.X, a.Y, m.X, m.Y) < a.Radius + 0.2);
                    if (t == null) continue;
                    m.Struck.Add(t.Id);
                    Rng r = R();
                    if (m.Accuracy > 0 && !r.Chance(HitChance(m.Accuracy, t.Defense, Hero.Level, t.Level)))
                        Emit(new Ev { Kind = "miss", A = t.Id, X = t.X, Y = t.Y });
                    else
                    {
                        double dealt = HurtFoe(t, m.Dmg, m.Elem, m.Crit, m.Slow);
                        if (m.Accuracy > 0 && Hero.S.LifeSteal > 0) Me.Life = Math.Min(Me.MaxLife, Me.Life + dealt * Hero.S.LifeSteal / 100.0);
                        if (m.Kind == "firebolt") Emit(new Ev { Kind = "blast", X = m.X, Y = m.Y, V = 1, E = Elem.Fire });
                    }
                    if (!m.Pierce) gone = true;
                }
                else if (Me.Alive && Dist(Me.X, Me.Y, m.X, m.Y) < Me.Radius + 0.25)
                {
                    Actor from = Find(m.Owner);
                    HurtHero(from, m.Dmg, m.Elem, m.Accuracy, false);
                    if (m.Slow > 0 && Me.Alive) Me.Slow = Math.Max(Me.Slow, m.Slow);
                    gone = true;
                }
            }
            if (gone)
            {
                Here.Missiles.Remove(m);
                if (m.Kind is "firebolt" or "ember" or "frost") Emit(new Ev { Kind = "fizzle", X = m.X, Y = m.Y, E = m.Elem });
            }
        }
    }

    private void Zones(double dt)
    {
        foreach (Zone z in Here.Zones.ToList())
        {
            if (z.Delay > 0)
            {
                z.Delay -= dt;
                if (z.Delay > 0) continue;
                if (z.Kind is "meteor" or "burst")
                {
                    Emit(new Ev { Kind = "blast", X = z.X, Y = z.Y, V = (int)z.R, E = Elem.Fire });
                    ZoneHit(z, z.Dmg);
                    Here.Zones.Remove(z);
                    continue;
                }
            }
            z.Next -= dt;
            if (z.Next <= 0)
            {
                z.Next += z.Every;
                ZoneHit(z, z.Dmg);
                if (z.Kind == "rain") Emit(new Ev { Kind = "rain", X = z.X, Y = z.Y, V = (int)z.R });
            }
            z.Left -= dt;
            if (z.Left <= 0) Here.Zones.Remove(z);
        }
    }

    private void ZoneHit(Zone z, double dmg)
    {
        if (z.FromHero)
        {
            Rng r = R();
            foreach (Actor a in FoesWithin(z.X, z.Y, z.R)) HurtFoe(a, dmg * (0.85 + 0.3 * r.Float()), z.Elem, false, 0);
        }
        else if (Dist(Me.X, Me.Y, z.X, z.Y) <= z.R) HurtHero(null, dmg, z.Elem, 0, false);
    }

    // ---- things on the floor ----------------------------------------------------------------------------------------

    /// <summary>Lay an item on the floor near a point, on an open cell.</summary>
    public Drop Place(Item it, double x, double y)
    {
        Rng r = R();
        Floor f = Map;
        double px = x, py = y;
        for (int k = 0; k < 12; k++)
        {
            double ang = r.Float() * Math.Tau, d = 0.4 + r.Float() * (0.6 + k * 0.1);
            double cx = x + Math.Sin(ang) * d, cy = y + Math.Cos(ang) * d;
            if (f.Walk(cx, cy) && !Here.Drops.Any(o => Dist(o.X, o.Y, cx, cy) < 0.45)) { px = cx; py = cy; break; }
        }
        var drop = new Drop { Id = NextId++, X = px, Y = py, Item = it };
        Here.Drops.Add(drop);
        Emit(new Ev { Kind = "drop", X = px, Y = py, A = drop.Id, V = (int)it.Rarity, Text = it.Base });
        return drop;
    }

    /// <summary>Pick up a drop (gold to the purse, draughts to the belt, the rest to the pack). False when there is no room.</summary>
    public bool Pick(Drop d)
    {
        bool gold = d.Item.B?.Kind == Kind.Gold;
        int count = d.Item.Count;
        if (!Hero.Take(d.Item))
        {
            Emit(new Ev { Kind = "full", Text = "No room in the pack." });
            return false;
        }
        Here.Drops.Remove(d);
        Emit(new Ev { Kind = gold ? "gold" : "pickup", V = gold ? count : (int)d.Item.Rarity, Text = d.Item.Title, X = d.X, Y = d.Y });
        return true;
    }

    /// <summary>Use a prop (null: the way back from a scroll, in the village).</summary>
    public void Use(Prop p)
    {
        if (p == null)
        {
            if (Depth != 0 || PortalDepth <= 0) return;
            int depth = PortalDepth;
            PortalDepth = 0;
            Enter(depth, (PortalX, PortalY));
            return;
        }
        switch (p.Kind)
        {
            case "chest" or "crate":
                if (Here.Opened.Contains(p.Id)) return;
                Here.Opened.Add(p.Id);
                Emit(new Ev { Kind = "chest", A = p.Id, X = p.X + 0.5, Y = p.Y + 0.5, Text = p.Kind });
                foreach (Item it in Loot.Drop(AreaLevel + 1, p.Kind == "chest" ? (Depth >= Data.LairBase ? 5 : 2) : 1, Hero.S.MagicFind, Hero.S.GoldFind, R())) Place(it, p.X + 0.5, p.Y + 0.5);
                break;
            case "barrel" or "urn":
            {
                if (Here.Opened.Contains(p.Id)) return;
                Here.Opened.Add(p.Id);
                Map.Broken(p);
                Emit(new Ev { Kind = "break", A = p.Id, X = p.X + 0.5, Y = p.Y + 0.5, Text = p.Kind });
                // Now and then something inside: gold or a draught, rarely more.
                Rng r = R();
                if (r.Chance(0.4)) foreach (Item it in Loot.Drop(AreaLevel, 0.6, Hero.S.MagicFind, Hero.S.GoldFind, r)) Place(it, p.X + 0.5, p.Y + 0.5);
                break;
            }
            case "waystone":
                if (Hero.Waystones.Add(Depth)) Emit(new Ev { Kind = "waystone", V = Depth, Text = $"The waystone of {Map.Def.Name} wakes." });
                Emit(new Ev { Kind = "waymenu", V = Depth });
                break;
            case "down":
                if (Depth < Data.Bottom) Enter(Depth + 1, null);
                break;
            case "up":
                if (Depth > 0)
                {
                    int to = Depth - 1;
                    Enter(to, null);
                    Prop down = Map.Find("down");
                    if (down != null) { Me.X = down.X + 1; Me.Y = down.Y + down.H + 1.0; if (!Map.Walk(Me.X, Me.Y)) (Me.X, Me.Y) = (Map.Start.x + 0.5, Map.Start.y + 0.5); }
                }
                break;
            case "stash":
                Emit(new Ev { Kind = "stash" });
                break;
            case "exit":
                LeaveLair();
                break;
            case "shrine":
            {
                if (Here.Opened.Contains(p.Id)) return;
                Here.Opened.Add(p.Id);
                var sh = Data.Shrines[new Rng(Seed ^ (ulong)(p.Id * 7919 + Depth * 104729)).Int(0, Data.Shrines.Length)];
                if (sh.seconds > 0) { Hero.Blessing = sh.id; Hero.BlessingLeft = sh.seconds; Hero.Changed(); }
                else { Me.Life = Hero.S.MaxLife; Me.Mana = Hero.S.MaxMana; }
                Emit(new Ev { Kind = "shrine", A = p.Id, X = p.X + 0.5, Y = p.Y + 0.5, Text = $"{sh.name}: {sh.about}" });
                break;
            }
            case "well":
                Emit(new Ev { Kind = "say", Text = "The water is black and very cold. Something far below breathes warm air up the shaft." });
                break;
        }
    }

    // ---- lairs ------------------------------------------------------------------------------------------------------

    /// <summary>Read a treasure map: its lair's portal opens beside the hero. Null when done, else why not.</summary>
    public string OpenLair(Item map)
    {
        if (Depth >= Data.LairBase) return "not from inside a lair";
        if (Lair != null) return "a lair's portal is already open";
        if (map.Lair < 0) return "the map is blank";
        double ax = Me.X + Math.Sin(Me.Facing) * 1.6, ay = Me.Y + Math.Cos(Me.Facing) * 1.6;
        if (!Map.Walk(ax, ay)) (ax, ay) = (Me.X, Me.Y + 1.4);
        if (!Map.Walk(ax, ay)) (ax, ay) = (Me.X + 1.4, Me.Y);
        Lair = new LairState { Kind = map.Lair, Seed = map.MapSeed, Level = map.Level, FromDepth = Depth, X = ax, Y = ay };
        Emit(new Ev { Kind = "lairportal", X = ax, Y = ay, Text = Data.Lairs[map.Lair].Name });
        return null;
    }

    /// <summary>Step through the lair's portal.</summary>
    public void EnterLair()
    {
        if (Lair == null) return;
        int depth = Data.LairBase + Lair.Kind;
        Floors.Remove(depth);
        Enter(depth, null);
        Emit(new Ev { Kind = "lair", Text = Data.Lairs[Lair.Kind].Name });
    }

    /// <summary>Out of the lair by its way out, back where the map was read; the lair closes behind.</summary>
    public void LeaveLair()
    {
        if (Lair == null || Depth < Data.LairBase) return;
        LairState l = Lair;
        Floors.Remove(Depth);
        Lair = null;
        Enter(l.FromDepth, (l.X, l.Y + 1.0));
        Emit(new Ev { Kind = "lairclosed", Text = Data.Lairs[l.Kind].Name });
    }

    /// <summary>Travel by waystone to a floor whose waystone has been touched. Null when done.</summary>
    public string Travel(int depth)
    {
        if (!Hero.Waystones.Contains(depth)) return "that waystone sleeps";
        Floor f = Floor.Make(Seed, depth);
        Prop w = f.Find("waystone");
        Enter(depth, w == null ? null : (w.X + w.W / 2.0, w.Y + w.H + 0.8));
        return null;
    }

    /// <summary>Drink or read what is in a belt place (1-4 on the keys).</summary>
    public string UseBelt(int k)
    {
        Item it = k >= 0 && k < 4 ? Hero.Belt[k] : null;
        if (it == null) return "nothing there";
        string why = Consume(it);
        if (why == null && --it.Count <= 0)
        {
            // An empty belt place takes the same draught from the pack, if there is one.
            int more = Array.FindIndex(Hero.Pack, p => p?.Base == it.Base);
            Hero.Belt[k] = more >= 0 ? Hero.Pack[more] : null;
            if (more >= 0) Hero.Pack[more] = null;
        }
        return why;
    }

    /// <summary>Use a draught or a scroll from the pack.</summary>
    public string UsePack(int k)
    {
        Item it = k >= 0 && k < Hero.Pack.Length ? Hero.Pack[k] : null;
        if (it == null) return "nothing there";
        if (it.B?.Wearable == true) return Hero.EquipFromPack(k);
        string why = Consume(it);
        if (why == null && --it.Count <= 0) Hero.Pack[k] = null;
        return why;
    }

    private string Consume(Item it)
    {
        if (!Me.Alive) return "you are dead";
        switch (it.B?.Kind)
        {
            case Kind.PotLife:
                Me.Life = Math.Min(Me.MaxLife, Me.Life + Data.PotionHeal + Me.MaxLife * 0.15);
                Emit(new Ev { Kind = "potion", E = Elem.Fire });
                return null;
            case Kind.PotMana:
                Me.Mana = Math.Min(Me.MaxMana, Me.Mana + Data.PotionHeal + Me.MaxMana * 0.15);
                Emit(new Ev { Kind = "potion", E = Elem.Cold });
                return null;
            case Kind.Scroll:
                if (Depth == 0) return "you are already in the village";
                PortalDepth = Depth; PortalX = Me.X; PortalY = Me.Y;
                Emit(new Ev { Kind = "portal", X = Me.X, Y = Me.Y });
                Enter(0, (PortalSpot.x, PortalSpot.y + 1.2));
                return null;
            case Kind.Map:
                return OpenLair(it);
            default:
                return "it cannot be used";
        }
    }

    // ---- the village ------------------------------------------------------------------------------------------------

    /// <summary>What the elder says now, and gives or rewards: the first quest not yet rewarded.</summary>
    public string TalkElder()
    {
        foreach (var q in Data.Quests)
        {
            int s = Hero.Quests.TryGetValue(q.id, out int v) ? v : 0;
            if (s == 0) { Hero.Quests[q.id] = 1; Emit(new Ev { Kind = "quest", Text = $"New quest: {q.name}" }); return q.text; }
            if (s == 1) return q.text;
            if (s == 2)
            {
                Hero.Quests[q.id] = 3;
                Reward(q.id);
                return q.done;
            }
        }
        return CanGoDeeper
            ? "The fire is out, they say. Then why do the stones still sweat? Something stirs further down than Vorgath ever went. If you would go again, it will be worse."
            : "Rest, if you can. The village is safer for what you have done.";
    }

    private void Reward(string quest)
    {
        switch (quest)
        {
            case "hag": Hero.SkillPoints += 1; Hero.Gold += 300; Place(Loot.Map(Hero.Level + 2, R(), 0), Me.X, Me.Y); break;
            case "rite":
                Hero.AttrPoints += 5;
                Place(Loot.Make(Loot.PickBase(Hero.Level + 2, R()).Id, Rarity.Rare, Hero.Level + 3, R()), Me.X, Me.Y);
                Place(Loot.Map(Hero.Level + 3, R(), 2), Me.X, Me.Y);
                break;
            case "warden": Hero.Gold += 2000; Place(Loot.Map(Hero.Level + 4, R(), 3), Me.X, Me.Y); break;
        }
        Emit(new Ev { Kind = "reward", Text = quest });
    }

    /// <summary>Sell a pack item to a merchant. Null when done.</summary>
    public string Sell(int place)
    {
        Item it = place >= 0 && place < Hero.Pack.Length ? Hero.Pack[place] : null;
        if (it == null) return "nothing there";
        Hero.Gold += it.Worth;
        Hero.Pack[place] = null;
        if (it.B.Wearable && it.Rarity >= Rarity.Magic)
        {
            // Oskar sells on what he buys, but keeps no more than two dozen things: the oldest go.
            SmithWares.Add(it);
            while (SmithWares.Count > 24) SmithWares.RemoveAt(0);
        }
        Emit(new Ev { Kind = "gold", V = it.Worth, Text = $"Sold {it.Title}" });
        return null;
    }

    /// <summary>Buy from the smith (index into its wares) or the healer ("pot-hp", "pot-mp", "scroll").</summary>
    public string Buy(string from, int index)
    {
        Item it;
        List<Item> unique = from == "smith" ? SmithWares : from == "peddler" ? PeddlerWares : null;
        if (unique != null)
        {
            if (index < 0 || index >= unique.Count) return "no such ware";
            it = unique[index];
        }
        else
        {
            List<Item> stock = Loot.HealerStock();
            if (index < 0 || index >= stock.Count) return "no such ware";
            it = stock[index];
        }
        int price = Price(it);
        if (Hero.Gold < price) return "not enough gold";
        if (unique != null)
        {
            // One of a kind (a sword, a map, a gem): it leaves the shelf.
            if (Hero.Free() == 0) return "no room";
            Hero.Take(it);
            unique.RemoveAt(index);
        }
        else if (!Hero.Take(new Item(it.Base))) return "no room";
        Hero.Gold -= price;
        Emit(new Ev { Kind = "gold", V = -price, Text = $"Bought {it.Name}" });
        return null;
    }

    public static int Price(Item it) => it.B.Stacks ? it.B.Price
        : it.B.Kind == Kind.Map ? 800 + it.Level * 60
        : it.B.Kind == Kind.Gem ? new[] { 150, 500, 1500 }[Math.Clamp(it.Grade, 0, 2)]
        : Math.Max(5, it.Worth * 4);

    /// <summary>What Brother Aldric asks for a blessing: more the stronger the hero, more again deeper.</summary>
    public int BlessingCost => 60 * Hero.Level * (1 + Tier);

    /// <summary>Brother Aldric blesses the hero as a shrine would, for five minutes, for gold. Null when done.</summary>
    public string Bless(string id)
    {
        var sh = Data.Shrines.FirstOrDefault(x => x.id == id);
        if (sh.id == null || sh.seconds <= 0) return "no such blessing";
        if (Hero.Gold < BlessingCost) return "not enough gold";
        Hero.Gold -= BlessingCost;
        Hero.Blessing = id;
        Hero.BlessingLeft = 300;
        Hero.Changed();
        Emit(new Ev { Kind = "shrine", X = Me.X, Y = Me.Y, A = -1, Text = $"{sh.name.Replace("Shrine", "Blessing")}: {sh.about}" });
        return null;
    }

    /// <summary>Move a pack item to the stash, or back (index into the stash).</summary>
    public string ToStash(int place)
    {
        Item it = place >= 0 && place < Hero.Pack.Length ? Hero.Pack[place] : null;
        if (it == null) return "nothing there";
        if (Hero.Stash.Count >= StashSize) return "the stash is full";
        Hero.Stash.Add(it);
        Hero.Pack[place] = null;
        return null;
    }

    public string FromStash(int index)
    {
        if (index < 0 || index >= Hero.Stash.Count) return "nothing there";
        int free = Array.IndexOf(Hero.Pack, null);
        if (free < 0) return "the pack is full";
        Hero.Pack[free] = Hero.Stash[index];
        Hero.Stash.RemoveAt(index);
        return null;
    }

    // ---- gems --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Set a gem from the pack into an item with an empty socket (a pack place, or something worn: equipped). It
    /// stays there for good. Null when done, else why not.
    /// </summary>
    public string Socket(int gemPlace, int packPlace = -1, Slot? worn = null)
    {
        Item gem = gemPlace >= 0 && gemPlace < Hero.Pack.Length ? Hero.Pack[gemPlace] : null;
        if (gem?.B?.Kind != Kind.Gem) return "that is not a gem";
        Item target = worn is Slot s ? Hero.Equip[(int)s] : packPlace >= 0 && packPlace < Hero.Pack.Length ? Hero.Pack[packPlace] : null;
        if (target == null || target == gem) return "nothing to set it in";
        if (target.Sockets == 0) return "it has no sockets";
        if (!target.OpenSocket) return "its sockets are full";
        target.Gems.Add($"{gem.Base}:{gem.Grade}");
        Hero.Pack[gemPlace] = null;
        Hero.Changed();
        Emit(new Ev { Kind = "socket", Text = $"{gem.Name} set in {target.Name}" });
        return null;
    }

    /// <summary>What the smith asks to join three gems: more for the better grade.</summary>
    public static int CombineCost(int grade) => grade == 0 ? 100 : 400;

    /// <summary>
    /// The smith joins three gems of a kind and grade into one of the next grade, as many times as the pack and the
    /// purse allow. Returns how many were made.
    /// </summary>
    public int CombineGems()
    {
        int made = 0;
        for (int grade = 0; grade < 2; grade++)
            foreach (Data.GemDef d in Data.Gems)
                while (true)
                {
                    var places = Enumerable.Range(0, Hero.Pack.Length).Where(i => Hero.Pack[i]?.Base == "gem-" + d.Id && Hero.Pack[i].Grade == grade).Take(3).ToList();
                    if (places.Count < 3 || Hero.Gold < CombineCost(grade)) break;
                    Hero.Gold -= CombineCost(grade);
                    foreach (int p in places) Hero.Pack[p] = null;
                    Hero.Pack[places[0]] = Item.Gem(d.Id, grade + 1);
                    made++;
                }
        if (made > 0) Emit(new Ev { Kind = "gold", V = 0, Text = $"Oskar joins {made} gem{(made > 1 ? "s" : "")}" });
        return made;
    }

    /// <summary>Drop a pack item on the floor at the hero's feet.</summary>
    public string Discard(int place)
    {
        Item it = place >= 0 && place < Hero.Pack.Length ? Hero.Pack[place] : null;
        if (it == null) return "nothing there";
        Hero.Pack[place] = null;
        Place(it, Me.X, Me.Y);
        return null;
    }
}

/// <summary>A small deterministic random source (xorshift): the same seed, the same game.</summary>
public sealed class Rng
{
    private ulong _s;
    public Rng(ulong seed) { _s = seed * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL; if (_s == 0) _s = 1; Next(); }
    public ulong Next() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; return _s; }
    public double Float() => (Next() >> 11) * (1.0 / (1UL << 53));
    public int Int(int lo, int hiExclusive) => hiExclusive <= lo ? lo : lo + (int)(Float() * (hiExclusive - lo));
    public bool Chance(double p) => Float() < p;
}
