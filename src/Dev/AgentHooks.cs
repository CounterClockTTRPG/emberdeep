using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using GodotAgent;

namespace Emberdeep;

/// <summary>
/// The game's side of godot-agent: what 'state' says (the hero, what is near, the floor around in ASCII), how cells map
/// to the screen ('at', 'drag', 'locate cell X Y'), and the game's own commands. Anything an agent playtester needs
/// that a player cannot do quickly gets a command here.
/// </summary>
public static class AgentHooks
{
    private static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    public static AgentServer Make(Root root)
    {
        var agent = new AgentServer { Name = "Agent" };
        Game G() => root.Game ?? throw new Exception("no game (try: new)");
        GameScreen S() => root.Current as GameScreen ?? throw new Exception("not on the game screen");
        agent.State = () => root.Game == null ? $"screen: {root.Current?.GetType().Name ?? "none"}" : State(root.Game, root.Current as GameScreen);
        agent.CellToScreen = (x, y) => S().ScreenOf(x + 0.5, y + 0.5);
        agent.Locate = target =>
        {
            string[] w = target.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (root.Current is not GameScreen gs) return null;
            if (w.Length == 3 && w[0] == "cell" && int.TryParse(w[1], out int x) && int.TryParse(w[2], out int y))
            {
                Vector2 a = gs.ScreenOf(x, y), b = gs.ScreenOf(x + 1, y + 1);
                return new Rect2(a.Min(b), (a - b).Abs());
            }
            if (w.Length == 2 && w[0] is "actor" or "monster" && int.TryParse(w[1], out int id) && root.Game.Find(id) is Actor act)
            {
                float h = act.Hero ? 1.8f : (float)(act.M?.Height ?? 1.7);
                Vector2 foot = gs.ScreenOf(act.X, act.Y), head = gs.ScreenOf(act.X, act.Y, h);
                float half = Math.Max(16, (foot.Y - head.Y) * 0.3f);
                return new Rect2(head.X - half, head.Y, half * 2, foot.Y - head.Y);
            }
            return null;
        };
        agent.OnQuit = () => { if (root.Game != null) Profile.SaveGame(root.Game); };

        agent.Command("new", "new [wanderer|rogue|sorcerer] [SEED] [man|woman]   a new hero in the agent's slot", async a =>
        {
            Cls c = a.Length > 1 && Enum.TryParse(a[1], true, out Cls cc) ? cc : Cls.Wanderer;
            ulong seed = a.Length > 2 ? (ulong)AgentServer.Int(a[2]) : 42;
            root.NewGame(1, seed, c, a.Length > 3 && a[3] == "woman");
            await agent.Frames(3);
            return $"new {c}, seed {root.Game.Seed}";
        });
        agent.Command("go", "go X Y                 walk to a place (cells; fractions allowed)", async a =>
        {
            G().OrderMove(Num(a[1]), Num(a[2]));
            await agent.Frames(1);
            return $"walking to {a[1]},{a[2]}";
        });
        agent.Command("attack", "attack ID [SKILL]      fight a monster until it falls (with the weapon, or a skill)", async a =>
        {
            Game g = G();
            Actor t = g.Find(AgentServer.Int(a[1])) ?? throw new Exception("no such monster");
            g.OrderAttack(t.Id, a.Length > 2 ? a[2] : "attack");
            await agent.Frames(1);
            return $"attacking {t.Name} ({t.Id})";
        });
        agent.Command("skill", "skill NAME [X Y | ID]  use a skill at a place, a monster, or (no target) where you stand", async a =>
        {
            Game g = G();
            string s = a[1];
            if (s != "attack" && Data.Skill(s) == null) throw new Exception("no such skill: " + string.Join(", ", Data.SkillsOf(g.Hero.Class).Select(k => k.Id)));
            if (a.Length == 3 && g.Find(AgentServer.Int(a[2])) is Actor t) g.OrderAttack(t.Id, s);
            else if (a.Length >= 4) g.OrderSkill(s, Num(a[2]), Num(a[3]));
            else g.OrderSkill(s, g.Me.X, g.Me.Y);
            await agent.Frames(1);
            return $"{s}: ordered";
        });
        agent.Command("pick", "pick [ID|all]          pick up a drop (all: walk to each in turn is not done; picks those within 2 m)", async a =>
        {
            Game g = G();
            if (a.Length > 1 && a[1] == "all")
            {
                int n = 0;
                foreach (Drop d in g.Here.Drops.Where(d => Game.Dist(d.X, d.Y, g.Me.X, g.Me.Y) < 2).ToList()) if (g.Pick(d)) n++;
                await agent.Frames(1);
                return $"picked up {n}";
            }
            Drop drop = a.Length > 1 ? g.Here.Drops.FirstOrDefault(d => d.Id == AgentServer.Int(a[1])) : g.Here.Drops.OrderBy(d => Game.Dist(d.X, d.Y, g.Me.X, g.Me.Y)).FirstOrDefault();
            if (drop == null) throw new Exception("no such drop");
            g.OrderPick(drop.Id);
            await agent.Frames(1);
            return $"going for {drop.Item.Title}";
        });
        agent.Command("use", "use ID|down|up|exit|waystone|chest|crate|barrel|urn|shrine|stash|well|portal|lair   walk to a thing and use it", async a =>
        {
            Game g = G();
            int id;
            if (a[1] == "portal") id = -2;
            else if (a[1] == "lair") id = -3;
            else if (int.TryParse(a[1], out int n)) id = n;
            else id = g.Map.Props.Where(p => p.Kind == a[1] && !(p.OneUse && g.Here.Opened.Contains(p.Id)))
                .OrderBy(p => Game.Dist(p.X, p.Y, g.Me.X, g.Me.Y)).FirstOrDefault()?.Id ?? throw new Exception($"no {a[1]} here");
            g.OrderUse(id);
            await agent.Frames(1);
            return $"going to use {a[1]}";
        });
        agent.Command("talk", "talk elder|healer|smith   walk to a village folk and talk", async a =>
        {
            Game g = G();
            Actor n = g.Here.Actors.FirstOrDefault(x => x.Npc && x.Def == a[1]) ?? throw new Exception("no one by that name here");
            g.OrderTalk(n.Id);
            await agent.Frames(1);
            return $"going to {a[1]}";
        });
        agent.Command("trade", "trade smith|healer|stash   open the trade (or the stash) at once (debug: from anywhere)", async a =>
        {
            GameScreen s = S();
            s.CloseAll();
            s.Trading = a[1];
            s.Open("left", a[1] == "stash" ? "stash" : "shop");
            s.Trading = a[1];
            s.Open("right", "inventory");
            await agent.Frames(2);
            return Wares(G(), a[1]);
        });
        agent.Command("buy", "buy smith|healer N    buy ware N (see trade)", async a =>
        {
            string why = G().Buy(a[1], AgentServer.Int(a[2]));
            S().Refresh();
            await agent.Frames(1);
            return why ?? $"bought; {G().Hero.Gold} gold left";
        });
        agent.Command("sell", "sell N                 sell pack place N", async a =>
        {
            string why = G().Sell(AgentServer.Int(a[1]));
            S().Refresh();
            await agent.Frames(1);
            return why ?? $"sold; {G().Hero.Gold} gold";
        });
        agent.Command("belt", "belt N                 drink or read belt place N (1-4)", async a =>
        {
            string why = G().UseBelt(AgentServer.Int(a[1]) - 1);
            await agent.Frames(1);
            return why ?? "done";
        });
        agent.Command("equip", "equip N                wear (or drink) pack place N", async a =>
        {
            string why = G().UsePack(AgentServer.Int(a[1]));
            S().Refresh();
            await agent.Frames(1);
            return why ?? "done";
        });
        agent.Command("unequip", "unequip SLOT           take off weapon, shield, body, head, hands, feet, amulet, ring1, ring2", async a =>
        {
            string why = Enum.TryParse(a[1], true, out Slot sl) ? G().Hero.Unequip(sl) : "no such slot";
            S().Refresh();
            await agent.Frames(1);
            return why ?? "done";
        });
        agent.Command("raise", "raise ATTR [N]         spend attribute points (might, agility, vigour, spirit)", async a =>
        {
            int n = a.Length > 2 ? AgentServer.Int(a[2]) : 1;
            string why = null;
            for (int k = 0; k < n && why == null; k++) why = G().Hero.Raise(a[1]);
            S().Refresh();
            await agent.Frames(1);
            return why ?? $"{a[1]} raised";
        });
        agent.Command("learn", "learn SKILL [N]        spend skill points", async a =>
        {
            int n = a.Length > 2 ? AgentServer.Int(a[2]) : 1;
            string why = null;
            for (int k = 0; k < n && why == null; k++) why = G().Hero.Learn(a[1]);
            S().Refresh();
            await agent.Frames(1);
            return why ?? $"{a[1]} is rank {G().Hero.BaseRank(a[1])}";
        });
        agent.Command("right", "right SKILL            put a skill on the right mouse button", async a =>
        {
            if (G().Hero.Rank(a[1]) <= 0) throw new Exception("not learned");
            G().Hero.Right = a[1];
            await agent.Frames(1);
            return $"right button: {a[1]}";
        });
        agent.Command("travel", "travel DEPTH           by waystone (from anywhere: debug) to a woken floor", async a =>
        {
            string why = G().Travel(AgentServer.Int(a[1]));
            await agent.Frames(3);
            return why ?? $"at {G().Map.Def.Name}";
        });
        agent.Command("descend", "descend [DEPTH]        (debug) straight to a floor", async a =>
        {
            Game g = G();
            g.Enter(a.Length > 1 ? AgentServer.Int(a[1]) : g.Depth + 1, null);
            await agent.Frames(3);
            return $"at {g.Map.Def.Name}";
        });
        agent.Command("panel", "panel inventory|character|skills|none   open a panel", async a =>
        {
            GameScreen s = S();
            if (a[1] == "none") s.CloseAll();
            else s.Toggle(a[1] == "inventory" ? "right" : "left", a[1]);
            await agent.Frames(2);
            return "ok";
        });
        agent.Command("inv", "inv                    what the hero wears, carries and has in the belt", async _ => Inventory(G()));
        agent.Command("skills", "skills                 the hero's skills and points", async _ =>
        {
            Hero h = G().Hero;
            var sb = new StringBuilder($"{h.SkillPoints} skill points, {h.AttrPoints} attribute points; left {h.Left}, right {h.Right}\n");
            foreach (SkillDef s in Data.SkillsOf(h.Class))
                sb.Append($"  {s.Id,-11} rank {h.Rank(s.Id)}  lvl {s.Level,2}  {(s.Passive ? "passive" : $"mana {Data.ManaCost(s, Math.Max(1, h.BaseRank(s.Id)))}")}  {Data.SkillNumbers(s.Id, Math.Max(1, h.Rank(s.Id)))}\n");
            return sb.ToString().TrimEnd();
        });
        agent.Command("give", "give BASE [plain|magic|rare] [LEVEL]   (debug) an item into the pack", async a =>
        {
            Game g = G();
            Rarity r = a.Length > 2 && Enum.TryParse(a[2], true, out Rarity rr) ? rr : Rarity.Plain;
            int lvl = a.Length > 3 ? AgentServer.Int(a[3]) : g.Hero.Level;
            Item it = a[1] == "treasure-map" ? Loot.Map(lvl + 2, g.R()) : Data.BaseOf(a[1])?.Wearable == true ? Loot.Make(a[1], r, lvl, g.R()) : new Item(a[1]);
            if (it.B == null) throw new Exception("no such base");
            if (!g.Hero.Take(it)) throw new Exception("no room");
            S().Refresh();
            await agent.Frames(1);
            return $"given {it.Title}";
        });
        agent.Command("xp", "xp N                   (debug) gain experience", async a =>
        {
            G().GainXp(AgentServer.Int(a[1]));
            await agent.Frames(1);
            return $"level {G().Hero.Level}";
        });
        agent.Command("heal", "heal                   (debug) full life and mana", async _ =>
        {
            G().Me.Life = G().Me.MaxLife;
            G().Me.Mana = G().Me.MaxMana;
            await agent.Frames(1);
            return "healed";
        });
        agent.Command("teleport", "teleport X Y           (debug) put the hero on a place", async a =>
        {
            G().Me.X = Num(a[1]); G().Me.Y = Num(a[2]);
            G().Halt();
            await agent.Frames(1);
            return $"at {a[1]},{a[2]}";
        });
        return agent;
    }

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static string Wares(Game g, string from)
    {
        if (from == "stash") return "stash: " + string.Join(", ", g.Hero.Stash.Select((it, i) => $"{i}:{it.Title}"));
        var list = from == "healer" ? Loot.HealerStock() : g.SmithWares;
        return string.Join("\n", list.Select((it, i) => $"  {i}: {it.Title} ({it.Rarity}) {Game.Price(it)} gold{(it.Mods.Count > 0 ? " -- " + string.Join(", ", it.Lines().Skip(1)) : "")}"));
    }

