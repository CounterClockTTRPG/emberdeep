using System;
using System.Linq;
using System.Text;
using Godot;

namespace Emberdeep;

/// <summary>
/// The balance report (<c>./play.sh --check --only Balance</c>): a plain bot plays each class from the first floor to
/// the bottom, clearing every floor, drinking when hurt, spending its points, wearing what is better. It prints the
/// level, deaths and time per floor, and checks the game can be won by playing it plainly.
/// </summary>
public partial class SelfCheck
{
    [Section(OnlyWhenAsked = true)]
    private void Balance()
    {
        var report = new StringBuilder("\n  balance: class, then per floor: level reached / deaths / minutes\n");
        foreach (Cls c in Enum.GetValues<Cls>())
        {
            Game g = Game.New(1000 + (ulong)c, c);
            var bot = new Bot(g);
            report.Append($"  {c,-9}");
            int totalDeaths = 0;
            for (int depth = 1; depth <= Data.Bottom; depth++)
            {
                g.Enter(depth, null);
                g.Hero.Waystones.Add(depth);
                int deaths = g.Hero.Deaths;
                double start = g.Time;
                bot.Clear(depth, 900);
                int died = g.Hero.Deaths - deaths;
                totalDeaths += died;
                report.Append($" {depth}:{g.Hero.Level}/{died}/{(g.Time - start) / 60:0.0}");
                if (System.Environment.GetEnvironmentVariable("BALANCE_DEBUG") != null)
                {
                    var left = g.Foes.Where(a => a.Alive).ToList();
                    GD.Print($"  {c} floor {depth}: " + string.Join(" ", bot.Tally.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")) +
                             $"; {left.Count} left: " + string.Join("; ", left.Take(5).Select(a => $"{a.Def}@{a.X:0},{a.Y:0}{(a.Awake ? " awake" : "")}")));
                    bot.Tally.Clear();
                    int[] dist = g.Map.Distances(((int)g.Me.X, (int)g.Me.Y));
                    foreach (Actor a in left.Take(2))
                        GD.Print($"    {a.Def} {a.Id} @{a.X:0.00},{a.Y:0.00} reach {dist[(int)a.Y * g.Map.W + (int)a.X]} path {g.Map.Path(g.Me.X, g.Me.Y, a.X, a.Y, 8000).Count} walk {g.Map.Walk(a.X, a.Y)} act {a.Act} hero {g.Me.X:0.0},{g.Me.Y:0.0} order {g.Order.Kind}/{g.Order.Target}/{g.Order.Skill} heroact {g.Me.Act}");
                }
            }
            report.Append($"  kills {g.Hero.Kills}, gold {g.Hero.Gold}, {(g.Won ? "WON" : "not won")}\n");
            Check(g.Won, $"{c}: a plain bot beats Vorgath");
            Check(totalDeaths < 25, $"{c}: and dies fewer than 25 times on the way ({totalDeaths})");
        }
        GD.Print(report.ToString());
    }

    /// <summary>A plain player: the nearest foe, the class's main skills, a draught when hurt.</summary>
    private sealed class Bot
    {
        private readonly Game g;
        private readonly System.Collections.Generic.HashSet<int> _left = new();
        public readonly System.Collections.Generic.Dictionary<string, int> Tally = new();
        public Bot(Game game) { g = game; }

        public void Clear(int depth, double seconds)
        {
            double end = g.Time + seconds;
            while (g.Time < end && !(depth == Data.Bottom && g.Won))
            {
                if (g.Depth != depth) { Shop(); g.Enter(depth, null); }
                Think();
                if (!g.Foes.Any(a => a.Alive) && g.Here.Drops.All(d => _left.Contains(d.Id))) return;
                for (int k = 0; k < 6; k++) g.Step(Game.Tick);
                foreach (Ev e in g.Events) Tally[e.Kind] = (Tally.TryGetValue(e.Kind, out int n) ? n : 0) + 1;
                g.Events.Clear();
            }
        }

        /// <summary>After a death, in the village: draughts from the healer.</summary>
        private void Shop()
        {
            for (int k = 0; k < 8 && g.Hero.Gold >= 25; k++) if (g.Buy("healer", k % 3 == 2 ? 1 : 0) != null) break;
        }

        private void Think()
        {
            Hero h = g.Hero;
            Actor me = g.Me;
            if (!me.Alive) return;
            if (me.Life < me.MaxLife * 0.4) Drink(Kind.PotLife);
            if (me.Mana < me.MaxMana * 0.15) Drink(Kind.PotMana);
            Spend();
            Wear();
            if (g.Order.Kind != "none" && g.Order.Kind != "move") return;
            if (me.Act is "attack" or "cast" or "leap") return;
            Actor t = g.Foes.Where(a => a.Alive).OrderBy(a => (a.Awake ? 0 : 6) + Game.Dist(a, me)).FirstOrDefault();
            Drop d = g.Here.Drops.Where(x => !_left.Contains(x.Id)).Where(x => x.Item.Base == "gold" || x.Item.B.Stacks || x.Item.Rarity >= Rarity.Magic || h.Free() > 10)
                .OrderBy(x => Game.Dist(x.X, x.Y, me.X, me.Y)).FirstOrDefault();
            if (d != null && (t == null || Game.Dist(d.X, d.Y, me.X, me.Y) < 4 && !(t.Awake && Game.Dist(t, me) < 5)))
            {
                if (Game.Dist(d.X, d.Y, me.X, me.Y) < 1.2) { if (!g.Pick(d)) _left.Add(d.Id); }
                else g.OrderPick(d.Id);
                return;
            }
            if (t == null) return;
            int near = g.Foes.Count(a => a.Alive && Game.Dist(a, me) < 3);
            int around = g.Foes.Count(a => a.Alive && Game.Dist(a, t) < 3);
            string skill = h.Class switch
            {
                Cls.Wanderer => near >= 3 && Can("quake") ? "quake" : near >= 3 && Can("warcry") ? "warcry" : near >= 2 && Can("cleave") ? "cleave" : Can("bash") ? "bash" : "attack",
                Cls.Rogue => around >= 3 && Can("rain") ? "rain" : around >= 2 && Can("multi") ? "multi" : Can("flurry") ? "flurry" : Can("power") ? "power" : "attack",
                _ => me.ShieldT <= 0 && Can("shield") ? "shield" : around >= 3 && Can("meteor") ? "meteor" : near >= 3 && Can("nova") ? "nova" : around >= 2 && Can("chain") ? "chain" : Can("firebolt") ? "firebolt" : "attack",
            };
            if (skill is "quake" or "warcry" or "nova" or "shield" or "rain" or "meteor" or "multi") g.OrderSkill(skill, t.X, t.Y);
            else g.OrderAttack(t.Id, skill);
        }

        private bool Can(string skill)
        {
            SkillDef s = Data.Skill(skill);
            return g.Hero.Rank(skill) > 0 && g.Me.Mana >= Data.ManaCost(s, g.Hero.BaseRank(skill)) + (skill is "bash" or "power" or "firebolt" ? 0 : 4);
        }

        private void Drink(Kind k)
        {
            for (int i = 0; i < 4; i++) if (g.Hero.Belt[i]?.B.Kind == k) { g.UseBelt(i); return; }
            for (int i = 0; i < g.Hero.Pack.Length; i++) if (g.Hero.Pack[i]?.B.Kind == k) { g.UsePack(i); return; }
        }

        private void Spend()
        {
            Hero h = g.Hero;
            while (h.AttrPoints > 0)
                h.Raise(h.Class switch
                {
                    Cls.Wanderer => h.AttrPoints % 5 < 2 ? "might" : "vigour",
                    Cls.Rogue => h.AttrPoints % 5 < 3 ? "agility" : "vigour",
                    _ => h.AttrPoints % 5 < 2 ? "spirit" : "vigour",
                });
            while (h.SkillPoints > 0)
            {
                var order = h.Class switch
                {
                    Cls.Wanderer => new[] { "quake", "cleave", "bash", "warcry", "ironskin", "brawn", "leap" },
                    Cls.Rogue => new[] { "rain", "multi", "power", "precision", "evasion", "flurry", "step" },
                    _ => new[] { "meteor", "chain", "firebolt", "nova", "shield", "arcane", "meditation" },
                };
                // A new skill first, then the main ones in turn.
                string pick = order.FirstOrDefault(s => h.BaseRank(s) == 0 && h.CannotLearn(s) == null)
                              ?? order.OrderBy(s => h.BaseRank(s)).FirstOrDefault(s => h.CannotLearn(s) == null);
                if (pick == null || h.Learn(pick) != null) break;
            }
        }

        private static double Score(Item it) => it.B.Weapon ? (it.Damage.lo + it.Damage.hi) / 2.0 * (1 + it.B.SpellPct / 50.0) + it.Mods.Count * 2
            : it.Armour + it.Mods.Count * 4;

        private void Wear()
        {
            Hero h = g.Hero;
            for (int k = 0; k < h.Pack.Length; k++)
            {
                Item it = h.Pack[k];
                if (it?.B.Wearable != true) continue;
                if (h.CannotWear(it) != null) { if (it.Requires > h.Level + 6) h.Pack[k] = null; continue; }
                // The class keeps its kind of weapon.
                if (it.B.Weapon && (h.Class == Cls.Rogue) != it.B.Ranged) { h.Pack[k] = null; continue; }
                if (it.B.Weapon && h.Class == Cls.Sorcerer && it.B.Kind != Kind.Staff) { h.Pack[k] = null; continue; }
                if (it.B.Kind == Kind.Shield && h.Weapon?.B.TwoHanded == true) { h.Pack[k] = null; continue; }
                Slot sl = h.SlotFor(it).Value;
                Item worn = h.Equip[(int)sl];
                if (worn == null || Score(it) > Score(worn)) h.EquipFromPack(k);
                else if (h.Free() < 6) { g.Hero.Gold += it.Worth; h.Pack[k] = null; }
            }
        }
    }
}
