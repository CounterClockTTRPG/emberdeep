using System;
using System.Linq;
using Godot;

namespace Emberdeep;

/// <summary>
/// The title: the hero of the chosen slot stands as equipped on a dais between two braziers (a 3D stage behind
/// everything), and a quiet column on the left holds the name, the slots, the class choice for a new hero, and the
/// settings. Choosing a slot or a class puts that hero on the stage. Up and Down choose a slot, Enter continues.
/// </summary>
public partial class TitleScreen : Control
{
    /// <summary>The slot a new hero is being chosen for (0: none, the slots are shown).</summary>
    public int Choosing;
    private int _selected;
    private Cls _class = Cls.Wanderer;
    private TitleStage _stage;
    private Control _column;
    private Label _caption, _captionSub;
    private double _clock;

    public override void _Ready()
    {
        Theme = UITheme.Theme;
        _selected = Math.Clamp(Profile.Current.LastSlot, 1, Profile.Slots);
        var vc = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(vc);
        UITheme.Anchor(vc, 0, 0, 1, 1);
        var vp = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa2X, HandleInputLocally = false };
        vc.AddChild(vp);
        _stage = new TitleStage();
        vp.AddChild(_stage);
        // A shade over the left of the stage, so the column reads against it.
        var shade = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(shade);
        UITheme.Anchor(shade, 0, 0, 1, 1);
        shade.Draw += () =>
        {
            Vector2 s = shade.Size;
            for (int k = 0; k < 32; k++)
            {
                float t = k / 32f;
                shade.DrawRect(new Rect2(s.X * 0.55f * t, 0, s.X * 0.55f / 32 + 1, s.Y), new Color(UITheme.Soot, 0.92f * (1 - t * t)));
            }
            shade.DrawRect(new Rect2(0, s.Y - 90, s.X, 90), new Color(UITheme.Soot, 0.35f));
        };
        _caption = UITheme.L("", 30, UITheme.Bone, UITheme.Title);
        _captionSub = UITheme.L("", 17, UITheme.Ash);
        var cap = UITheme.V(0);
        cap.AddChild(_caption);
        cap.AddChild(_captionSub);
        _caption.HorizontalAlignment = _captionSub.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(cap);
        UITheme.Anchor(cap, 0.5f, 1, 1, 1, 0, -150, -40, -70);
        Build();
        Show();
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_column != null) _column.Modulate = new Color(1, 1, 1, (float)Math.Clamp((_clock - 0.15) / 0.6, 0, 1));
    }

    /// <summary>Put the right hero on the stage and say who it is.</summary>
    private void Show()
    {
        if (Choosing > 0)
        {
            _stage.Stand(Hero.New(_class), false);
            _caption.Text = _class switch { Cls.Wanderer => "The Wanderer", Cls.Rogue => "The Rogue", _ => "The Sorcerer" };
            _captionSub.Text = "Level 1, about to go down";
            return;
        }
        Game g = Profile.LoadGame(_selected);
        _stage.Stand(g?.Hero ?? Hero.New(Cls.Wanderer), g == null);
        _caption.Text = g == null ? "" : $"Level {g.Hero.Level} {g.Hero.Class}";
        _captionSub.Text = g == null ? "" : g.Won ? "Slayer of the Ashen Warden" : $"Last seen in {Data.Floor(g.Depth).Name}";
    }

    private void Build()
    {
        _column?.QueueFree();
        var col = UITheme.V(6);
        _column = col;
        AddChild(col);
        UITheme.Anchor(col, 0, 0, 0, 1, 112, 96, 112 + 520, -48);
        Label name = UITheme.L("Emberdeep", 104, UITheme.Bone, UITheme.Title);
        name.AddThemeColorOverride("font_shadow_color", new Color(UITheme.Ember, 0.35f));
        name.AddThemeConstantOverride("shadow_offset_x", 0);
        name.AddThemeConstantOverride("shadow_offset_y", 0);
        name.AddThemeConstantOverride("shadow_outline_size", 18);
        col.AddChild(name);
        col.AddChild(UITheme.Wrap("Below Emberwick the old graves go down, and down, to a fire that should have stayed asleep.", 20, UITheme.Ash, 470));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 44) });
        if (Choosing > 0) Classes(col);
        else Slots(col);
        col.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var settings = UITheme.H(26);
        settings.AddChild(Toggle("Sound", Profile.Current.Sound, on => { Profile.Current.Sound = on; Profile.Current.Apply(); }));
        settings.AddChild(Toggle("Music", Profile.Current.MusicOn, on => { Profile.Current.MusicOn = on; Sounds.ApplyMusic(); }));
        settings.AddChild(Toggle("Full screen", Profile.Current.Fullscreen, on => { Profile.Current.Fullscreen = on; Profile.Current.Apply(); }));
        settings.AddChild(UITheme.Flat("Quit", () => GetTree().Quit(), 17, UITheme.Ash));
        col.AddChild(settings);
    }

    private void Slots(VBoxContainer col)
    {
        for (int slot = 1; slot <= Profile.Slots; slot++)
        {
            int s = slot;
            Game g = Profile.LoadGame(s);
            string line = g == null ? "Empty" : $"{g.Hero.Class}, level {g.Hero.Level}";
            string sub = g == null ? "A new hero can begin here" : g.Won ? "The Warden is slain" : Data.Floor(g.Depth).Name;
            col.AddChild(new SlotRow($"Slot {s}", line, sub, s == _selected, () => { _selected = s; Build(); Show(); }));
        }
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 22) });
        var actions = UITheme.H(28);
        Game sel = Profile.LoadGame(_selected);
        if (sel != null)
        {
            Button go = UITheme.Flat("Continue", () => Root.Instance.Continue(_selected), 30, UITheme.Ember, UITheme.Title);
            actions.AddChild(go);
            actions.AddChild(UITheme.Flat("Start over", () => { Choosing = _selected; _class = sel.Hero.Class; Build(); Show(); }, 19, UITheme.Bone));
            actions.AddChild(UITheme.Flat("Clear", () => { Profile.DeleteSlot(_selected); Build(); Show(); }, 19, UITheme.Ash));
            CallDeferred(Control.MethodName.GrabFocus);
            go.CallDeferred(Control.MethodName.GrabFocus);
        }
        else
        {
            Button go = UITheme.Flat("Begin a new hero", () => { Choosing = _selected; Build(); Show(); }, 30, UITheme.Ember, UITheme.Title);
            actions.AddChild(go);
            go.CallDeferred(Control.MethodName.GrabFocus);
        }
        col.AddChild(actions);
    }

    private void Classes(VBoxContainer col)
    {
        col.AddChild(UITheme.L($"Who goes down from slot {Choosing}?", 26, UITheme.Bone, UITheme.Title));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        foreach (Cls c in Enum.GetValues<Cls>())
        {
            Cls cls = c;
            var (m, a, v, sp) = Data.StartAttrs(c);
            string name = c switch { Cls.Wanderer => "The Wanderer", Cls.Rogue => "The Rogue", _ => "The Sorcerer" };
            col.AddChild(new SlotRow(null, name, Data.Blurb(c) + $"\nMight {m}, agility {a}, vigour {v}, spirit {sp}", c == _class,
                () => { _class = cls; Build(); Show(); }, c));
        }
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 22) });
        var actions = UITheme.H(28);
        string who = _class switch { Cls.Wanderer => "the Wanderer", Cls.Rogue => "the Rogue", _ => "the Sorcerer" };
        Button go = UITheme.Flat($"Go down as {who}", () => Root.Instance.NewGame(Choosing, 0, _class), 30, UITheme.Ember, UITheme.Title);
        actions.AddChild(go);
        actions.AddChild(UITheme.Flat("Back", () => { Choosing = 0; Build(); Show(); }, 19, UITheme.Ash));
        col.AddChild(actions);
        go.CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode is Key.Up or Key.Down)
        {
            int step = k.Keycode == Key.Up ? -1 : 1;
            if (Choosing > 0) _class = (Cls)(((int)_class + step + 3) % 3);
            else _selected = (_selected - 1 + step + Profile.Slots) % Profile.Slots + 1;
            Build();
            Show();
            GetViewport().SetInputAsHandled();
        }
        if (k.Keycode == Key.Escape && Choosing > 0) { Choosing = 0; Build(); Show(); GetViewport().SetInputAsHandled(); }
    }

    private static CheckBox Toggle(string text, bool on, Action<bool> set)
    {
        var cb = new CheckBox { Text = text, ButtonPressed = on, FocusMode = FocusModeEnum.None };
        var none = new StyleBoxEmpty();
        foreach (string st in new[] { "normal", "hover", "pressed", "focus", "hover_pressed" }) cb.AddThemeStyleboxOverride(st, none);
        cb.AddThemeColorOverride("font_color", UITheme.Ash);
        cb.AddThemeColorOverride("font_pressed_color", UITheme.Bone);
        cb.AddThemeColorOverride("font_hover_color", UITheme.Ember);
        cb.AddThemeColorOverride("font_hover_pressed_color", UITheme.Ember);
        cb.AddThemeFontSizeOverride("font_size", 17);
        cb.Toggled += v => { set(v); Profile.Current.Save(); };
        return cb;
    }
}