    public static string Inventory(Game g)
    {
        Hero h = g.Hero;
        var sb = new StringBuilder();
        foreach (Slot s in Enum.GetValues<Slot>())
            if (h.Equip[(int)s] is Item it) sb.Append($"{s}: {it.Title} [{it.Rarity}] {string.Join("; ", it.Lines())}\n");
        sb.Append("belt: " + string.Join(" | ", h.Belt.Select((it, i) => $"{i + 1}:{it?.Title ?? "-"}")) + "\n");
        for (int k = 0; k < h.Pack.Length; k++)
            if (h.Pack[k] is Item it) sb.Append($"pack {k}: {it.Title} [{it.Rarity}] {string.Join("; ", it.Lines())}\n");
        sb.Append($"{h.Gold} gold, {h.Free()} free places");
        return sb.ToString();
    }

    /// <summary>The hero, what is near, and the floor around as ASCII.</summary>
    public static string State(Game g, GameScreen gs)
    {
        Hero h = g.Hero;
        Actor me = g.Me;
        Stats st = h.S;
        var sb = new StringBuilder();
        sb.Append(FormattableString.Invariant(
            $"{g.Map.Def.Name} (depth {g.Depth}); time {g.Time:0.0}s; {(g.Won ? "WON; " : "")}panels [{gs?.LeftPanel}|{gs?.RightPanel}|{gs?.CentrePanel}]\n"));
        sb.Append(FormattableString.Invariant(
            $"hero: {h.Class} level {h.Level} xp {h.Xp}/{Data.XpToNext(h.Level)} at {me.X:0.0},{me.Y:0.0} life {me.Life:0}/{me.MaxLife:0} mana {me.Mana:0}/{me.MaxMana:0} gold {h.Gold} act {me.Act} order {g.Order.Kind}{(me.Shield > 0 ? $" shield {me.Shield:0}" : "")} points {h.AttrPoints}/{h.SkillPoints} dmg {st.DMin:0}-{st.DMax:0} armour {st.Armour:0}\n"));
        sb.Append("belt: " + string.Join(" ", h.Belt.Select((it, i) => $"{i + 1}:{(it == null ? "-" : it.Base + "x" + it.Count)}")) + $"; pack {Data.PackSize - h.Free()}/{Data.PackSize}\n");
        var near = g.Here.Actors.Where(a => a.Alive && Game.Dist(a, me) < 15).OrderBy(a => Game.Dist(a, me)).Take(12).ToList();
        if (near.Count > 0)
            sb.Append("near: " + string.Join("; ", near.Select(a => FormattableString.Invariant(
                $"{a.Id} {a.Name}{(a.Npc ? "" : $" L{a.Level} {a.Life:0}/{a.MaxLife:0} {a.Act}{(a.Awake ? "" : " asleep")}")} @{a.X:0.0},{a.Y:0.0} d{Game.Dist(a, me):0.0}"))) + "\n");
        var drops = g.Here.Drops.Where(d => Game.Dist(d.X, d.Y, me.X, me.Y) < 15).OrderBy(d => Game.Dist(d.X, d.Y, me.X, me.Y)).Take(10).ToList();
        if (drops.Count > 0)
            sb.Append("drops: " + string.Join("; ", drops.Select(d => FormattableString.Invariant($"{d.Id} {d.Item.Title} ({d.Item.Rarity}) @{d.X:0.0},{d.Y:0.0}"))) + "\n");
        var props = g.Map.Props.Where(p => p.Usable && !(p.OneUse && g.Here.Opened.Contains(p.Id)))
            .OrderBy(p => Game.Dist(p.X, p.Y, me.X, me.Y)).Take(6);
        sb.Append("things: " + string.Join("; ", props.Select(p => FormattableString.Invariant($"{p.Id} {p.Kind} @{p.X + p.W / 2.0:0.0},{p.Y + p.H / 2.0:0.0} d{Game.Dist(p.X, p.Y, me.X, me.Y):0}"))) + "\n");
        int left = g.Foes.Count(a => a.Alive);
        sb.Append($"{left} foes alive on this floor; quests {string.Join(",", h.Quests.Select(q => q.Key + "=" + q.Value))}\n");
        sb.Append(Ascii(g, 22, 12));
        return sb.ToString();
    }

