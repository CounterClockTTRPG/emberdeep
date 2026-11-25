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
    public List<Item> SmithWares = new();
    public bool Won;
    public double Accum;

    [JsonIgnore] public List<Ev> Events = new();
    [JsonIgnore] private Floor _map;
    [JsonIgnore] private int _mapDepth = -1;

    public Floor Map
    {
        get
        {
            if (_map == null || _mapDepth != Depth) { _map = Floor.Make(Seed, Depth); _mapDepth = Depth; }
            return _map;
        }
    }

    public FloorState Here => Floors[Depth];
    public IEnumerable<Actor> Foes => Here.Actors.Where(a => !a.Npc && !a.Hero);

    /// <summary>The way back in the village, beside the waystone.</summary>
    public static (double x, double y) PortalSpot => (Floor.VillageSize / 2 + 3.5, Floor.VillageSize / 2 + 1.5);

    public static Game New(ulong seed, Cls cls = Cls.Wanderer)
    {
        var g = new Game { Seed = seed, Hero = Hero.New(cls) };
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

    /// <summary>Go to a floor, arriving at a place (null: where the floor's arrivals arrive).</summary>
    public void Enter(int depth, (double x, double y)? at)
    {
        Depth = Math.Clamp(depth, 0, Data.Bottom);
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
        if (Depth > Hero.Deepest) Hero.Deepest = Depth;
        if (Depth == 0) SmithWares = Loot.SmithStock(Hero.Level, R());
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
        foreach (Spawn s in f.Spawns) st.Actors.Add(MakeMonster(s.Mon, s.X, s.Y, f.Depth, s.Champion, s.Unique, s.Pack));
        return st;
    }

    public Actor MakeMonster(string id, double x, double y, int depth, bool champion = false, string unique = null, int pack = 0)
    {
        MonDef m = Data.Mon(id);
        int lvl = Data.MonLevel(depth) + (champion ? 2 : 0) + (unique != null ? 3 : 0);
        double life = m.Life * (1 + 0.2 * (lvl - 1)), dmg = 0.8 * (1 + 0.1 * (lvl - 1));
        var u = Data.Uniques.FirstOrDefault(q => q.title == unique);
        if (champion) { life *= 2.5; dmg *= 1.3; }
        if (unique != null) { life *= u.lifeX; dmg *= u.dmgX; }
        var a = new Actor
        {
            Id = NextId++, Def = id, Level = lvl, X = x, Y = y, MaxLife = Math.Round(life), Life = Math.Round(life), DmgX = dmg,
            Champion = champion, Title = unique ?? (champion ? $"{m.Name} Champion" : null), Pack = pack,
            Accuracy = 10 + 9 * lvl, Defense = m.Armour + 5 * lvl, Resist = m.Resist, Facing = R().Float() * Math.Tau,
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
        HeroThink(dt);
        Act(Me, dt);
        foreach (Actor a in st.Actors.ToList())
        {
            if (!a.Alive) { a.DeadT += dt; continue; }
            Timers(a, dt);
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
        (a.Hero ? Hero.S.MoveSpeed : a.M?.Speed ?? 2) * (a.Slow > 0 ? 0.5 : 1) * (a.Champion ? 1.15 : 1);

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

    private static double Mass(Actor a) => a.Npc ? 1000 : a.Hero ? 3 : a.M?.Ai == Ai.Boss ? 20 : a.Radius > 0.55 ? 4 : 1;

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

    private bool Busy(Actor a) => a.Act is "attack" or "cast" or "hit" or "leap" or "dead" || a.Stun > 0;

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
                if (p == null && o.Target != -2) { Halt(); break; }
                (double px, double py) = p == null ? PortalSpot : (p.X + p.W / 2.0, p.Y + p.H / 2.0);
                double reach = p == null ? 1.2 : Math.Max(p.W, p.H) / 2.0 + 1.0;
                if (!WalkTo(Me, px, py, reach, dt)) break;
                Use(p);
                Halt();
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
            if (Me.Alive && Me.Hidden <= 0 && d < (m.Ai == Ai.Boss ? 12 : 9) && Map.Sight(a.X, a.Y, Me.X, Me.Y)) Wake(a);
            else { if (a.Act == "walk") a.Act = "idle"; return; }
        }
        if (!Me.Alive || Me.Hidden > 0)
        {
            if (a.Act == "walk") a.Act = "idle";
            if (!Me.Alive) a.Awake = false;
            return;
        }
        if (m.Ai == Ai.Boss) BossPhases(a);
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
        foreach (Actor o in Here.Actors.Where(o => o.Alive && !o.Npc && (o == a || (a.Pack > 0 && o.Pack == a.Pack))))
        {
            if (!o.Awake && o.M?.Ai == Ai.Boss) Emit(new Ev { Kind = "boss", A = o.Id, Text = o.Name });
            o.Awake = true;
        }
    }

    /// <summary>Vorgath calls imps out of the fire at two thirds and at one third of his life.</summary>
    private void BossPhases(Actor a)
    {
        int phase = a.Life < a.MaxLife / 3 ? 2 : a.Life < a.MaxLife * 2 / 3 ? 1 : 0;
        if (phase <= a.Phase) return;
        a.Phase = phase;
        Emit(new Ev { Kind = "say", A = a.Id, Text = phase == 1 ? "Burn, little light." : "The deep remembers its fire!" });
        Rng r = R();
        for (int k = 0; k < 2 + 2 * phase; k++)
        {
            double ang = r.Float() * Math.Tau;
            double x = a.X + Math.Sin(ang) * 3, y = a.Y + Math.Cos(ang) * 3;
            if (!Map.Walk(x, y)) continue;
            Actor imp = MakeMonster("imp", x, y, Depth, false, null, a.Pack);
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
            if (skill != "attack")
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
            dur = skill == "volley" ? 1.2 : m.AttackTime * 1.25 / (a.Slow > 0 ? 0.6 : 1);
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
        if (knock > 0 && t.Alive && t.M?.Ai != Ai.Boss)
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
                    if (a.M?.Ai != Ai.Boss) a.Stun = Math.Max(a.Stun, 1 + 0.15 * rank);
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
                    if (a.M?.Ai != Ai.Boss) a.Stun = Math.Max(a.Stun, 0.6);
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
        HurtHero(a, dmg, m.Elem, a.Accuracy, true);
        if (m.Ai == Ai.Charger && Me.Alive)
        {
            double dx = Me.X - a.X, dy = Me.Y - a.Y, len = Math.Max(0.01, Math.Sqrt(dx * dx + dy * dy));
            for (int k = 0; k < 4; k++) Move(Me, dx / len * 0.3, dy / len * 0.3);
        }
    }

    // ---- damage -----------------------------------------------------------------------------------------------------

    /// <summary>Damage to a monster after its resistance; it wakes, may stagger, may die. Returns what it took.</summary>
    private double HurtFoe(Actor t, double amount, Elem e, bool crit, double slow)
    {
        if (!t.Alive) return 0;
        double res = e == Elem.Phys ? 0 : t.Resist / 100.0;
        double dmg = Math.Max(1, Math.Round(amount * (1 - res)));
        t.Life -= dmg;
        if (slow > 0) t.Slow = Math.Max(t.Slow, slow);
        Emit(new Ev { Kind = "hit", A = t.Id, X = t.X, Y = t.Y, V = (int)dmg, E = e, Crit = crit });
        if (!t.Awake) Wake(t);
        if (t.Life <= 0) { Die(t); return dmg; }
        // A heavy blow staggers (not a boss, not twice in a row).
        if (dmg > t.MaxLife / 6 && t.Act is not ("hit" or "dead") && t.M?.Ai != Ai.Boss)
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
        int alvl = from?.Level ?? Data.MonLevel(Depth);
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
        Hero.Kills++;
        MonDef m = t.M;
        double xp = 4 * m.Xp * (1 + 0.3 * (t.Level - 1)) * (t.Champion ? 3 : 1) * (t.Title != null && !t.Champion ? 4 : 1);
        // Little for monsters far below the hero.
        if (Hero.Level > t.Level + 5) xp *= Math.Max(0.1, 1 - (Hero.Level - t.Level - 5) * 0.15);
        GainXp((long)Math.Round(xp));
        double quality = m.Ai == Ai.Boss ? 10 : t.Title != null && !t.Champion ? 7 : t.Champion ? 2.5 : 1;
        foreach (Item it in Loot.Drop(t.Level, quality, Hero.S.MagicFind, Hero.S.GoldFind, R())) Place(it, t.X, t.Y);
        var u = Data.Uniques.FirstOrDefault(q => q.title == t.Title);
        if (u.quest != null)
        {
            if (Hero.Quests.TryGetValue(u.quest, out int qs) ? qs < 2 : true)
            {
                Hero.Quests[u.quest] = 2;
                Emit(new Ev { Kind = "quest", Text = $"{t.Title} is slain. Return to the elder." });
            }
            if (m.Ai == Ai.Boss) { Won = true; Emit(new Ev { Kind = "won", Text = "Vorgath falls. The Emberdeep goes quiet." }); }
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
                if (z.Kind == "meteor")
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
            case "chest":
                if (Here.Opened.Contains(p.Id)) return;
                Here.Opened.Add(p.Id);
                Emit(new Ev { Kind = "chest", A = p.Id, X = p.X + 0.5, Y = p.Y + 0.5 });
                foreach (Item it in Loot.Drop(Data.MonLevel(Depth) + 1, 2, Hero.S.MagicFind, Hero.S.GoldFind, R())) Place(it, p.X + 0.5, p.Y + 0.5);
                break;
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
            case "well":
                Emit(new Ev { Kind = "say", Text = "The water is black and very cold. Something far below breathes warm air up the shaft." });
                break;
        }
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
        if (why == null && --it.Count <= 0) Hero.Belt[k] = null;
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
        return "Rest, if you can. The village is safer for what you have done.";
    }

    private void Reward(string quest)
    {
        switch (quest)
        {
            case "hag": Hero.SkillPoints += 1; Hero.Gold += 300; break;
            case "rite":
                Hero.AttrPoints += 5;
                Place(Loot.Make(Loot.PickBase(Hero.Level + 2, R()).Id, Rarity.Rare, Hero.Level + 3, R()), Me.X, Me.Y);
                break;
            case "warden": Hero.Gold += 2000; break;
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
        if (it.B.Wearable && it.Rarity >= Rarity.Magic) SmithWares.Add(it);
        Emit(new Ev { Kind = "gold", V = it.Worth, Text = $"Sold {it.Title}" });
        return null;
    }

    /// <summary>Buy from the smith (index into its wares) or the healer ("pot-hp", "pot-mp", "scroll").</summary>
    public string Buy(string from, int index)
    {
        Item it;
        if (from == "smith")
        {
            if (index < 0 || index >= SmithWares.Count) return "no such ware";
            it = SmithWares[index];
        }
        else
        {
            List<Item> stock = Loot.HealerStock();
            if (index < 0 || index >= stock.Count) return "no such ware";
            it = stock[index];
        }
        int price = Price(it);
        if (Hero.Gold < price) return "not enough gold";
        if (it.B.Wearable ? Hero.Free() == 0 : !Hero.Take(new Item(it.Base))) return "no room";
        if (it.B.Wearable) { Hero.Take(it); SmithWares.RemoveAt(index); }
        Hero.Gold -= price;
        Emit(new Ev { Kind = "gold", V = -price, Text = $"Bought {it.Name}" });
        return null;
    }

    public static int Price(Item it) => it.B.Stacks ? it.B.Price : Math.Max(5, it.Worth * 4);

    /// <summary>Move a pack item to the stash, or back (index into the stash).</summary>
    public string ToStash(int place)
    {
        Item it = place >= 0 && place < Hero.Pack.Length ? Hero.Pack[place] : null;
        if (it == null) return "nothing there";
        if (Hero.Stash.Count >= 60) return "the stash is full";
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
