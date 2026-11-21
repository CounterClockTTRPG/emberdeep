using GodotAgent;
using Godot;

namespace Starter;

/// <summary>Playing: the board drawn in code, the HUD, the keys (arrows or WASD to move, Esc for the title).</summary>
public partial class GameScreen : Control
{
    public Game Game;
    public const int Cell = 56;
    private Label _hud;
    private Vector2 _origin;

    public override void _Ready()
    {
        Theme = UITheme.Theme;
        FocusMode = FocusModeEnum.All;
        _hud = UITheme.L("", 22, UITheme.Text);
        AddChild(_hud);
        UITheme.Anchor(_hud, 0, 0, 1, 0, 24, 18, -24, 60);
        var back = UITheme.B("Title (Esc)", () => Root.Instance.ShowTitle(), 16);
        AddChild(back);
        UITheme.Anchor(back, 1, 0, 1, 0, -170, 16, -24, 52);
    }

    /// <summary>Where a board cell is on screen (the agent's 'at X Y' uses this).</summary>
    public Vector2 CellCentre(int x, int y) => GetGlobalTransformWithCanvas() * (_origin + new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell));

    public override void _Process(double delta)
    {
        // Under an agent, time only passes while a command runs (AgentServer.Running is always true otherwise).
        if (AgentServer.Running) Game.Step(delta);
        _hud.Text = Game.Won ? $"All {Game.Goal} gems gathered in {Game.Steps} steps!  (Esc for the title)"
            : $"Gems {Game.Score} / {Game.Goal}     Steps {Game.Steps}     Time {Game.Time:0}s";
        QueueRedraw();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k) return;
        (int dx, int dy) = k.Keycode switch
        {
            Key.Left or Key.A => (-1, 0), Key.Right or Key.D => (1, 0), Key.Up or Key.W => (0, -1), Key.Down or Key.S => (0, 1), _ => (0, 0),
        };
        if (k.Keycode == Key.Escape) { Root.Instance.ShowTitle(); GetViewport().SetInputAsHandled(); return; }
        if (dx == 0 && dy == 0) return;
        string what = Game.Move(dx, dy);
        Sounds.Play(what switch { "gem" => "gem", "won" => "win", "blocked" => "bump", _ => "step" }, what == "moved" ? 0.35f : 0.7f, what == "moved" ? 0.08f : 0);
        GetViewport().SetInputAsHandled();
    }

    public override void _Draw()
    {
        _origin = (Size - new Vector2(Game.W, Game.H) * Cell) / 2 + new Vector2(0, 30);
        for (int y = 0; y < Game.H; y++)
            for (int x = 0; x < Game.W; x++)
            {
                var r = new Rect2(_origin + new Vector2(x, y) * Cell, new Vector2(Cell, Cell));
                bool wall = Game.Walls.Contains(y * Game.W + x);
                DrawRect(r.Grow(-1), wall ? new Color(0.22f, 0.24f, 0.29f) : (x + y) % 2 == 0 ? new Color(0.11f, 0.12f, 0.145f) : new Color(0.12f, 0.13f, 0.16f));
            }
        float pulse = 0.85f + 0.15f * Mathf.Sin((float)Game.Time * 4);
        foreach (int c in Game.Gems)
        {
            Vector2 m = _origin + new Vector2(c % Game.W + 0.5f, c / Game.W + 0.5f) * Cell;
            float s = Cell * 0.28f * pulse;
            DrawColoredPolygon(new[] { m + new Vector2(0, -s), m + new Vector2(s, 0), m + new Vector2(0, s * 1.2f), m + new Vector2(-s, 0) }, UITheme.Accent);
        }
        Vector2 p = _origin + new Vector2(Game.X + 0.5f, Game.Y + 0.5f) * Cell;
        DrawCircle(p, Cell * 0.32f, new Color(0.45f, 0.75f, 0.95f));
        DrawCircle(p + new Vector2(-6, -6), Cell * 0.08f, new Color(1, 1, 1, 0.8f));
    }
}
