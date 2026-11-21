using System;
using System.IO;
using Godot;
using STask = System.Threading.Tasks.Task;

namespace Starter;

/// <summary>
/// The screenshot walkthrough (<c>./play.sh --shots</c>): the real game, a fixed seed and fixed steps, a picture of
/// each screen in shots/. The same build gives the same pictures, so they can be checked against a baseline:
/// <c>shotkit check shots docs/baseline</c>. Add a step here for every new screen.
/// </summary>
public partial class ShotRunner : Node
{
    private string _dir;

    public override void _Ready()
    {
        _dir = ProjectSettings.GlobalizePath("res://shots");
        Directory.CreateDirectory(_dir);
        _ = Walk();
    }

    private async STask Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    private async STask Shot(string name)
    {
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_dir, name + ".png"));
        GD.Print($"  shots/{name}.png");
    }

    private async STask Walk()
    {
        try
        {
            GD.Print("walkthrough:");
            Root.Instance.ShowTitle();
            await Shot("title");
            Root.Instance.Play(Game.New(42));
            await Shot("game");
            Game g = Root.Instance.Game;
            foreach (var (dx, dy) in new[] { (1, 0), (1, 0), (0, 1), (0, 1), (-1, 0) }) g.Move(dx, dy);
            await Shot("game-moved");
        }
        catch (Exception e) { GD.PrintErr("walkthrough failed: " + e); GetTree().Quit(1); return; }
        GetTree().Quit(0);
    }
}
