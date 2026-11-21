using System.Linq;
using SfxGen;

namespace Starter;

/// <summary>The sample game's rules, its save, and the parts every game made from the starter has.</summary>
public partial class SelfCheck
{
    [Section]
    private void Moving()
    {
        Game g = Game.New(7);
        g.Walls.Clear();
        g.Gems.Clear();
        g.X = 0; g.Y = 0;
        Eq(g.Move(-1, 0), "blocked", "the edge of the board stops you");
        Eq(g.Move(1, 0), "moved", "a step onto an open cell");
        Eq((g.X, g.Steps), (1, 1), "one cell right, one step");
        g.Walls.Add(0 * Game.W + 2);
        Eq(g.Move(1, 0), "blocked", "a wall stops you");
        Eq(g.Steps, 1, "and costs no step");
    }

    [Section]
    private void Gems()
    {
        Game g = Game.New(7);
        g.Walls.Clear();
        g.Gems.Clear();
        g.X = 0; g.Y = 0;
        g.Gems.Add(1);
        Eq(g.Move(1, 0), "gem", "stepping on a gem gathers it");
        Check(g.Score == 1 && g.Gems.Count == 0, "the gem is counted and gone");
        g.Score = Game.Goal - 1;
        g.Gems.Add(2);
        Eq(g.Move(1, 0), "won", "the last gem wins");
        Eq(g.Move(1, 0), "blocked", "nothing moves once it is won");
        Game a = Game.New(99), b = Game.New(99);
        a.Step(10); b.Step(10);
        Check(a.Gems.SequenceEqual(b.Gems) && a.Gems.Count > 3, "gems appear over time, the same for the same seed");
        Check(!Game.New(1).Walls.SetEquals(Game.New(2).Walls), "another seed, another board");
    }

    [Section]
    private void Saving()
    {
        Game g = Game.New(5);
        g.Move(1, 0);
        g.Step(4);
        Game back = Saves.Read<Game>(Saves.Write(g));
        Check(back.Seed == g.Seed && back.X == g.X && back.Steps == g.Steps && back.Gems.SequenceEqual(g.Gems) && back.Walls.SetEquals(g.Walls),
            "a game survives a save");
        Check(Profile.Scratch && Profile.SlotPath(1).Contains("scratch"), "the self-check never touches the player's slots");
        Check(Profile.SaveGame(g, 2) && Profile.LoadGame(2)?.Seed == g.Seed, "a slot is written and read back");
        Profile.DeleteSlot(2);
        Check(Profile.LoadGame(2) == null, "and cleared");
    }

    [Section]
    private void SoundsAndLook()
    {
        foreach (var (name, def) in Sounds.All) Check(Effects.Render(def).Any(s => System.Math.Abs(s) > 0.05f), $"the '{name}' sound is made");
        Check(UITheme.Theme != null && UITheme.Body != null, "the theme and its fonts load (Godot's own when assets/fonts is empty)");
    }
}
