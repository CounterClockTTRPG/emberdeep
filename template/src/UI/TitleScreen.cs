using Godot;

namespace Starter;

/// <summary>The title: the slots (continue, new, clear), the settings, quit.</summary>
public partial class TitleScreen : Control
{
    public override void _Ready()
    {
        Theme = UITheme.Theme;
        var bg = new ColorRect { Color = new Color(0.06f, 0.065f, 0.075f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(bg);
        UITheme.Anchor(bg, 0, 0, 1, 1);
        var box = UITheme.V(14);
        box.AddChild(UITheme.Heading(ProjectSettings.GetSetting("application/config/name").AsString(), 64));
        box.AddChild(UITheme.L(ProjectSettings.GetSetting("application/config/description").AsString(), 18, UITheme.Dim));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
        for (int slot = 1; slot <= Profile.Slots; slot++)
        {
            int s = slot;
            string peek = Profile.Peek(s);
            var panel = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) };
            var row = UITheme.H(10);
            var who = UITheme.V(2);
            who.AddChild(UITheme.L($"Slot {s}", 20, UITheme.Accent));
            who.AddChild(UITheme.L(peek ?? "empty", 15, peek == null ? UITheme.Faint : UITheme.Dim));
            who.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(who);
            if (peek != null) row.AddChild(UITheme.B("Continue", () => Root.Instance.Continue(s)));
            row.AddChild(UITheme.B(peek == null ? "New game" : "Start over", () => Root.Instance.NewGame(s)));
            if (peek != null) row.AddChild(UITheme.B("Clear", () => { Profile.DeleteSlot(s); Root.Instance.ShowTitle(); }, 15));
            panel.AddChild(row);
            box.AddChild(panel);
        }
        var settings = UITheme.H(18);
        settings.AddChild(Toggle("Sound", Profile.Current.Sound, on => { Profile.Current.Sound = on; Profile.Current.Apply(); }));
        settings.AddChild(Toggle("Music", Profile.Current.MusicOn, on => { Profile.Current.MusicOn = on; Sounds.ApplyMusic(); }));
        settings.AddChild(Toggle("Full screen (F11)", Profile.Current.Fullscreen, on => { Profile.Current.Fullscreen = on; Profile.Current.Apply(); }));
        box.AddChild(settings);
        box.AddChild(UITheme.B("Quit", () => GetTree().Quit()));
        CenterContainer c = UITheme.Centred(box);
        AddChild(c);
        UITheme.Anchor(c, 0, 0, 1, 1);
    }

    private static CheckBox Toggle(string text, bool on, System.Action<bool> set)
    {
        var cb = new CheckBox { Text = text, ButtonPressed = on, FocusMode = FocusModeEnum.None };
        cb.Toggled += v => { set(v); Profile.Current.Save(); };
        return cb;
    }
}
