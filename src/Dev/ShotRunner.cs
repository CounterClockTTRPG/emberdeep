using System;
using System.IO;
using System.Linq;
using Godot;
using STask = System.Threading.Tasks.Task;

namespace Emberdeep;

/// <summary>
/// The screenshot walkthrough (<c>./play.sh --shots [--only NAME]</c>): the real game, a fixed seed and fixed steps, a
/// picture of each screen in shots/. The same build gives the same pictures, so they can be checked against a baseline:
/// <c>shotkit check shots docs/baseline</c>. Add a step here for every new screen.
/// </summary>
public partial class ShotRunner : Node
{
    private string _dir, _only;

    public override void _Ready()
    {
        _dir = ProjectSettings.GlobalizePath("res://shots");
        Directory.CreateDirectory(_dir);
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--only");
        if (at >= 0 && at + 1 < args.Length) _only = args[at + 1];
        _ = Walk();
    }

    private async STask Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    private async STask Shot(string name)
    {
        await Frames(6);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (_only != null && !name.Contains(_only)) return;
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_dir, name + ".png"));
        GD.Print($"  shots/{name}.png");
    }

    private static GameScreen Screen => Root.Instance.Current as GameScreen;

    /// <summary>Let the game run for a while (as it would when played), a frame at a time.</summary>
    private async STask Run(double seconds)
    {
        for (double t = 0; t < seconds; t += 1 / 30.0)
        {
            Root.Instance.Game.Step(1 / 30.0);
            await Frames(1);
        }
    }

    private async STask Walk()
    {
        try
        {
            GD.Print("walkthrough:");
            // The title with a hero in slot 1 (a scratch slot): a rogue with a long bow, part-way down.
            Game saved = Game.New(42, Cls.Rogue);
            saved.Hero.Level = 9;
            saved.Hero.Wear(new Item("long-bow"));
            saved.Hero.Wear(new Item("leather-armour"));
            saved.Enter(5, null);
            Profile.SaveGame(saved, 1);
            Profile.Current.LastSlot = 1;
            Root.Instance.ShowTitle();
            await Frames(60);
            await Shot("title");
            Root.Instance.Show(new TitleScreen { Choosing = 2 });
            await Frames(60);
            await Shot("classes");

            // The village: the elder's quest, the smith's wares.
            Root.Instance.Play(Game.New(42, Cls.Wanderer));
            Game g = Root.Instance.Game;
            await Frames(10);
            await Shot("village");
            Actor elder = g.Here.Actors.First(a => a.Def == "elder");
            g.Me.X = elder.X - 1.5; g.Me.Y = elder.Y + 1.5;
            g.OrderTalk(elder.Id);
            await Run(1);
            await Shot("talk");
            Screen.CloseAll();
            Screen.Trading = "smith";
            Screen.Open("left", "shop");
            Screen.Trading = "smith";
            Screen.Open("right", "inventory");
            await Shot("shop");
            Screen.CloseAll();

            // The first floor: a fight.
            g.Hero.Level = 6; g.Hero.Skills["bash"] = 3; g.Hero.Skills["cleave"] = 2; g.Hero.Skills["warcry"] = 1; g.Hero.Skills["leap"] = 1; g.Hero.Changed();
            g.Me.Life = g.Hero.S.MaxLife; g.Me.Mana = g.Hero.S.MaxMana;
            g.Enter(1, null);
            await Frames(10);
            await Shot("barrow");
            Actor foe = g.Foes.OrderBy(a => Game.Dist(a, g.Me)).First();
            Prop near = null;
            g.Me.X = foe.X + 2.5; g.Me.Y = foe.Y + 2.5;
            if (!g.Map.Walk(g.Me.X, g.Me.Y)) (g.Me.X, g.Me.Y) = (foe.X, foe.Y + 1.5);
            _ = near;
            g.OrderAttack(foe.Id, "bash");
            await Run(1.6);
            await Shot("fight");
            // Gems: two in the pack, one picked up, a sword with three sockets (two set), armour with one.
            g.Hero.Equip[(int)Slot.Weapon].Sockets = 3;
            g.Hero.Equip[(int)Slot.Weapon].Gems.AddRange(new[] { "gem-ruby:2", "gem-topaz:1" });
            g.Hero.Equip[(int)Slot.Body].Sockets = 1;
            g.Hero.Pack[0] = Item.Gem("sapphire", 1);
            g.Hero.Pack[1] = Item.Gem("bloodstone", 2);
            g.Hero.Pack[2] = Loot.Make("heater-shield", Rarity.Plain, 8, new Rng(3));
            g.Hero.Pack[2].Sockets = 2;
            g.Hero.Changed();
            Screen.GemPick = 1;
            Screen.Open("right", "inventory");
            Screen.Open("left", "character");
            await Shot("character");
            Screen.GemPick = -1;
            Screen.Open("left", "skills");
            Screen.Open("right", "");
            await Shot("skills");
            Screen.CloseAll();
            Screen.Open("centre", "menu");
            await Shot("menu");
            Screen.CloseAll();

            // Things to use: before (glowing, the name under the cursor), opening and breaking, after.
            Root.Instance.Play(Game.New(21, Cls.Wanderer));
            g = Root.Instance.Game;
            // The first floor that has a chest and something to break.
            for (int d = 1; d <= 9; d++)
            {
                g.Enter(d, null);
                if (g.Map.Props.Any(p => p.Kind == "chest") && g.Map.Props.Any(p => p.Breaks)) break;
            }
            g.Here.Actors.Clear();
            Floor fl = g.Map;
            Prop box = fl.Props.Where(p => p.Kind == "chest")
                .OrderBy(p => fl.Props.Where(o => o.Breaks).Min(o => Math.Abs(o.X - p.X) + Math.Abs(o.Y - p.Y))).First();
            Prop jar = fl.Props.Where(p => p.Breaks).OrderBy(p => Math.Abs(p.X - box.X) + Math.Abs(p.Y - box.Y)).First();
            g.Me.X = (box.X + jar.X) / 2.0 + 1.5; g.Me.Y = (box.Y + jar.Y) / 2.0 + 2;
            if (!fl.Walk(g.Me.X, g.Me.Y)) (g.Me.X, g.Me.Y) = (box.X + 0.5, box.Y + 1.6);
            Screen.World.Zoom = 0.55f;
            await Run(0.5);
            Input.WarpMouse(Screen.ScreenOf(box.X + 0.5, box.Y + 0.5, 0.4f));
            await Run(0.3);
            await Shot("things-before");
            g.Use(box);
            g.Use(jar);
            Input.WarpMouse(new Vector2(40, 40));
            await Run(1.2);
            await Shot("things-after");

            // A sorcerer in the ember halls: spells.
            Root.Instance.Play(Game.New(7, Cls.Sorcerer));
            g = Root.Instance.Game;
            g.Hero.Level = 14;
            foreach (string s in new[] { "firebolt", "nova", "chain", "meteor", "shield" }) g.Hero.Skills[s] = 4;
            g.Hero.Changed();
            g.Enter(8, null);
            g.Me.Life = g.Hero.S.MaxLife; g.Me.Mana = g.Hero.S.MaxMana;
            Actor pack = g.Foes.OrderBy(a => Game.Dist(a, g.Me)).First();
            g.Me.X = pack.X + 4; g.Me.Y = pack.Y + 4;
            if (!g.Map.Walk(g.Me.X, g.Me.Y)) (g.Me.X, g.Me.Y) = (pack.X, pack.Y + 2);
            // Harmless for the picture (the walkthrough runs slowly in software and the deep would kill him first).
            foreach (Actor f in g.Foes) { f.DmgX = 0; f.Accuracy = 0; }
            g.Hero.Vigour = 400; g.Hero.Changed();
            g.Me.Life = g.Hero.S.MaxLife; g.Me.Mana = 500;
            g.OrderSkill("meteor", pack.X, pack.Y);
            await Run(0.55);
            g.Me.Mana = 500;
            g.OrderAttack(pack.Id, "chain");
            await Run(0.45);
            g.Me.Mana = 500;
            g.OrderSkill("nova", g.Me.X, g.Me.Y);
            await Run(0.3);
            g.Me.Mana = 500;
            g.OrderAttack(pack.Id, "firebolt");
            await Run(0.2);
            await Shot("spells");
            await Run(0.6);
            Screen.Toggle("left", "");
            await Shot("map-small");

            // The rogue: arrows.
            Root.Instance.Play(Game.New(11, Cls.Rogue));
            g = Root.Instance.Game;
            g.Hero.Level = 10; g.Hero.Skills["multi"] = 4; g.Hero.Changed();
            g.Enter(4, null);
            Actor mark = g.Foes.OrderBy(a => Game.Dist(a, g.Me)).First();
            g.Me.X = mark.X + 5; g.Me.Y = mark.Y + 3;
            if (!g.Map.Walk(g.Me.X, g.Me.Y)) (g.Me.X, g.Me.Y) = (mark.X, mark.Y + 3);
            g.Me.Mana = g.Hero.S.MaxMana;
            g.OrderSkill("multi", mark.X, mark.Y);
            await Run(0.45);
            await Shot("arrows");

            // A treasure map read in the village: the lair's portal; inside, the Hollow King on his dais.
            Root.Instance.Play(Game.New(5, Cls.Sorcerer));
            g = Root.Instance.Game;
            g.Hero.Level = 16; g.Hero.Vigour = 300; g.Hero.Changed();
            g.Me.Life = g.Hero.S.MaxLife;
            g.Me.Facing = 0.8;
            g.Hero.Pack[0] = Loot.Map(18, new Rng(7), 3);
            g.UsePack(0);
            await Run(0.6);
            await Shot("lair-portal");
            g.EnterLair();
            Actor king = g.Foes.First(a => a.Boss);
            foreach (Actor f in g.Foes) { f.DmgX = 0; f.Accuracy = 0; }
            g.Me.X = king.X + 1.5; g.Me.Y = king.Y + 5;
            if (!g.Map.Walk(g.Me.X, g.Me.Y)) (g.Me.X, g.Me.Y) = (king.X, king.Y + 3);
            await Run(0.4);
            await Shot("lair");

            // The bottom.
            Root.Instance.Play(Game.New(3, Cls.Wanderer));
            g = Root.Instance.Game;
            g.Hero.Level = 25; g.Hero.Changed();
            g.Enter(Data.Bottom, null);
            Actor boss = g.Foes.First(a => a.Def == "vorgath");
            g.Me.X = boss.X + 1; g.Me.Y = boss.Y + 7;
            g.Me.Life = g.Hero.S.MaxLife;
            await Run(0.15);
            await Shot("throne");
        }
        catch (Exception e) { GD.PrintErr("walkthrough failed: " + e); GetTree().Quit(1); return; }
        GetTree().Quit(0);
    }
}