/// <summary>A row in the title's column: a bar on the left (ember when chosen), a line, and a quieter line under it.</summary>
public partial class SlotRow : Control
{
    private readonly string _tag, _line, _sub;
    private readonly bool _chosen;
    private readonly Action _pick;
    private readonly Cls? _icons;
    private bool _hover;

    public SlotRow() { }

    public SlotRow(string tag, string line, string sub, bool chosen, Action pick, Cls? icons = null)
    {
        _tag = tag; _line = line; _sub = sub; _chosen = chosen; _pick = pick; _icons = icons;
        CustomMinimumSize = new Vector2(500, icons != null ? 118 : 70);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public override void _Draw()
    {
        Color bar = _chosen ? UITheme.Ember : _hover ? UITheme.EdgeLit : UITheme.Edge;
        DrawRect(new Rect2(0, 8, _chosen ? 4 : 2, Size.Y - 16), bar);
        if (_chosen) DrawRect(new Rect2(4, 8, Size.X - 4, Size.Y - 16), new Color(UITheme.Ember, 0.06f));
        float x = 22, y = 34;
        if (_tag != null)
        {
            DrawString(UITheme.Body, new Vector2(x, y), _tag, HorizontalAlignment.Left, -1, 17, UITheme.Ash);
            x += 74;
        }
        DrawString(UITheme.Title, new Vector2(x, y), _line, HorizontalAlignment.Left, -1, 28, _chosen || _hover ? UITheme.Bone : UITheme.Bone.Darkened(0.2f));
        if (_icons is Cls c)
        {
            float ix = Size.X - 7 * 26;
            foreach (SkillDef s in Data.SkillsOf(c))
            {
                if (Kit.Icon(s.Icon) is Texture2D t) DrawTextureRect(t, new Rect2(ix, 12, 22, 22), false, _chosen ? Colors.White : new Color(1, 1, 1, 0.45f));
                ix += 26;
            }
        }
        DrawMultilineString(UITheme.Body, new Vector2(x, y + 24), _sub, HorizontalAlignment.Left, Size.X - x - 8, 17, 4, UITheme.Ash);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { Sounds.Play("click"); _pick?.Invoke(); AcceptEvent(); }
    }
}

/// <summary>
/// The title's 3D stage: a stone dais between two braziers, broken pillars behind in the dark, embers rising, and the
/// hero standing on it as equipped, lit warm from the fires and cold from behind. The braziers flare up when it
/// opens and when the hero changes.
/// </summary>
public partial class TitleStage : Node3D
{
    private Figure3D _hero;
    private string _heroKey;
    private readonly OmniLight3D[] _fires = new OmniLight3D[2];
    private double _clock, _flare;

