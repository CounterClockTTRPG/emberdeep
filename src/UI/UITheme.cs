using System;
using Godot;

namespace Emberdeep;

/// <summary>
/// The look of every screen in one place: the palette as named colours, the fonts, a Theme for the built-in controls,
/// and small builders (labels, headings, buttons, boxes) so screens stay short. Restyle the game here.
/// Fonts: put Title.ttf, Body.ttf and Mono.ttf (with their licences) in assets/fonts; until then Godot's own is used.
/// </summary>
public static class UITheme
{
    public static readonly Color Text = new(0.92f, 0.93f, 0.95f);
    public static readonly Color Dim = new(0.62f, 0.65f, 0.7f);
    public static readonly Color Faint = new(0.4f, 0.43f, 0.48f);
    public static readonly Color Accent = new(0.95f, 0.72f, 0.3f);
    public static readonly Color Good = new(0.5f, 0.85f, 0.55f);
    public static readonly Color Bad = new(0.95f, 0.4f, 0.38f);
    public static readonly Color PanelFill = new(0.09f, 0.1f, 0.12f, 0.95f);
    public static readonly Color PanelFill2 = new(0.13f, 0.14f, 0.17f, 0.97f);
    public static readonly Color Edge = new(0.25f, 0.28f, 0.33f);
    public static readonly Color EdgeLit = new(0.55f, 0.6f, 0.68f);

    private static Font _title, _body, _mono;
    private static Theme _theme;

    public static Font Title => _title ??= Load("Title.ttf");
    public static Font Body => _body ??= Load("Body.ttf");
    public static Font Mono => _mono ??= Load("Mono.ttf");

    /// <summary>A font from assets/fonts (in the project, or beside an exported build), or Godot's own.</summary>
    private static Font Load(string file)
    {
        string path = $"res://assets/fonts/{file}";
        if (!FileAccess.FileExists(path)) path = OS.GetExecutablePath().GetBaseDir() + $"/assets/fonts/{file}";
        if (!FileAccess.FileExists(path)) return ThemeDB.FallbackFont;
        return new FontFile { Data = FileAccess.GetFileAsBytes(path), Antialiasing = TextServer.FontAntialiasing.Gray };
    }

    public static StyleBoxFlat Box(Color fill, Color edge, int border = 1, int radius = 6, int pad = 10)
    {
        var s = new StyleBoxFlat { BgColor = fill, BorderColor = edge, CornerDetail = 6 };
        s.SetBorderWidthAll(border);
        s.SetCornerRadiusAll(radius);
        s.ContentMarginLeft = s.ContentMarginRight = pad;
        s.ContentMarginTop = s.ContentMarginBottom = Math.Max(3, pad - 4);
        return s;
    }

    public static Theme Theme
    {
        get
        {
            if (_theme != null) return _theme;
            var t = new Theme { DefaultFont = Body, DefaultFontSize = 18 };
            t.SetColor("font_color", "Label", Text);
            foreach (string b in new[] { "Button", "CheckBox", "OptionButton" })
            {
                t.SetColor("font_color", b, Text);
                t.SetColor("font_hover_color", b, Accent);
                t.SetColor("font_pressed_color", b, Accent);
                t.SetColor("font_disabled_color", b, Faint);
                t.SetStylebox("focus", b, new StyleBoxEmpty());
            }
            t.SetStylebox("normal", "Button", Box(PanelFill2, Edge));
            t.SetStylebox("hover", "Button", Box(new Color(0.18f, 0.19f, 0.23f), EdgeLit));
            t.SetStylebox("pressed", "Button", Box(new Color(0.24f, 0.2f, 0.12f), Accent));
            t.SetStylebox("disabled", "Button", Box(new Color(0.08f, 0.085f, 0.1f), new Color(0.18f, 0.2f, 0.23f)));
            t.SetStylebox("panel", "PanelContainer", Box(PanelFill, Edge, 1, 8, 14));
            t.SetStylebox("normal", "LineEdit", Box(new Color(0.05f, 0.055f, 0.065f), Edge));
            t.SetStylebox("focus", "LineEdit", Box(new Color(0.05f, 0.055f, 0.065f), Accent));
            t.SetColor("font_color", "TooltipLabel", Text);
            t.SetStylebox("panel", "TooltipPanel", Box(PanelFill2, EdgeLit, 1, 4, 8));
            return _theme = t;
        }
    }

    public static Label L(string text, int size = 18, Color? color = null, Font font = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is Color c) l.AddThemeColorOverride("font_color", c);
        if (font != null) l.AddThemeFontOverride("font", font);
        return l;
    }

    public static Label Heading(string text, int size = 30) => L(text, size, Accent, Title);

    /// <summary>A label that wraps at the given width.</summary>
    public static Label Wrap(string text, int size, Color color, float width)
    {
        Label l = L(text, size, color);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(width, 0);
        return l;
    }

    public static Button B(string text, Action onPress, int size = 18, string tip = null)
    {
        var b = new Button { Text = text, TooltipText = tip ?? "", FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", size);
        b.Pressed += () => { Sounds.Play("click"); onPress(); };
        return b;
    }

    public static VBoxContainer V(int gap = 8) { var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", gap); return v; }
    public static HBoxContainer H(int gap = 8) { var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", gap); return h; }

    /// <summary>Anchors and offsets a control in its parent. Call it after AddChild (anchoring an orphan does nothing).</summary>
    public static void Anchor(Control c, float l, float t, float r, float b, float ol = 0, float ot = 0, float or = 0, float ob = 0)
    {
        c.AnchorLeft = l; c.AnchorTop = t; c.AnchorRight = r; c.AnchorBottom = b;
        c.OffsetLeft = ol; c.OffsetTop = ot; c.OffsetRight = or; c.OffsetBottom = ob;
    }

    /// <summary>A control centred on screen (its size from its contents).</summary>
    public static CenterContainer Centred(Control inner)
    {
        var c = new CenterContainer();
        c.AddChild(inner);
        return c;
    }
}