    /// <summary>The floor around the hero: # rock, . floor, o something in the way, &lt; &gt; stairs, W waystone, C chest or
    /// crate (c opened), B barrel or urn (, broken), S stash, O well,
    /// $ a drop, N village folk, monsters by their first letter (capital: a champion or a named foe), @ the hero.</summary>
    public static string Ascii(Game g, int rx, int ry)
    {
        Floor f = g.Map;
        int cx = (int)g.Me.X, cy = (int)g.Me.Y;
        var grid = new char[2 * ry + 1, 2 * rx + 1];
        for (int y = -ry; y <= ry; y++)
            for (int x = -rx; x <= rx; x++)
            {
                int wx = cx + x, wy = cy + y;
                grid[y + ry, x + rx] = !f.In(wx, wy) || !f.Floor_(wx, wy) ? '#' : f.Blocked[wy * f.W + wx] ? 'o' : '.';
            }
        void Put(double x, double y, char c)
        {
            int gx = (int)Math.Floor(x) - cx + rx, gy = (int)Math.Floor(y) - cy + ry;
            if (gx >= 0 && gy >= 0 && gx <= 2 * rx && gy <= 2 * ry) grid[gy, gx] = c;
        }
        foreach (Prop p in f.Props)
        {
            bool spent = p.OneUse && g.Here.Opened.Contains(p.Id);
            char c = p.Kind switch
            {
                "down" => '>', "up" => '<', "waystone" => 'W', "stash" => 'S', "well" => 'O',
                "chest" or "crate" => spent ? 'c' : 'C', "barrel" or "urn" => spent ? ',' : 'B', _ => '\0',
            };
            if (c != '\0') Put(p.X, p.Y, c);
        }
        foreach (Drop d in g.Here.Drops) Put(d.X, d.Y, '$');
        foreach (Actor a in g.Here.Actors.Where(a => a.Alive))
            Put(a.X, a.Y, a.Npc ? 'N' : a.Title != null ? char.ToUpperInvariant(a.Def[0]) : a.Def[0]);
        Put(g.Me.X, g.Me.Y, '@');
        var sb = new StringBuilder($"map around {cx},{cy} (x {cx - rx}..{cx + rx}, y {cy - ry}..{cy + ry}):\n");
        for (int y = 0; y <= 2 * ry; y++)
        {
            for (int x = 0; x <= 2 * rx; x++) sb.Append(grid[y, x]);
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }
}
