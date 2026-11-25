// godot-agent -- remote control of a running Godot (C#) game for an agent playtester.
// https://github.com/ (local building block); MIT licence. Copy this folder into a game's addons/.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using STask = System.Threading.Tasks.Task;

namespace GodotAgent;

/// <summary>
/// Drives the real game for an agent: <c>bin/agent.sh "state; press Start; key Space; shot title"</c> writes the
/// commands to <c>agent/cmd.txt</c>, this node runs them in order and writes the reply to <c>agent/out.txt</c>.
/// Keys and clicks go in through <see cref="Input.ParseInputEvent"/> as a player's would, and the real keyboard and
/// mouse are shut out. By default game time only passes while a command runs (<see cref="Frozen"/>): games check
/// <see cref="Running"/> before advancing their simulation, so an agent can think between commands for free.
///
/// <code>
/// var agent = new AgentServer { State = () => $"turn {game.Turn}", OnQuit = () => game.Save() };
/// agent.CellToScreen = (x, y) => map.ScreenOf(x, y);           // enables 'at X Y' and 'drag X1 Y1 X2 Y2'
/// agent.Command("gold", "gold N            (debug) set the gold", async a => { game.Gold = AgentServer.Int(a[1]); return "ok"; });
/// AddChild(agent);
/// </code>
/// </summary>
public partial class AgentServer : Node
{
    /// <summary>True once an agent server runs in this game.</summary>
    public static bool Active { get; private set; }
    /// <summary>Game time only passes while a command runs (default on; 'freeze off' lets it run freely).</summary>
    public static bool Frozen = true;
    /// <summary>Set while a command wants the game to run (a wait, a key, a press); custom commands may set it too.</summary>
    public static bool Busy;
    /// <summary>Whether the game should advance now: always without an agent; with one, while a command runs or unfrozen.</summary>
    public static bool Running => !Active || (!Frozen && !_hold) || Busy;
    private static bool _hold;
    private static bool _quitting;

    /// <summary>
    /// Hold: block the main loop between command lines, so no frame is drawn until the next command arrives. For
    /// filming under Godot's Movie Maker, which records every frame the game renders: a game that idles between
    /// commands would put idle frames into the movie. Implies frozen game time. Set by <c>hold on|off</c>, or by the
    /// game at start (<c>Hold = !args.Contains("--reel-live")</c>, see <see cref="ReelDir"/>).
    /// </summary>
    public bool Hold { get => _hold; set => _hold = value; }

    /// <summary>The DIR after <c>--reel</c> in the user args (<c>OS.GetCmdlineUserArgs()</c>), or null without one.</summary>
    public static string ReelDir(string[] userArgs)
    {
        int i = Array.IndexOf(userArgs, "--reel");
        return i >= 0 && i + 1 < userArgs.Length ? userArgs[i + 1] : null;
    }

    /// <summary>Input events from the agent carry this device id; everything else is dropped.</summary>
    public const int Device = 4242;

    /// <summary>Where cmd.txt and out.txt live (default res://agent, made absolute).</summary>
    public string Dir = "res://agent";
    /// <summary>The game in words for 'state' (null: the scene and the buttons on screen).</summary>
    public Func<string> State;
    /// <summary>A map cell to a screen position (layout pixels), for 'at' and 'drag'; null when the game has no grid.</summary>
    public Func<int, int, Vector2> CellToScreen;
    /// <summary>
    /// Where a thing the game knows is on screen, for 'locate TARGET' (layout pixels, the same space as 'mouse'):
    /// the rectangle, or null when the game does not know the target (then a button whose label contains it is used).
    /// </summary>
    public Func<string, Rect2?> Locate;
    /// <summary>Called by 'quit' before the game closes (an ironman save, say).</summary>
    public Action OnQuit;
    /// <summary>Keep the window from taking focus or catching the real mouse, so the agent can run beside you.</summary>
    public bool Unobtrusive = true;

