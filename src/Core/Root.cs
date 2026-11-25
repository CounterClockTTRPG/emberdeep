using System;
using System.Linq;
using Godot;
using GodotAgent;

namespace Emberdeep;

/// <summary>
/// The main scene, and the only .tscn: owns the flow between screens and the command-line entry points --
/// <c>--check</c> (the headless self-check), <c>--shots</c> (the screenshot walkthrough), <c>--agent</c> (remote
/// control by godot-agent), <c>--reel DIR</c> (the same, filmed by Reel: the agent folder is DIR and the game holds
/// still between commands; <c>--reel-live</c> lets it run).
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
        Data.BaseBodies = World3D.Dressable;
        // The treasure map's icon, once assetgen has made one on a dark background (cut out cleanly on the floor).
        if (FileAccess.FileExists("res://assets/icons/map.png")) Base.MapIcon = "map";
        GetWindow().Title = ProjectSettings.GetSetting("application/config/name").AsString();
        string[] args = OS.GetCmdlineUserArgs();
        bool check = Array.IndexOf(args, "--check") >= 0, shots = Array.IndexOf(args, "--shots") >= 0, agent = Array.IndexOf(args, "--agent") >= 0;
        string reel = AgentServer.ReelDir(args);
        agent |= reel != null;
        Profile.Scratch = check || shots;
        Profile.Agent = agent;
        Profile.Load();
        Profile.Current.Apply();
        // On a phone everything is drawn a third larger, so a thumb can hit it.
        if (OS.HasFeature("mobile") && DisplayServer.IsTouchscreenAvailable()) GetWindow().ContentScaleFactor = 1.35f;
        _ui = new CanvasLayer { Name = "UI", Layer = 10 };
        AddChild(_ui);
        if (check) { AddChild(new SelfCheck()); return; }
        if (Array.IndexOf(args, "--sounds") >= 0)
        {
            GD.Print("sounds:");
            Sounds.WriteAll(ProjectSettings.GlobalizePath("res://shots/audio"));
            GetTree().Quit();
            return;
        }
        Sounds.Start(this);
        if (shots) { AddChild(new ShotRunner()); return; }
        if (agent)
        {
            AgentServer server = AgentHooks.Make(this);
            if (reel != null) { server.Dir = reel; server.Hold = !args.Contains("--reel-live"); }
            AddChild(server);
        }
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
        // Android's back button (project.godot: quit_on_go_back=false), handled like Escape.
        if (what == NotificationWMGoBackRequest)
        {
            if (Current is GameScreen gs) gs.Back();
            else if (Current is TitleScreen t) t.Back();
        }
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
    public void NewGame(int slot, ulong seed = 0, Cls cls = Cls.Wanderer, bool female = false, string name = null)
    {
        Profile.Slot = slot;
        Profile.Current.LastSlot = slot;
        Profile.Current.Save();
        if (seed == 0) seed = ((ulong)GD.Randi() << 32) | GD.Randi();
        Play(Game.New(seed, cls, female, name));
        if (!Profile.Scratch) Profile.SaveGame(Game, slot);
    }

    public bool Continue(int slot)
    {
        Game g = Profile.LoadGame(slot);
        if (g == null) return false;
        g.Restock();
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