    public override void _Ready()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = UITheme.Soot,
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.45f, 0.4f, 0.42f), AmbientLightEnergy = 0.25f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic, GlowEnabled = true, GlowIntensity = 0.8f, GlowBloom = 0.08f,
            FogEnabled = true, FogLightColor = UITheme.Soot, FogDensity = 0.06f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        // The camera low and to the left, so the hero stands right of the middle of the screen, looking big.
        var cam = new Camera3D { Fov = 32, Current = true };
        AddChild(cam);
        cam.Position = new Vector3(-1.05f, 1.35f, 5.6f);
        cam.LookAt(new Vector3(-1.35f, 1.0f, 0), Vector3.Up);
        var stone = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.18f, 0.17f), Roughness = 0.95f };
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.6f, BottomRadius = 1.75f, Height = 0.28f, RadialSegments = 10 }, MaterialOverride = stone, Position = new Vector3(0, -0.14f, 0) });
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(40, 40) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.065f, 0.06f), Roughness = 1 }, Position = new Vector3(0, -0.28f, 0) });
        // Everything stands behind the hero and to the right, so the column on the left has only dark behind it.
        Place("pillar", 3.4f, new Vector3(-0.9f, -0.28f, -4.2f), 0.4f);
        Place("pillar", 3.4f, new Vector3(2.5f, -0.28f, -3.4f), 2.1f);
        Place("tomb", 2.2f, new Vector3(0.9f, -0.28f, -6f), 0.2f);
        for (int k = 0; k < 2; k++)
        {
            float x = k == 0 ? -1.25f : 1.5f;
            Place("brazier", 1.1f, new Vector3(x, -0.28f, -0.6f), k * 1.3f);
            _fires[k] = new OmniLight3D { LightColor = new Color(1f, 0.52f, 0.2f), LightEnergy = 0, OmniRange = 7, Position = new Vector3(x, 1.15f, -0.6f), ShadowEnabled = true };
            AddChild(_fires[k]);
        }
        // Cold light from behind and above: a rim that lifts the hero off the dark.
        var rim = new OmniLight3D { LightColor = new Color(0.45f, 0.55f, 0.85f), LightEnergy = 2.2f, OmniRange = 6, Position = new Vector3(0.6f, 2.8f, -1.8f) };
        AddChild(rim);
        AddChild(Embers());
        _flare = 0;
    }

    private void Place(string model, float size, Vector3 at, float turn)
    {
        Node3D n = Kit.Prop(model, size);
        if (n == null) return;
        n.Position = at;
        n.Rotation = new Vector3(0, turn, 0);
        AddChild(n);
    }

    private static CpuParticles3D Embers()
    {
        var glow = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.55f, 0.2f), EmissionEnabled = true,
            Emission = new Color(1f, 0.5f, 0.15f), EmissionEnergyMultiplier = 3, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        return new CpuParticles3D
        {
            Amount = 60, Lifetime = 5, Preprocess = 5, Mesh = new QuadMesh { Size = new Vector2(0.03f, 0.03f), Material = glow },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(3, 0.2f, 1.5f), Position = new Vector3(0, 0.6f, -0.6f),
            Direction = Vector3.Up, Spread = 25, Gravity = new Vector3(0.05f, 0.18f, 0), InitialVelocityMin = 0.1f, InitialVelocityMax = 0.4f,
            ScaleAmountMin = 0.5f, ScaleAmountMax = 1.4f,
        };
    }

    /// <summary>Put a hero on the dais, as equipped. A ghost (an empty slot) stands dim, as a hint of who could come.</summary>
    public void Stand(Hero h, bool ghost)
    {
        string key = $"{h.Class}|{h.Weapon?.Base}|{h.Equip[(int)Slot.Shield]?.Base}|{ghost}";
        if (key == _heroKey) return;
        _heroKey = key;
        _hero?.QueueFree();
        _hero = new Figure3D(Data.Body(h.Class), 1.8f) { Rotation = new Vector3(0, 0.38f, 0) };
        AddChild(_hero);
        World3D.Dress(_hero, h);
        _hero.Play(h.Weapon == null ? "idle" : h.S.Ranged ? "aim" : "ready", 0.8f, false, "idle");
        if (ghost) _hero.Tint(new Color(0.55f, 0.5f, 0.5f), 0.55f);
        _flare = 0;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        _flare += delta;
        // The fires catch over a second and a half when the stage opens or a new hero steps up, then flicker.
        float rise = (float)Math.Clamp(_flare / 1.4, 0, 1);
        rise = 1 - (1 - rise) * (1 - rise);
        for (int k = 0; k < 2; k++)
        {
            if (_fires[k] == null) continue;
            float flick = 0.88f + 0.12f * Mathf.Sin((float)_clock * (8.3f + k) + k * 2) * Mathf.Sin((float)_clock * 4.7f + k);
            _fires[k].LightEnergy = 2.6f * rise * flick;
        }
    }
}