    private readonly Dictionary<string, (string help, Func<string[], System.Threading.Tasks.Task<string>> run)> _commands = new();
    private string _dir, _lastId;
    private double _poll, _gameTime;
    private bool _running, _down;
    private Vector2 _mouse = new(100, 100);

    /// <summary>Adds a game command. The handler gets the words (a[0] is the command) and returns the reply line (or null).</summary>
    public void Command(string name, string help, Func<string[], System.Threading.Tasks.Task<string>> run) => _commands[name.ToLowerInvariant()] = (help, run);

    /// <summary>The command line after the command word (for commands taking free text).</summary>
    public static string Rest(string[] a) => string.Join(' ', a.Skip(1));
    public static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
    public static float Float(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private const string BuiltIn = @"commands (separate with ';'):
  help                  this list
  state                 the game in words
  shot NAME             screenshot to agent/NAME.png
  ui                    the buttons on screen, with where they are and whether they are on or disabled
  press TEXT            click the first enabled button whose text contains TEXT (dialogs first)
  type TEXT             type into the focused text field
  key K [MS]            tap a key, or hold it MS ms: letters, digits, Space Esc Enter Tab Backspace Delete
                        Up Down Left Right Home End PgUp PgDn F1..F12 Shift Ctrl Alt < > ? / - =; ctrl+K, shift+K
  mouse X Y             move the mouse to X,Y (layout pixels)
  size                  'size LW LH WW WH': the layout (what a movie shows) and the window, in pixels
  locate TARGET         'box X Y W H' (layout pixels) of what TARGET names: the game's own targets, else the first
                        visible enabled button whose text contains TARGET
  glide X Y MS [VX VY]  move the mouse to X,Y over MS ms of game time, easing in and out; straight, or with VX,VY a
                        quadratic Bézier whose control point is VX,VY (it bends toward it); may run a frame past MS
  click [left|right|middle] [MS]   click at the mouse; MS holds the button down that long (game time)
  down / up             hold / release the left button
  mdrag X1 Y1 X2 Y2     left-drag between two screen points
  rdrag DX DY           right-drag by DX,DY from the mouse
  wheel up|down [N]     turn the mouse wheel N notches at the mouse
  at X Y                move the mouse onto map cell X,Y (when the game maps cells)
  drag X1 Y1 X2 Y2      left-drag from map cell to map cell (when the game maps cells)
  wait MS               let the game run MS milliseconds of game time (real time outside Movie Maker)
  time                  game time so far: 'time SECONDS' (the sum of the frames' delta since the server started)
  frames N              let N frames pass
  tree [DEPTH] [PATH]   the scene tree under PATH (default the root), DEPTH levels (default 3)
  prop PATH NAME        read a property of a node
  perf [FRAMES]         time FRAMES frames (default 120): frame and process time, draw calls, objects, nodes
  freeze on|off         on (default): game time passes only while a command runs
  hold on|off           on: no frame is drawn between command lines (for filming under Movie Maker)
  quit                  close the game";

    public string Help => BuiltIn + (_commands.Count == 0 ? "" : "\n" + string.Join("\n", _commands.Values.Select(c => "  " + c.help)));

    public override void _Ready()
    {
        Active = true;
        _quitting = false;
        ProcessMode = ProcessModeEnum.Always;
        if (Unobtrusive && DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
            DisplayServer.WindowSetMousePassthrough(new[] { Vector2.Zero, new Vector2(1, 0), new Vector2(1, 1) });
        }
        _dir = Dir.StartsWith("res://") || Dir.StartsWith("user://") ? ProjectSettings.GlobalizePath(Dir) : Dir;
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "out.txt"), "ready\n" + Help + "\n");
        string cmd = Path.Combine(_dir, "cmd.txt");
        if (File.Exists(cmd)) _lastId = File.ReadLines(cmd).FirstOrDefault();
        GD.Print("agent: listening on " + _dir);
    }

