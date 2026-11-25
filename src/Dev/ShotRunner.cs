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
            Root.Instance.ShowTitle();
            await Shot("title");
            ((TitleScreen)Root.Instance.Current).Choosing = 1;
            Root.Instance.Show(new TitleScreen { Choosing = 1 });
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
            Screen.Open("right", "inventory");
            Screen.Open("left", "character");
            await Shot("character");
            Screen.Open("left", "skills");
            Screen.Open("right", "");
            await Shot("skills");
            Screen.CloseAll();

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
            g.OrderSkill("meteor", pack.X, pack.Y);
            await Run(0.7);
            g.OrderAttack(pack.Id, "chain");
            await Run(0.9);
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
