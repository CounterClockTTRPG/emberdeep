using System;
using System.Linq;
using SfxGen;

namespace Emberdeep;

/// <summary>The rules: floors, items, the hero, fighting, skills, the village, saving.</summary>
public partial class SelfCheck
{
    /// <summary>A game on a floor with nothing else on it, for trying one thing at a time.</summary>
    private static Game Bare(Cls cls, int depth, ulong seed = 11)
    {
        Game g = Game.New(seed, cls);
        g.Enter(depth, null);
        g.Here.Actors.Clear();
        g.Here.Drops.Clear();
        g.Events.Clear();
        return g;
    }

    /// <summary>A monster beside the hero, awake.</summary>
    private static Actor Beside(Game g, string mon, double dx = 1.2, double dy = 0)
    {
        double x = g.Me.X + dx, y = g.Me.Y + dy;
        if (!g.Map.Walk(x, y)) (x, y) = (g.Me.X, g.Me.Y + 1.2);
        Actor a = g.MakeMonster(mon, x, y, g.Depth);
        a.Awake = true;
        g.Here.Actors.Add(a);
        return a;
    }

    private static void Run(Game g, double seconds) { for (double t = 0; t < seconds; t += 0.1) g.Step(0.1); }

    [Section]
    private void Floors()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 77 })
            for (int depth = 0; depth <= Data.Bottom; depth++)
            {
                Floor f = Floor.Make(seed, depth);
                string at = $"seed {seed} floor {depth}";
                Check(f.Walk(f.Start.x, f.Start.y), $"{at}: the hero arrives on open floor");
                int[] d = f.Distances(f.Start);
                int open = 0, lost = 0;
                for (int c = 0; c < f.W * f.H; c++) if (f.Cells[c] == 1 && !f.Blocked[c]) { open++; if (d[c] < 0) lost++; }
                Eq(lost, 0, $"{at}: every open cell can be walked to ({open} open)");
                Check(f.Find("waystone") != null, $"{at}: a waystone");
                if (depth > 0) Check(f.Find("up") != null, $"{at}: a way up");
                if (depth < Data.Bottom) Check(f.Find("down") != null, $"{at}: a way down");
                if (depth < Data.Bottom)
                {
                    Prop down = f.Find("down");
                    Check(d[down.Y * f.W + down.X] > 0 || depth == 0 && d[(down.Y + 2) * f.W + down.X] >= 0, $"{at}: the way down can be reached");
                }
                Check(f.Spawns.All(s => f.Walk(s.X, s.Y)), $"{at}: monsters stand on open floor");
                if (depth > 0) Check(f.Spawns.Count >= 8, $"{at}: monsters to fight ({f.Spawns.Count})");
            }
        Floor a = Floor.Make(5, 3), b = Floor.Make(5, 3), other = Floor.Make(6, 3);
        Check(a.Cells.SequenceEqual(b.Cells) && a.Props.Count == b.Props.Count && a.Spawns.Count == b.Spawns.Count, "the same seed makes the same floor");
        Check(!a.Cells.SequenceEqual(other.Cells), "another seed, another floor");
        Check(a.Spawns.Any(s => s.Unique == "Mother Wort"), "Mother Wort waits on the third floor");
        Check(Floor.Make(5, 6).Spawns.Any(s => s.Unique == "Ember-Priest Kael"), "Kael on the sixth");
        Check(Floor.Make(5, Data.Bottom).Spawns.Any(s => s.Mon == "vorgath"), "Vorgath at the bottom");
        Check(Data.MonstersFor(1).All(m => m.MinDepth <= 1) && Data.MonstersFor(9).Any(m => m.Id == "golem"), "monsters by depth");
        Floor f1 = Floor.Make(9, 1);
        Prop dn = f1.Find("down");
        var path = f1.Path(f1.Start.x + 0.5, f1.Start.y + 0.5, dn.X + 1, dn.Y + 1);
        Check(path.Count > 5 && Game.Dist(path[^1].x, path[^1].y, dn.X + 1, dn.Y + 1) < 1.5, "a path from the start to the way down");
    }

    [Section]
    private void Items()
    {
        var r = new Rng(3);
        Item plain = Loot.Make("long-sword", Rarity.Plain, 10, r);
        Check(plain.Mods.Count == 0 && plain.Name == "Long Sword" && plain.Damage == (5, 14), "a plain sword: its base's damage");
        for (int k = 0; k < 200; k++)
        {
            Item m = Loot.Make("war-axe", Rarity.Magic, 12, r);
            Item q = Loot.Make("plate-armour", Rarity.Rare, 20, r);
            if (m.Mods.Count is < 1 or > 2) { Check(false, $"a magic item has 1-2 affixes, got {m.Mods.Count}"); break; }
            if (q.Mods.Count is < 3 or > 5) { Check(false, $"a rare has 3-5 affixes, got {q.Mods.Count}"); break; }
            if (m.Mods.Select(x => x.Stat).Distinct().Count() != m.Mods.Count) { Check(false, "no stat twice on one item"); break; }
        }
        Item ring = Loot.Make("ring", Rarity.Plain, 5, r);
        Check(ring.Rarity == Rarity.Magic && ring.Mods.Count > 0, "a ring is never plain");
        Item cruel = new("short-sword") { Mods = { new Mod(Stat.DmgPct, 50) } };
        Eq(cruel.Damage, (3, 11), "+50% damage on a 2-7 sword");
        int gold = 0, pots = 0, wear = 0, rares = 0;
        for (int k = 0; k < 3000; k++)
            foreach (Item it in Loot.Drop(10, k % 10 == 0 ? 10 : 1, 0, 0, r))
            {
                if (it.Base == "gold") gold++;
                else if (it.B.Stacks) pots++;
                else wear++;
                if (it.Rarity == Rarity.Rare) rares++;
            }
        Check(gold > 200 && pots > 100 && wear > 200 && rares > 10, $"monsters drop gold, draughts, things to wear, now and then a rare ({gold}/{pots}/{wear}/{rares})");
        Check(Loot.SmithStock(5, r).Count == 12, "the smith has a dozen wares");
        Check(Data.Bases.All(b => Godot.FileAccess.FileExists($"res://assets/icons/{b.Icon}.png")), "every item base has its icon");
        Check(Data.Skills.All(s => Godot.FileAccess.FileExists($"res://assets/icons/{s.Icon}.png")), "every skill has its icon");
    }

    [Section]
    private void Heroes()
    {
        foreach (Cls c in Enum.GetValues<Cls>())
        {
            Hero h = Hero.New(c);
            Check(h.Weapon != null && h.S.MaxLife > 30 && h.S.MaxMana > 10 && h.S.DMax > h.S.DMin, $"{c}: armed, alive, with mana");
            Check(h.Skills.Count == 1 && h.Right == h.Skills.Keys.First(), $"{c}: knows its first skill, on the right button");
            Eq(Data.SkillsOf(c).Count(), 7, $"{c}: seven skills");
            Check(Data.SkillsOf(c).All(s => Data.SkillNumbers(s.Id, 1) != ""), $"{c}: every skill says what it does");
        }
        Hero w = Hero.New(Cls.Wanderer);
        Check(w.S.Block > 0 && !w.S.Ranged, "the wanderer blocks with a buckler, fights up close");
        Check(Hero.New(Cls.Rogue).S.Ranged, "the rogue shoots");
        Check(w.Learn("cleave") != null, "no skill points at level 1");
        w.SkillPoints = 1;
        Check(w.Learn("cleave") != null, "cleave needs level 2");
        w.Level = 2;
        Check(w.Learn("cleave") == null && w.Rank("cleave") == 1 && w.SkillPoints == 0, "and is learned at level 2 with a point");
        w.AttrPoints = 1;
        double life = w.S.MaxLife;
        Check(w.Raise("vigour") == null && Math.Abs(w.S.MaxLife - (life + 4 * 1.2)) < 1e-6, "a point of vigour is four of life (the wanderer's a fifth more)");
        // A two-handed bow puts the buckler away; the buckler back puts the bow away.
        w.Pack[0] = new Item("short-bow");
        Check(w.EquipFromPack(0) == null && w.Weapon.Base == "short-bow" && w.Equip[(int)Slot.Shield] == null, "a bow takes both hands");
        Check(w.Pack.Count(i => i != null) == 2 && w.Pack.Any(i => i?.Base == "buckler") && w.Pack.Any(i => i?.Base == "short-sword"), "the sword and the buckler go to the pack");
        int buckler = Array.FindIndex(w.Pack, i => i?.Base == "buckler");
        Check(w.EquipFromPack(buckler) == null && w.Equip[(int)Slot.Shield]?.Base == "buckler" && w.Weapon == null, "the buckler back on puts the bow away");
        Item plate = new("plate-armour");
        w.Pack[5] = plate;
        Check(w.EquipFromPack(5) == "needs level 15", "plate needs level 15");
        Check(w.Take(new Item("gold", 30)) && w.Gold == 70, "gold goes to the purse");
        Check(w.Take(new Item("pot-hp")) && w.Belt[0].Count == 2, "a draught stacks on the belt");
    }

    [Section]
    private void Fighting()
    {
        Game g = Bare(Cls.Wanderer, 1);
        Actor gnawer = Beside(g, "gnawer");
        g.OrderAttack(gnawer.Id);
        Run(g, 8);
        Check(!gnawer.Alive, "the wanderer kills a gnawer with the sword");
        Check(g.Hero.Xp > 0 && g.Hero.Kills == 1, "and gains experience");
        Check(g.Events.Any(e => e.Kind == "hit") && g.Events.Any(e => e.Kind == "die"), "blows and a death to show");

        g = Bare(Cls.Wanderer, 4);
        Actor hollow = Beside(g, "hollow");
        hollow.MaxLife = hollow.Life = 100000;
        Run(g, 10);
        Check(g.Me.Life < g.Me.MaxLife || g.Hero.Deaths > 0 || g.Events.Any(e => e.Kind is "block" or "miss"), "a hollow left alone fights back");

        g = Bare(Cls.Rogue, 1);
        Actor sentry = Beside(g, "sentry", 5, 0);
        g.OrderAttack(sentry.Id);
        Run(g, 15);
        Check(!sentry.Alive, "the rogue shoots a bone sentry down");

        // Dying sends the hero home, a little poorer.
        g = Bare(Cls.Sorcerer, 2);
        g.Hero.Gold = 100;
        Actor brute = Beside(g, "brute");
        brute.DmgX = 50; brute.Accuracy = 1e6;
        Run(g, 10);
        Check(g.Hero.Deaths == 1 && g.Depth == 0 && g.Me.Alive && g.Me.Life == g.Me.MaxLife && g.Hero.Gold == 90, "killed, revived in the village with 10% less gold");

        // The same seed and the same orders: the same fight.
        Game a = Bare(Cls.Wanderer, 2, 99), b = Bare(Cls.Wanderer, 2, 99);
        foreach (Game x in new[] { a, b }) { Actor m = Beside(x, "ghoul"); x.OrderAttack(m.Id); Run(x, 4); }
        Check(a.Me.Life == b.Me.Life && a.Here.Actors[0].Life == b.Here.Actors[0].Life && a.Hero.Xp == b.Hero.Xp, "a fight replays the same");
    }

    [Section]
    private void Skills()
    {
        Game g = Bare(Cls.Sorcerer, 1);
        Actor t = Beside(g, "hollow", 6, 0);
        g.OrderAttack(t.Id, "firebolt");
        Run(g, 1);
        Check(g.Events.Any(e => e.Kind == "cast") && t.Life < t.MaxLife, "a firebolt flies and burns");
        Check(g.Me.Mana < g.Me.MaxMana, "it costs mana");

        g = Bare(Cls.Sorcerer, 1);
        g.Hero.Level = 12; g.Hero.Skills["nova"] = 3; g.Hero.Skills["chain"] = 3; g.Hero.Skills["meteor"] = 1; g.Hero.Skills["shield"] = 1; g.Hero.Changed();
        g.Me.Mana = g.Me.MaxMana = 500;
        Actor a1 = Beside(g, "hollow", 2, 0), a2 = Beside(g, "hollow", -2, 0);
        foreach (Actor x in new[] { a1, a2 }) x.MaxLife = x.Life = 1000;
        g.OrderSkill("nova", g.Me.X, g.Me.Y);
        Run(g, 1);
        Check(a1.Life < a1.MaxLife && a2.Life < a2.MaxLife && a1.Slow > 0, "a frost nova hurts and slows all around");
        foreach (Actor x in new[] { a1, a2 }) x.Life = x.MaxLife;
        g.OrderAttack(a1.Id, "chain");
        Run(g, 1);
        Check(a1.Life < a1.MaxLife && a2.Life < a2.MaxLife, "chain lightning leaps to the second hollow");
        g.Me.Mana = 500;
        Actor far = Beside(g, "golem", 0, 5);
        far.MaxLife = far.Life = 100000;
        g.OrderSkill("meteor", far.X, far.Y);
        Run(g, 2);
        Check(far.Life < far.MaxLife && g.Events.Any(e => e.Kind == "blast"), "a meteor falls where it was called");
        g.OrderSkill("shield", g.Me.X, g.Me.Y);
        Run(g, 1);
        Check(g.Me.Shield > 0 && g.Me.ShieldT > 50, "an arcane shield");

        g = Bare(Cls.Rogue, 1);
        g.Hero.Level = 12; g.Hero.Skills["multi"] = 3; g.Hero.Skills["step"] = 1; g.Hero.Skills["rain"] = 1; g.Hero.Changed();
        g.Me.Mana = g.Me.MaxMana = 500;
        g.OrderSkill("multi", g.Me.X + 5, g.Me.Y);
        Run(g, 0.5);
        Check(g.Events.Count(e => e.Kind == "shoot") == 1, "a multishot is one shot");
        double x0 = g.Me.X, y0 = g.Me.Y;
        (double sx, double sy) = g.Reach(x0, y0, x0 + 4, y0, 6.3);
        g.OrderSkill("step", x0 + 4, y0);
        Run(g, 0.6);
        Check(Game.Dist(g.Me.X, g.Me.Y, sx, sy) < 0.6 && g.Me.Hidden > 0, "a shadow step moves the rogue and hides her");

        g = Bare(Cls.Wanderer, 1);
        g.Hero.Level = 12; g.Hero.Skills["leap"] = 1; g.Hero.Skills["warcry"] = 1; g.Hero.Changed();
        g.Me.Mana = g.Me.MaxMana = 500;
        (double lx, double ly) = g.Reach(g.Me.X, g.Me.Y, g.Me.X, g.Me.Y + 5, 7);
        g.OrderSkill("leap", g.Me.X, g.Me.Y + 5);
        Run(g, 1);
        Check(Game.Dist(g.Me.X, g.Me.Y, lx, ly) < 0.6, "a leap lands where it was aimed (or as far as it can)");
        Actor s1 = Beside(g, "hollow", 1.5, 0);
        g.OrderSkill("warcry", g.Me.X, g.Me.Y);
        Run(g, 0.7);
        Check(s1.Stun > 0, "a war cry stuns");
    }

    [Section]
    private void Growing()
    {
        Game g = Bare(Cls.Rogue, 1);
        g.GainXp(Data.XpToNext(1) + Data.XpToNext(2));
        Check(g.Hero.Level == 3 && g.Hero.AttrPoints == 10 && g.Hero.SkillPoints == 2, "two levels: ten attribute points, two skill points");
        Check(g.Me.Life == g.Hero.S.MaxLife, "a new level heals");
        Check(Data.XpToNext(10) > Data.XpToNext(9) * 1.1, "each level takes longer");
    }

    [Section]
    private void Village()
    {
        Game g = Game.New(4, Cls.Wanderer);
        Eq(g.Depth, 0, "a new game starts in the village");
        Check(g.Here.Actors.Count(a => a.Npc) == 3, "the elder, the healer and the smith");
        string first = g.TalkElder();
        Check(g.Hero.Quests["hag"] == 1 && first.Contains("hag"), "the elder sends the hero after the hag");
        g.Hero.Quests["hag"] = 2;
        int pts = g.Hero.SkillPoints;
        g.TalkElder();
        Check(g.Hero.Quests["hag"] == 3 && g.Hero.SkillPoints == pts + 1, "and rewards her death");
        g.Hero.Gold = 100;
        Check(g.Buy("healer", 0) == null && g.Hero.Gold == 75, "a draught from the healer for 25 gold");
        int free = Array.IndexOf(g.Hero.Pack, null);
        g.Hero.Pack[free] = Loot.Make("long-sword", Rarity.Magic, 5, new Rng(1));
        int worth = g.Hero.Pack[free].Worth;
        Check(g.Sell(free) == null && g.Hero.Gold == 75 + worth && worth > 10, "the smith buys a magic sword");
        Check(g.SmithWares.Count >= 12, "and sells it on");
        g.Hero.Pack[0] = new Item("hatchet");
        Check(g.ToStash(0) == null && g.Hero.Stash.Count == 1 && g.FromStash(0) == null && g.Hero.Stash.Count == 0, "the stash keeps things");
        for (int k = 0; k < Game.StashSize + 2; k++) { g.Hero.Pack[0] = new Item("hatchet"); g.ToStash(0); }
        Check(g.Hero.Stash.Count == Game.StashSize && g.Hero.Pack[0] != null, $"up to {Game.StashSize} things, all of them shown");
        g.Hero.Stash.Clear();
        for (int k = 0; k < 40; k++) { g.Hero.Pack[1] = Loot.Make("hatchet", Rarity.Magic, 3, new Rng((ulong)k)); g.Sell(1); }
        Check(g.SmithWares.Count <= 24, $"the smith keeps no more than two dozen wares ({g.SmithWares.Count})");

        // Down, the scroll home, and back through its way.
        g.Use(g.Map.Find("down"));
        Eq(g.Depth, 1, "the way down leads to the first floor");
        g.Use(g.Map.Find("waystone"));
        Check(g.Hero.Waystones.Contains(1), "touching its waystone wakes it");
        g.Hero.Belt[3] = new Item("scroll");
        g.Me.X += 0; double px = g.Me.X, py = g.Me.Y;
        Check(g.UseBelt(3) == null && g.Depth == 0 && g.PortalDepth == 1, "a scroll goes home and leaves a way back");
        g.Use(null);
        Check(g.Depth == 1 && Game.Dist(g.Me.X, g.Me.Y, px, py) < 0.01 && g.PortalDepth == 0, "the way back returns to the same place");
        g.Enter(0, null);
        Check(g.Travel(1) == null && g.Depth == 1, "the village waystone travels to the first floor");
        Check(g.Travel(5) != null, "but not to a floor never reached");
        g.Use(g.Map.Find("up"));
        Eq(g.Depth, 0, "up from the first floor is the village");
    }

    [Section]
    private void Things()
    {
        // A barrel: the hero walks up, swings, it breaks; its cell opens; it stays broken when the floor is made again.
        Game g = Bare(Cls.Wanderer, 2);
        Prop barrel = g.Map.Props.First(p => p.Kind == "barrel");
        Check(barrel.Usable && barrel.Breaks && !g.Map.Walk(barrel.X, barrel.Y), "a barrel is usable and stands in the way");
        g.Me.X = barrel.X + 0.5; g.Me.Y = barrel.Y + 2.5;
        if (!g.Map.Walk(g.Me.X, g.Me.Y)) (g.Me.X, g.Me.Y) = (g.Map.Start.x + 0.5, g.Map.Start.y + 0.5);
        g.OrderUse(barrel.Id);
        Run(g, 12);
        Check(g.Here.Opened.Contains(barrel.Id) && g.Events.Any(e => e.Kind == "break") && g.Events.Any(e => e.Kind == "swing"), "the hero swings and the barrel breaks");
        Check(g.Map.Walk(barrel.X, barrel.Y), "its cell is open now");
        g.Enter(0, null); g.Enter(2, null);
        Check(g.Map.Walk(barrel.X, barrel.Y) && g.Map.Props.First(p => p.Id == barrel.Id).Kind == "barrel", "and stays broken when the floor is made again");
        // Told to walk onto something in the way, the hero stops beside it (not walking on the spot for ever).
        Prop block = g.Map.Props.First(p => p.Blocks && p.Kind == "deco");
        g.Me.X = g.Map.Start.x + 0.5; g.Me.Y = g.Map.Start.y + 0.5;
        g.OrderMove(block.X + 0.5, block.Y + 0.5);
        Run(g, 20);
        Check(g.Order.Kind == "none" && g.Me.Act == "idle" && Game.Dist(g.Me.X, g.Me.Y, block.X + 0.5, block.Y + 0.5) < 2.2, $"walking onto a {block.Model} ends beside it ({g.Order.Kind}, {g.Me.Act})");
        // A chest opens once; a tomb is only to look at.
        Prop chest = g.Map.Props.First(p => p.Kind == "chest");
        g.Use(chest);
        int drops = g.Here.Drops.Count;
        g.Use(chest);
        Check(g.Here.Opened.Contains(chest.Id) && g.Here.Drops.Count == drops, "a chest opens once");
        Check(Floor.Make(3, 2).Props.Any(p => p.Kind is "urn" or "barrel") && Floor.Make(3, 4).Props.Any(p => p.Model is "bone-pile" or "candelabra" or "gibbet-cage"), "floors have things to break and things to look at");
        Check(Floor.Make(3, 1).Props.Where(p => p.Model is "tomb" or "pillar" or "gravestone" or "bone-pile").All(p => !p.Usable), "tombs, pillars, gravestones and bones are only to look at");
    }

    [Section]
    private void ShrinesAndChampions()
    {
        // A shrine blesses once, for a while.
        Game g = null;
        Prop shrine = null;
        for (ulong seed = 1; seed < 30 && shrine == null; seed++)
        {
            g = Game.New(seed, Cls.Wanderer);
            g.Enter(2, null);
            shrine = g.Map.Props.FirstOrDefault(p => p.Kind == "shrine");
        }
        Check(shrine != null && shrine.Usable, "floors have shrines");
        g.Me.Life = 1;
        g.Use(shrine);
        Check(g.Here.Opened.Contains(shrine.Id) && (g.Hero.BlessingLeft > 0 || g.Me.Life == g.Me.MaxLife), "a shrine blesses (or mends)");
        string had = g.Hero.Blessing;
        g.Use(shrine);
        Check(g.Hero.Blessing == had, "once");
        g.Hero.Blessing = "iron"; g.Hero.BlessingLeft = 1; g.Hero.Changed();
        double armour = g.Hero.S.Armour;
        g.Here.Actors.Clear();
        for (int k = 0; k < 40; k++) g.Step(0.05);
        Check(g.Hero.BlessingLeft <= 0 && g.Hero.S.Armour < armour, "and the blessing fades");
        // Champions carry traits that change the fight.
        Actor c = g.MakeMonster("hollow", g.Me.X + 1, g.Me.Y, 3, true, null, 7);
        Actor c2 = g.MakeMonster("hollow", g.Me.X + 2, g.Me.Y, 3, true, null, 7);
        Check(c.Traits.Count is 1 or 2 && c.Traits.SequenceEqual(c2.Traits), $"a champion pack shares its traits ({string.Join(",", c.Traits)})");
        var stone = g.MakeMonster("hollow", 0, 0, 3);
        stone.Traits.Add("stone"); stone.MaxLife = stone.Life = 1000;
        var plain = g.MakeMonster("hollow", 0, 0, 3);
        plain.MaxLife = plain.Life = 1000;
        g.Here.Actors.Add(stone); g.Here.Actors.Add(plain);
        var hurt = typeof(Game).GetMethod("HurtFoe", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        hurt.Invoke(g, new object[] { stone, 100.0, Elem.Phys, false, 0.0 });
        hurt.Invoke(g, new object[] { plain, 100.0, Elem.Phys, false, 0.0 });
        Check(stone.Life > plain.Life + 30, "a stone-skinned one shrugs off a blow");
    }

    [Section]
    private void UniquesAndRespec()
    {
        Check(Data.UniqueItems.All(u => Data.BaseOf(u.baseId)?.Wearable == true && u.level >= Data.BaseOf(u.baseId).Level), "every unique has a base it can be");
        Item u0 = Loot.Unique(0);
        Check(u0.Rarity == Rarity.Unique && u0.Name == "Graveward" && u0.Mods.Count == 4 && u0.Requires == 3, "a unique: its name, its affixes, its level");
        var r = new Rng(5);
        int uniques = 0;
        for (int k = 0; k < 4000; k++) uniques += Loot.Drop(20, 10, 0, 0, r).Count(i => i.Rarity == Rarity.Unique);
        Check(uniques > 10, $"bosses drop uniques now and then ({uniques} in 4000)");
        int fromPlain = 0;
        for (int k = 0; k < 4000; k++) fromPlain += Loot.Drop(20, 1, 0, 0, r).Count(i => i.Rarity == Rarity.Unique);
        Check(fromPlain < uniques, $"and plain monsters far less ({fromPlain})");
        // The elder unlearns every point: free once, then for gold.
        Game g = Game.New(3, Cls.Rogue, true, "Wenna");
        Check(g.Hero.Name == "Wenna" && g.Hero.Female, "a named hero");
        g.GainXp(Data.XpToNext(1) + Data.XpToNext(2) + Data.XpToNext(3));
        g.Hero.Raise("agility"); g.Hero.Raise("agility"); g.Hero.Learn("multi");
        int attrs = g.Hero.AttrPoints, skills = g.Hero.SkillPoints;
        Check(g.Hero.Respec() == null && g.Hero.AttrPoints == attrs + 2 && g.Hero.SkillPoints == skills + 1 && g.Hero.Rank("multi") == 0 && g.Hero.Rank("power") == 1, "unlearning gives every point back");
        g.Hero.Gold = 0;
        Check(g.Hero.Respec() != null, "the second time costs gold");
    }

    [Section]
    private void Lairs()
    {
        foreach (int k in Enumerable.Range(0, Data.Lairs.Length))
        {
            Data.LairDef lair = Data.Lairs[k];
            Floor f = Floor.Make(1234 + (ulong)k, Data.LairBase + k);
            int[] d = f.Distances(f.Start);
            int lost = 0;
            for (int c = 0; c < f.W * f.H; c++) if (f.Cells[c] == 1 && !f.Blocked[c] && d[c] < 0) lost++;
            Check(lost == 0 && f.Find("exit") != null, $"{lair.Name}: every cell can be reached, and the way out is there");
            Check(f.Spawns.Any(s => s.Unique == lair.BossTitle) && f.Props.Count(p => p.Model == "chest-ornate") >= 2, $"{lair.Name}: {lair.BossTitle} and the sealed chests");
            Check(f.Spawns.Count(s => s.Champion) >= 4, $"{lair.Name}: champions");
        }
        // A map from a named foe; read, it opens a portal; through it, the lair at the map's level; out, back.
        var r = new Rng(9);
        int maps = 0;
        for (int k = 0; k < 400; k++) maps += Loot.Drop(10, 7, 0, 0, r).Count(i => i.Base == "treasure-map");
        Check(maps > 100, $"named foes leave treasure maps ({maps} in 400)");
        Game g = Bare(Cls.Sorcerer, 3);
        g.Hero.Level = 12; g.Hero.Changed();
        Item map = Loot.Map(14, new Rng(3), 3);
        int slot = Array.IndexOf(g.Hero.Pack, null);
        g.Hero.Pack[slot] = map;
        double ox = g.Me.X, oy = g.Me.Y;
        Check(g.UsePack(slot) == null && g.Lair != null && g.Hero.Pack[slot] == null, "a map, read, opens a portal (and is gone)");
        g.OrderUse(-3);
        Run(g, 4);
        Check(g.Depth == Data.LairBase + 3 && g.Map.Def.Name == "The Hollow King's Crypt", "through the portal: the lair");
        Actor boss = g.Foes.First(a => a.Title == "Saelith the Hollow King");
        Check(boss.Boss && boss.Scale >= 1 && boss.Level >= 14 && boss.MaxLife > 2000, $"its boss, at the map's level ({boss.Level}, {boss.MaxLife})");
        Check(g.Foes.Where(a => !a.Boss).All(a => a.Level >= 14), "and everything in it");
        g.Use(g.Map.Find("exit"));
        Check(g.Depth == 3 && g.Lair == null && Game.Dist(g.Me.X, g.Me.Y, ox, oy) < 3 && !g.Floors.ContainsKey(Data.LairBase + 3), "the way out leads back, and the lair closes");
        // Dying in a lair loses it.
        g.Hero.Pack[slot] = Loot.Map(14, new Rng(4), 0);
        g.UsePack(slot);
        g.EnterLair();
        g.Me.Life = 0.1;
        Actor q = g.Foes.First();
        q.X = g.Me.X + 0.8; q.Y = g.Me.Y; q.DmgX = 100; q.Accuracy = 1e6; q.Awake = true;
        Run(g, 10);
        Check(g.Depth == 0 && g.Lair == null, "dying in a lair loses it");
    }

    [Section]
    private void Gems()
    {
        Check(Data.Gems.All(d => Data.BaseOf("gem-" + d.Id)?.Kind == Kind.Gem && Godot.FileAccess.FileExists($"res://assets/icons/gem-{d.Id}.png")), "seven gems, each with its icon");
        var r = new Rng(4);
        int socketed = 0, gems = 0;
        for (int k = 0; k < 2000; k++) if (Loot.Make("long-sword", Rarity.Plain, 10, r).Sockets > 0) socketed++;
        for (int k = 0; k < 3000; k++) gems += Loot.Drop(12, 1, 0, 0, r).Count(i => i.B.Kind == Kind.Gem);
        Check(socketed > 400 && socketed < 1000, $"a plain sword has sockets about a third of the time ({socketed} in 2000)");
        Check(gems > 50, $"monsters drop gems ({gems} in 3000)");
        Check(Loot.Make("ring", Rarity.Magic, 10, r).Sockets == 0, "rings never have sockets");
        // A ruby in a sword adds fire; in armour, life; in a shield, fire resistance.
        Game g = Game.New(2, Cls.Wanderer);
        Hero h = g.Hero;
        h.Equip[(int)Slot.Weapon].Sockets = 2;
        h.Equip[(int)Slot.Body].Sockets = 1;
        h.Equip[(int)Slot.Shield].Sockets = 1;
        h.Changed();
        double fire = h.S.FireMax, life = h.S.MaxLife;
        int res = h.S.FireRes;
        h.Pack[0] = Item.Gem("ruby", 2); h.Pack[1] = Item.Gem("ruby", 1); h.Pack[2] = Item.Gem("ruby", 0);
        Check(g.Socket(0, -1, Slot.Weapon) == null && g.Socket(1, -1, Slot.Body) == null && g.Socket(2, -1, Slot.Shield) == null, "gems go into sockets");
        Check(h.S.FireMax == fire + 16 && Math.Abs(h.S.MaxLife - (life + 20 * 1.2)) < 1e-6 && h.S.FireRes == res + 12, "and give by where they are");
        Check(h.Pack[0] == null && h.Equip[(int)Slot.Body].Gems.Count == 1, "the gem leaves the pack");
        h.Pack[3] = Item.Gem("topaz", 0);
        Check(g.Socket(3, -1, Slot.Body) == "its sockets are full", "a full item takes no more");
        Check(g.Socket(3, -1, Slot.Hands) != null, "nor does one without sockets");
        // Oskar joins three of a kind and grade.
        for (int k = 4; k < 10; k++) h.Pack[k] = Item.Gem("emerald", 0);
        h.Gold = 1000;
        Check(g.CombineGems() == 2 && h.Pack.Count(i => i?.Base == "gem-emerald" && i.Grade == 1) == 2 && h.Gold == 800, "six chipped emeralds, joined: two emeralds for 200 gold");
        Game back = Saves.Read<Game>(Saves.Write(g));
        Check(back.Hero.Equip[(int)Slot.Weapon].Gems.Count == 1 && back.Hero.S.FireMax == h.S.FireMax, "socketed gems survive a save");
    }

    [Section]
    private void Comforts()
    {
        Game g = Bare(Cls.Rogue, 1);
        g.Place(new Item("gold", 25), g.Me.X + 2, g.Me.Y);
        Drop gold = g.Here.Drops[0];
        int had = g.Hero.Gold;
        g.OrderMove(gold.X, gold.Y);
        Run(g, 3);
        Check(g.Hero.Gold == had + 25 && g.Here.Drops.Count == 0, "gold is picked up by walking over it");
        g.Hero.Belt[0] = new Item("pot-hp");
        g.Hero.Pack[5] = new Item("pot-hp", 3);
        g.Me.Life = 1;
        g.UseBelt(0);
        Check(g.Hero.Belt[0]?.Count == 3 && g.Hero.Pack[5] == null, "an emptied belt place refills from the pack");
    }

    [Section]
    private void Restocking()
    {
        Game g = Game.New(8, Cls.Wanderer);
        g.Enter(1, null);
        int full = g.Foes.Count(a => a.Alive);
        foreach (Actor a in g.Foes) { a.Life = 0; a.Act = "dead"; }
        Prop chest = g.Map.Props.FirstOrDefault(p => p.Kind == "chest");
        if (chest != null) g.Use(chest);
        g.Enter(0, null);
        g.Step(30);
        g.Enter(1, null);
        Check(!g.Foes.Any(a => a.Alive), "back soon after: the floor is as it was left");
        g.Enter(0, null);
        g.Time += Game.RestockAfter;
        g.Enter(1, null);
        Check(g.Foes.Count(a => a.Alive) == full && (chest == null || !g.Here.Opened.Contains(chest.Id)), $"back after ten minutes: monsters and chests again ({full})");
        Check(g.Here.Seen.Any(b => b == 1), "and the map of it is kept");
        foreach (Actor a in g.Foes) { a.Life = 0; a.Act = "dead"; }
        g.Enter(2, null);
        Game back = Saves.Read<Game>(Saves.Write(g));
        back.Restock();
        Check(back.Depth == 2 && back.Foes.Any(a => a.Alive), "a loaded game keeps the floor the hero stands on");
        back.Enter(1, null);
        Check(back.Foes.Count(a => a.Alive) == full, "and fills the others up again");
    }

    [Section]
    private void Saving()
    {
        Game g = Game.New(5, Cls.Sorcerer);
        g.Enter(2, null);
        g.Hero.Pack[3] = Loot.Make("rune-staff", Rarity.Rare, 12, new Rng(2));
        g.GainXp(500);
        Actor m = g.Foes.First();
        m.Life = 1;
        Run(g, 1);
        Game back = Saves.Read<Game>(Saves.Write(g));
        Check(back.Seed == g.Seed && back.Depth == 2 && back.Hero.Level == g.Hero.Level && back.Hero.Class == Cls.Sorcerer, "a game survives a save");
        Check(back.Hero.Pack[3]?.Name == g.Hero.Pack[3].Name && back.Hero.Pack[3].Mods.Count == g.Hero.Pack[3].Mods.Count, "with its items");
        Check(back.Here.Actors.Count == g.Here.Actors.Count && back.Here.Actors.First(a => a.Id == m.Id).Life == 1, "and its monsters as they were");
        Check(back.Map.Cells.SequenceEqual(g.Map.Cells), "the floor is made again the same");
        Check(back.Me.X == g.Me.X && back.Hero.S.MaxLife == g.Hero.S.MaxLife, "the hero where they stood");
        Check(Profile.Scratch && Profile.SlotPath(1).Contains("scratch"), "the self-check never touches the player's slots");
        Check(Profile.SaveGame(g, 2) && Profile.LoadGame(2)?.Seed == g.Seed, "a slot is written and read back");
        Profile.DeleteSlot(2);
        Check(Profile.LoadGame(2) == null, "and cleared");
    }

    [Section]
    private void SoundsAndLook()
    {
        foreach (var (name, def) in Sounds.All) Check(Effects.Render(def).Any(s => Math.Abs(s) > 0.05f), $"the '{name}' sound is made");
        Check(UITheme.Theme != null && UITheme.Body != null, "the theme and its fonts load (Godot's own when assets/fonts is empty)");
    }
}
