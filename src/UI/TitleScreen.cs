using Godot;

namespace Emberdeep;

/// <summary>The title: the slots (continue, new, clear), the choice of a class for a new hero, the settings, quit.</summary>
public partial class TitleScreen : Control
{
    /// <summary>The slot a new hero is being chosen for (0: none, the slots are shown).</summary>
    public int Choosing;

    public override void _Ready()
    {
        Theme = UITheme.Theme;
        var bg = new ColorRect { Color = new Color(0.035f, 0.03f, 0.03f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(bg);
        UITheme.Anchor(bg, 0, 0, 1, 1);
        var glow = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(glow);
        UITheme.Anchor(glow, 0, 0, 1, 1);
        glow.Draw += () =>
        {
            // An ember glow rising from the bottom of the screen.
            Vector2 s = glow.Size;
            for (int k = 0; k < 24; k++)
            {
                float t = k / 24f;
                glow.DrawRect(new Rect2(0, s.Y * (0.55f + 0.45f * t), s.X, s.Y * 0.45f / 24 + 1), new Color(0.6f, 0.18f, 0.05f, 0.012f + 0.03f * t * t));
            }
        };
        Build();
    }

    private void Build()
    {
        foreach (Node n in GetChildren()) if (n is CenterContainer) n.QueueFree();
        var box = UITheme.V(14);
        box.AddChild(UITheme.Heading("EMBERDEEP", 72));
        box.AddChild(UITheme.L("Below the village of Emberwick the old graves go down, and down, to a fire that should have stayed asleep.", 17, UITheme.Dim));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
        if (Choosing > 0) Classes(box);
        else Slots(box);
        CenterContainer c = UITheme.Centred(box);
        AddChild(c);
        UITheme.Anchor(c, 0, 0, 1, 1);
    }

    private void Slots(VBoxContainer box)
    {
        for (int slot = 1; slot <= Profile.Slots; slot++)
        {
            int s = slot;
            string peek = Profile.Peek(s);
            var panel = new PanelContainer { CustomMinimumSize = new Vector2(640, 0) };
            var row = UITheme.H(10);
            var who = UITheme.V(2);
            who.AddChild(UITheme.L($"Slot {s}", 20, UITheme.Accent));
            who.AddChild(UITheme.L(peek ?? "empty", 15, peek == null ? UITheme.Faint : UITheme.Dim));
            who.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(who);
            if (peek != null) row.AddChild(UITheme.B("Continue", () => Root.Instance.Continue(s)));
            row.AddChild(UITheme.B(peek == null ? "New hero" : "Start over", () => { Choosing = s; Build(); }));
            if (peek != null) row.AddChild(UITheme.B("Clear", () => { Profile.DeleteSlot(s); Root.Instance.ShowTitle(); }, 15));
            panel.AddChild(row);
            box.AddChild(panel);
        }
        var settings = UITheme.H(18);
        settings.AddChild(Toggle("Sound", Profile.Current.Sound, on => { Profile.Current.Sound = on; Profile.Current.Apply(); }));
        settings.AddChild(Toggle("Music", Profile.Current.MusicOn, on => { Profile.Current.MusicOn = on; Sounds.ApplyMusic(); }));
        settings.AddChild(Toggle("Full screen (F11)", Profile.Current.Fullscreen, on => { Profile.Current.Fullscreen = on; Profile.Current.Apply(); }));
        box.AddChild(settings);
        box.AddChild(UITheme.L("Left click: walk, fight, pick up, talk.  Right click: your skill.  Q-T: choose a skill.  1-4: the belt.  I C S: pack, character, skills.  Tab: map.", 14, UITheme.Faint));
        box.AddChild(UITheme.B("Quit", () => GetTree().Quit()));
    }

    private void Classes(VBoxContainer box)
    {
        box.AddChild(UITheme.L($"Who goes down? (slot {Choosing})", 22, UITheme.Text));
        var row = UITheme.H(16);
        foreach (Cls c in System.Enum.GetValues<Cls>())
        {
            Cls cls = c;
            var card = new PanelContainer { CustomMinimumSize = new Vector2(300, 330) };
            var col = UITheme.V(8);
            col.AddChild(UITheme.L(c switch { Cls.Wanderer => "The Wanderer", Cls.Rogue => "The Rogue", _ => "The Sorcerer" }, 26, UITheme.Accent, UITheme.Title));
            var icons = UITheme.H(4);
            foreach (SkillDef s in Data.SkillsOf(c))
                icons.AddChild(new TextureRect { Texture = Kit.Icon(s.Icon), CustomMinimumSize = new Vector2(36, 36), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TooltipText = s.Name });
            col.AddChild(icons);
            col.AddChild(UITheme.Wrap(Data.Blurb(c), 15, UITheme.Dim, 270));
            var (m, a, v, sp) = Data.StartAttrs(c);
            col.AddChild(UITheme.L($"Might {m}  Agility {a}\nVigour {v}  Spirit {sp}", 14, UITheme.Text));
            var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
            col.AddChild(spacer);
            col.AddChild(UITheme.B("Begin", () => Root.Instance.NewGame(Choosing, 0, cls)));
            card.AddChild(col);
            row.AddChild(card);
        }
        box.AddChild(row);
        box.AddChild(UITheme.B("Back", () => { Choosing = 0; Build(); }));
    }

    private static CheckBox Toggle(string text, bool on, System.Action<bool> set)
    {
        var cb = new CheckBox { Text = text, ButtonPressed = on, FocusMode = FocusModeEnum.None };
        cb.Toggled += v => { set(v); Profile.Current.Save(); };
        return cb;
    }
}
