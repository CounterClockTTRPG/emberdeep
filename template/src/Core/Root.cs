using System;
using Godot;

namespace Starter;

/// <summary>
/// The main scene, and the only .tscn: owns the flow between screens and the command-line entry points --
/// <c>--check</c> (the headless self-check), <c>--shots</c> (the screenshot walkthrough), <c>--agent</c> (remote
/// control by godot-agent).
/// </summary>
public partial class Root : Node
{
    public static Root Instance { get; private set; }
    public Game Game;
    public Control Current;
    private CanvasLayer _ui;

    public override void _Ready()
    {
        Instance = this;
        GetWindow().Title = ProjectSettings.GetSetting("application/config/name").AsString();
        string[] args = OS.GetCmdlineUserArgs();
        bool check = Array.IndexOf(args, "--check") >= 0, shots = Array.IndexOf(args, "--shots") >= 0, agent = Array.IndexOf(args, "--agent") >= 0;
        Profile.Scratch = check || shots;
        Profile.Agent = agent;
        Profile.Load();
        Profile.Current.Apply();
        _ui = new CanvasLayer { Name = "UI", Layer = 10 };
        AddChild(_ui);
        if (check) { AddChild(new SelfCheck()); return; }
        Sounds.Start(this);
        if (shots) { AddChild(new ShotRunner()); return; }
        if (agent) AddChild(AgentHooks.Make(this));
        ShowTitle();
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 })
        {
            Profile.Current.Fullscreen = !Profile.Current.Fullscreen;
            Profile.Current.Apply();
            Profile.Current.Save();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Notification(int what)
    {
        // Closing the window keeps the game where it was.
        if (what == NotificationWMCloseRequest && Game != null && !Profile.Scratch) Profile.SaveGame(Game);
    }

    public void Show(Control c)
    {
        foreach (Node n in _ui.GetChildren()) n.QueueFree();
        _ui.AddChild(c);
        UITheme.Anchor(c, 0, 0, 1, 1);
        Current = c;
    }

    public void ShowTitle()
    {
        if (Game != null && !Profile.Scratch) Profile.SaveGame(Game);
        Game = null;
        Show(new TitleScreen());
    }

    /// <summary>A new game in a slot, over whatever was there.</summary>
    public void NewGame(int slot, ulong seed = 0)
    {
        Profile.Slot = slot;
        Profile.Current.LastSlot = slot;
        Profile.Current.Save();
        if (seed == 0) seed = ((ulong)GD.Randi() << 32) | GD.Randi();
        Play(Game.New(seed));
        if (!Profile.Scratch) Profile.SaveGame(Game, slot);
    }

    public bool Continue(int slot)
    {
        Game g = Profile.LoadGame(slot);
        if (g == null) return false;
        Profile.Slot = slot;
        Profile.Current.LastSlot = slot;
        Profile.Current.Save();
        Play(g);
        return true;
    }

    public void Play(Game g)
    {
        Game = g;
        Show(new GameScreen { Game = g });
    }
}