    public override void _ExitTree() => Active = false;

    public override void _Input(InputEvent e)
    {
        if (e.Device != Device) GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        _gameTime += delta;
        if (_running) return;
        if (Hold)
        {
            // No frame finishes until a command arrives. A line that completes without waiting for a frame (time,
            // state of a custom command) leaves the loop blocked, so it costs no frame either.
            while (Hold && !_running && !_quitting)
            {
                PollOnce();
                if (!_running) System.Threading.Thread.Sleep(2);
            }
            return;
        }
        _poll += delta;
        if (_poll < 0.1) return;
        _poll = 0;
        PollOnce();
    }

    /// <summary>Starts the command line in cmd.txt when it is a new one.</summary>
    private void PollOnce()
    {
        string path = Path.Combine(_dir, "cmd.txt");
        if (!File.Exists(path)) return;
        string[] lines;
        try { lines = File.ReadAllLines(path); } catch { return; }
        if (lines.Length < 1 || lines[0] == _lastId) return;
        _lastId = lines[0];
        _ = Run(_lastId, string.Join(";", lines.Skip(1)));
    }

    private async STask Run(string id, string cmds)
    {
        _running = true;
        var sb = new StringBuilder();
        bool quit = false;
        foreach (string raw in cmds.Split(';'))
        {
            string c = raw.Trim();
            if (c.Length == 0) continue;
            if (c.Equals("quit", StringComparison.OrdinalIgnoreCase)) { quit = true; sb.AppendLine("bye"); break; }
            try
            {
                string r = await Do(c);
                if (!string.IsNullOrEmpty(r)) sb.AppendLine(r);
            }
            catch (Exception e) { sb.AppendLine($"error in '{c}': {e.Message} -- stopped here"); break; }
            finally { Busy = false; }
        }
        if (sb.Length == 0) sb.AppendLine("ok");
        string tmp = Path.Combine(_dir, "out.tmp"), dst = Path.Combine(_dir, "out.txt");
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try { File.WriteAllText(tmp, id + "\n" + sb); File.Move(tmp, dst, true); break; }
            catch { await Ms(100); }
        }
        _running = false;
        if (quit) { _quitting = true; OnQuit?.Invoke(); GetTree().Quit(); }
    }

    // ---- time -------------------------------------------------------------------------

    /// <summary>Lets N frames pass (the game runs while a command awaits this with <see cref="Busy"/> set).</summary>
    public async STask Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Game time in seconds: the sum of the frames' process delta since the server started.</summary>
    public double GameTime => _gameTime;

    /// <summary>Lets MS milliseconds of game time pass (real time normally; frames over the fps under Movie Maker).</summary>
    public async STask Ms(int ms)
    {
        double end = _gameTime + ms / 1000.0;
        while (_gameTime < end - 1e-9) await Frames(1);
    }

    // ---- input ------------------------------------------------------------------------

    private static readonly Dictionary<string, Key> Named = new()
    {
        ["space"] = Key.Space, ["esc"] = Key.Escape, ["escape"] = Key.Escape, ["enter"] = Key.Enter, ["return"] = Key.Enter,
        ["tab"] = Key.Tab, ["backspace"] = Key.Backspace, ["delete"] = Key.Delete, ["del"] = Key.Delete, ["insert"] = Key.Insert,
        ["up"] = Key.Up, ["down"] = Key.Down, ["left"] = Key.Left, ["right"] = Key.Right, ["home"] = Key.Home, ["end"] = Key.End,
        ["pgup"] = Key.Pageup, ["pageup"] = Key.Pageup, ["pgdn"] = Key.Pagedown, ["pagedown"] = Key.Pagedown,
        ["shift"] = Key.Shift, ["ctrl"] = Key.Ctrl, ["alt"] = Key.Alt,
        ["<"] = Key.Comma, [">"] = Key.Period, [","] = Key.Comma, ["."] = Key.Period, ["?"] = Key.Slash, ["/"] = Key.Slash,
        ["-"] = Key.Minus, ["="] = Key.Equal, ["+"] = Key.Equal, ["["] = Key.Bracketleft, ["]"] = Key.Bracketright,
    };

    /// <summary>A key by name: a letter, a digit, F1..F12 or one of the names in <see cref="BuiltIn"/>.</summary>
    public static Key KeyOf(string k)
    {
        string l = k.ToLowerInvariant();
        if (Named.TryGetValue(l, out Key named)) return named;
        if (l.Length == 1 && char.IsLetter(l[0])) return Key.A + (l[0] - 'a');
        if (l.Length == 1 && char.IsDigit(l[0])) return Key.Key0 + (l[0] - '0');
        if (l.Length >= 2 && l[0] == 'f' && int.TryParse(l[1..], out int f) && f is >= 1 and <= 12) return Key.F1 + (f - 1);
        throw new Exception($"unknown key '{k}'");
    }

    /// <summary>Taps (or holds) a key, as 'key' does: "ctrl+z", "shift+a", "F2", "<".</summary>
    public async STask TapKey(string k, int holdMs = 0)
    {
        bool ctrl = false, shift = false, alt = false;
        while (k.Length > 1 && k.Contains('+') && k != "+")
        {
            string mod = k[..k.IndexOf('+')].ToLowerInvariant();
            if (mod == "ctrl") ctrl = true; else if (mod == "shift") shift = true; else if (mod == "alt") alt = true; else break;
            k = k[(k.IndexOf('+') + 1)..];
        }
        Key key = KeyOf(k);
        shift |= k is "<" or ">" or "?" or "+" || (k.Length == 1 && char.IsUpper(k[0]));
        long unicode = ctrl || alt ? 0 : k.Length == 1 ? k[0] : k.Equals("space", StringComparison.OrdinalIgnoreCase) ? ' ' : 0;
        if (unicode != 0 && char.IsLetter((char)unicode) && !shift) unicode = char.ToLowerInvariant((char)unicode);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = true, ShiftPressed = shift, CtrlPressed = ctrl, AltPressed = alt, Device = Device });
        if (holdMs > 0) await Ms(holdMs); else await Frames(2);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false, ShiftPressed = shift, CtrlPressed = ctrl, AltPressed = alt, Device = Device });
        await Frames(2);
    }

    /// <summary>The window pixel of a layout pixel (the layout is stretched to fill a bigger or smaller window).</summary>
    private Vector2 Win(Vector2 layout) => GetTree().Root.GetFinalTransform() * layout;

    /// <summary>Moves the mouse to a layout position, with buttons held as given.</summary>
    public async STask MoveMouse(Vector2 to, MouseButtonMask mask = 0)
    {
        Vector2 from = _mouse;
        _mouse = to;
        Vector2 wt = Win(to);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = wt, GlobalPosition = wt, Relative = wt - Win(from), ButtonMask = mask, Device = Device });
        await Frames(1);
    }

    /// <summary>Presses or releases a mouse button at the mouse.</summary>
    public async STask MouseButtonAt(MouseButton b, bool down, int frames = 2)
    {
        MouseButtonMask mask = !down ? 0 : b switch { MouseButton.Right => MouseButtonMask.Right, MouseButton.Middle => MouseButtonMask.Middle, _ => MouseButtonMask.Left };
        Input.ParseInputEvent(new InputEventMouseButton { Position = Win(_mouse), GlobalPosition = Win(_mouse), ButtonIndex = b, Pressed = down, ButtonMask = mask, Device = Device });
        if (frames > 0) await Frames(frames);
    }

    /// <summary>Clicks at the mouse; with holdMs the button stays down that long (game time), else a couple of frames.</summary>
    public async STask Click(MouseButton b = MouseButton.Left, int holdMs = 0)
    {
        if (holdMs > 0)
        {
            await MouseButtonAt(b, true, 0);
            await Ms(holdMs);
            await MouseButtonAt(b, false, 0);
            return;
        }
        await MouseButtonAt(b, true);
        await MouseButtonAt(b, false);
    }

    /// <summary>
    /// Moves the mouse to a layout position over ms of game time, a motion event each frame: eased in and out
    /// (t &lt; .5 ? 4t³ : 1 - (-2t+2)³/2), along a straight line or, with a via point, a quadratic Bezier curve with
    /// it as the control point (the curve bends toward it but does not pass through it).
    /// </summary>
    public async STask Glide(Vector2 to, int ms, Vector2? via = null, MouseButtonMask mask = 0)
    {
        Vector2 from = _mouse;
        double start = _gameTime, end = start + ms / 1000.0;
        while (_gameTime < end - 1e-9)
        {
            double t = Math.Clamp((_gameTime - start) / (ms / 1000.0), 0, 1);
            float e = (float)(t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2);
            Vector2 p = via is Vector2 v ? from * ((1 - e) * (1 - e)) + v * (2 * (1 - e) * e) + to * (e * e) : from.Lerp(to, e);
            await MoveMouse(p, mask);
        }
        if (_mouse != to) await MoveMouse(to, mask);
    }

    /// <summary>Layout and window size in pixels: the layout is what Movie Maker records, the window what is on the desktop.</summary>
    private string SizeLine()
    {
        Vector2 lay = GetViewport().GetVisibleRect().Size;
        Vector2I win = GetTree().Root.Size;
        return $"size {lay.X:0} {lay.Y:0} {win.X} {win.Y}";
    }

    /// <summary>The rectangle (layout pixels) of a target: the game's hook first, then a button by its label.</summary>
    public Rect2 LocateTarget(string target)
    {
        Rect2? r = Locate?.Invoke(target);
        if (r == null)
        {
            BaseButton b = Buttons().FirstOrDefault(x => !x.Disabled && Label(x).Contains(target, StringComparison.OrdinalIgnoreCase));
            if (b != null) r = b.GetGlobalRect();
        }
        return r ?? throw new Exception($"nothing answers to \"{target}\"");
    }

    private async STask Drag(Vector2 from, Vector2 to, MouseButton b = MouseButton.Left, int steps = 6)
    {
        MouseButtonMask mask = b == MouseButton.Right ? MouseButtonMask.Right : MouseButtonMask.Left;
        await MoveMouse(from);
        await MouseButtonAt(b, true);
        for (int k = 1; k <= steps; k++) await MoveMouse(from.Lerp(to, k / (float)steps), mask);
        await MouseButtonAt(b, false);
    }

    private Vector2 Cell(string x, string y) =>
        (CellToScreen ?? throw new Exception("this game maps no cells to the screen (set AgentServer.CellToScreen)"))(Int(x), Int(y));

    // ---- the buttons on screen ----------------------------------------------------------

    /// <summary>Visible buttons on screen, dialogs and overlays (last in the tree) first.</summary>
    public List<BaseButton> Buttons()
    {
        var all = new List<BaseButton>();
        void Walk(Node n)
        {
            if (n is CanvasItem ci && !ci.IsVisibleInTree()) return;
            if (n is BaseButton b) all.Add(b);
            foreach (Node ch in n.GetChildren()) Walk(ch);
        }
        Walk(GetTree().Root);
        all.Reverse();
        Rect2 screen = GetViewport().GetVisibleRect();
        return all.Where(b => screen.Intersects(b.GetGlobalRect())).ToList();
    }

    /// <summary>What a button says: its text, or its tooltip, or its node name.</summary>
    public static string Label(BaseButton b) => b is Button bt && !string.IsNullOrEmpty(bt.Text) ? bt.Text : !string.IsNullOrEmpty(b.TooltipText) ? b.TooltipText : b.Name;

    private string ListUi()
    {
        var sb = new StringBuilder();
        foreach (BaseButton b in Enumerable.Reverse(Buttons()))
        {
            Vector2 c = b.GetGlobalRect().GetCenter();
            sb.AppendLine($"  [{Label(b)}] at {c.X:0},{c.Y:0}{(b.Disabled ? " (disabled)" : "")}{(b.ToggleMode && b.ButtonPressed ? " (on)" : "")}");
        }
        return sb.Length == 0 ? "no buttons on screen" : sb.ToString().TrimEnd();
    }

    private string Tree(Node at, int depth)
    {
        var sb = new StringBuilder();
        void Walk(Node n, int d)
        {
            string extra = n is CanvasItem ci && !ci.Visible ? " (hidden)" : n is Label l ? $" \"{Short(l.Text)}\"" : n is Button b && b.Text != "" ? $" [{Short(b.Text)}]" : "";
            sb.AppendLine($"{new string(' ', 2 * (d + 1))}{n.Name} ({n.GetClass()}){extra}");
            if (d + 1 >= depth) { if (n.GetChildCount() > 0) sb.AppendLine($"{new string(' ', 2 * (d + 2))}... {n.GetChildCount()} children"); return; }
            foreach (Node ch in n.GetChildren()) Walk(ch, d + 1);
        }
        Walk(at, 0);
        return sb.ToString().TrimEnd();
    }

    private static string Short(string s) => s.Length > 40 ? s[..40] + "..." : s.Replace('\n', ' ');

    // ---- the commands -------------------------------------------------------------------

    private async System.Threading.Tasks.Task<string> Do(string c)
    {
        string[] a = c.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string word = a[0].ToLowerInvariant(), rest = Rest(a);
        if (_commands.TryGetValue(word, out var custom)) return await custom.run(a);
        switch (word)
        {
            case "help": return Help;
            case "state":
                Busy = true; await Frames(2); Busy = false;
                return State?.Invoke() ?? $"scene: {GetTree().CurrentScene?.Name ?? GetTree().Root.GetChild(GetTree().Root.GetChildCount() - 1).Name}\n{ListUi()}";
            case "shot":
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string name = a.Length > 1 ? a[1] : "shot";
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_dir, name + ".png"));
                return $"saved agent/{name}.png";
            }
            case "ui": return ListUi();
            case "press":
            {
                BaseButton b = Buttons().FirstOrDefault(x => !x.Disabled && Label(x).Contains(rest, StringComparison.OrdinalIgnoreCase))
                    ?? throw new Exception($"no enabled button with '{rest}'");
                string label = Label(b);
                await MoveMouse(b.GetGlobalRect().GetCenter());
                Busy = true;
                await Click();
                await Frames(3);
                return $"pressed '{label}'";
            }
            case "type":
                foreach (char ch in rest)
                {
                    Key k = char.IsLetter(ch) ? Key.A + (char.ToLowerInvariant(ch) - 'a') : char.IsDigit(ch) ? Key.Key0 + (ch - '0') : ch == ' ' ? Key.Space : Key.None;
                    Input.ParseInputEvent(new InputEventKey { Unicode = ch, Keycode = k, Pressed = true, ShiftPressed = char.IsUpper(ch), Device = Device });
                    Input.ParseInputEvent(new InputEventKey { Unicode = ch, Keycode = k, Pressed = false, Device = Device });
                    await Frames(1);
                }
                return $"typed {rest.Length} characters";
            case "key": Busy = true; await TapKey(a[1], a.Length > 2 ? Int(a[2]) : 0); return null;
            case "mouse": await MoveMouse(new Vector2(Float(a[1]), Float(a[2])), _down ? MouseButtonMask.Left : 0); return null;
            case "click":
            {
                Busy = true;
                string btn = a.Length > 1 && !char.IsDigit(a[1][0]) ? a[1] : "left";
                int holdMs = a.Length > 1 && char.IsDigit(a[^1][0]) ? Int(a[^1]) : 0;
                await Click(btn == "right" ? MouseButton.Right : btn == "middle" ? MouseButton.Middle : MouseButton.Left, holdMs);
                return null;
            }
            case "size": return SizeLine();
            case "locate":
            {
                Rect2 r = LocateTarget(rest);
                return string.Create(CultureInfo.InvariantCulture, $"box {r.Position.X:0.##} {r.Position.Y:0.##} {r.Size.X:0.##} {r.Size.Y:0.##}");
            }
            case "glide":
                Busy = true;
                await Glide(new Vector2(Float(a[1]), Float(a[2])), Int(a[3]), a.Length > 5 ? new Vector2(Float(a[4]), Float(a[5])) : null, _down ? MouseButtonMask.Left : 0);
                return null;
            case "down": _down = true; await MouseButtonAt(MouseButton.Left, true); return null;
            case "up": _down = false; await MouseButtonAt(MouseButton.Left, false); return null;
            case "mdrag": Busy = true; await Drag(new Vector2(Float(a[1]), Float(a[2])), new Vector2(Float(a[3]), Float(a[4]))); return null;
            case "rdrag": Busy = true; await Drag(_mouse, _mouse + new Vector2(Float(a[1]), Float(a[2])), MouseButton.Right, 8); return null;
            case "wheel":
            {
                MouseButton b = a.Length > 1 && a[1] == "down" ? MouseButton.WheelDown : MouseButton.WheelUp;
                for (int k = 0; k < (a.Length > 2 ? Int(a[2]) : 1); k++) await Click(b);
                return null;
            }
            case "at": await MoveMouse(Cell(a[1], a[2]), _down ? MouseButtonMask.Left : 0); return null;
            case "drag": Busy = true; await Drag(Cell(a[1], a[2]), Cell(a[3], a[4])); return null;
            case "wait": Busy = true; await Ms(Int(a[1])); return null;
            case "frames": Busy = true; await Frames(Int(a[1])); return null;
            case "tree":
            {
                Node at = a.Length > 2 ? GetTree().Root.GetNodeOrNull(a[2]) ?? throw new Exception($"no node '{a[2]}'") : GetTree().Root;
                return Tree(at, a.Length > 1 ? Int(a[1]) : 3);
            }
            case "prop":
            {
                Node n = GetTree().Root.GetNodeOrNull(a[1]) ?? throw new Exception($"no node '{a[1]}'");
                return FormattableString.Invariant($"{a[1]}.{a[2]} = {n.Get(a[2])}");
            }
            case "perf":
            {
                int n = a.Length > 1 ? Int(a[1]) : 120;
                Busy = true;
                var frames = new List<double>();
                double proc = 0, draws = 0, objs = 0;
                ulong last = Time.GetTicksUsec();
                for (int k = 0; k < n; k++)
                {
                    await Frames(1);
                    ulong now = Time.GetTicksUsec();
                    frames.Add((now - last) / 1000.0);
                    last = now;
                    proc += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000;
                    draws += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
                    objs += Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
                }
                frames.Sort();
                return string.Create(CultureInfo.InvariantCulture, $"frame avg {frames.Average():0.0} ms, median {frames[n / 2]:0.0}, worst {frames[^1]:0.0} ({1000 / frames.Average():0} fps); ") +
                       string.Create(CultureInfo.InvariantCulture, $"process {proc / n:0.0} ms; draw calls {draws / n:0}, objects {objs / n:0}; nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0}");
            }
            case "time": return string.Create(CultureInfo.InvariantCulture, $"time {_gameTime:0.000}");
            case "hold": Hold = a.Length < 2 || a[1] != "off"; return $"hold {(Hold ? "on" : "off")}";
            case "freeze": Frozen = a.Length < 2 || a[1] != "off"; return $"freeze {(Frozen ? "on" : "off")}";
        }
        throw new Exception($"unknown command '{a[0]}' (try help)");
    }
}
