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
        Check(w.Raise("vigour") == null && w.S.MaxLife == life + 4, "a point of vigour is four of life");
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
