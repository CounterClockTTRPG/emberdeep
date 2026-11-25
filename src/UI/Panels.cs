using System;
using System.Linq;
using Godot;

namespace Emberdeep;

/// <summary>A square holding an item: its icon on a rarity-coloured frame, a count, a key; a tooltip with every line.</summary>
public partial class ItemSlot : Control
{
    public Item Item;
    public string Key, Empty;
    public Action Clicked, RightClicked;
    /// <summary>Picked up (a gem waiting for a socket): drawn with an ember frame.</summary>
    public bool Picked;
    /// <summary>A last line for the tooltip (what it sells for, what it costs).</summary>
    public Func<Item, string> Extra;
    public Game Game;

    public override void _Ready()
    {
        TooltipText = Item != null ? Item.Name : Empty ?? "";
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        Color frame = Item == null ? UITheme.Edge : Item.B?.Stacks == true ? UITheme.Edge : World3D.RarityColour(Item) * 0.8f;
        DrawRect(r, new Color(0.07f, 0.06f, 0.06f, 0.95f));
        if (Item != null && Item.Rarity >= Rarity.Magic) DrawRect(r.Grow(-2), World3D.RarityColour(Item) * new Color(1, 1, 1, 0.1f));
        if (Item != null && Game != null && Item.B?.Wearable == true && Item.Requires > Game.Hero.Level) DrawRect(r.Grow(-2), new Color(0.6f, 0.05f, 0.05f, 0.35f));
        Texture2D icon = Kit.Icon(Item?.B?.Icon);
        if (icon != null) DrawTextureRect(icon, r.Grow(-4), false);
        else if (Item == null && Empty != null) DrawString(UITheme.Body, new Vector2(0, Size.Y / 2 + 5), Empty, HorizontalAlignment.Center, Size.X, 12, UITheme.Faint);
        DrawRect(r, frame, false, 2);
        if (Item != null && Item.Count > 1) DrawString(UITheme.Body, new Vector2(4, Size.Y - 5), Item.Count.ToString(), HorizontalAlignment.Left, -1, 14, UITheme.Text);
        if (Item != null && Item.Sockets > 0)
        {
            // A dot a socket along the bottom: the gem's colour, or hollow when empty.
            float d = Math.Min(9, (Size.X - 8) / Item.Sockets);
            for (int k = 0; k < Item.Sockets; k++)
            {
                var c = new Vector2(Size.X / 2 + (k - (Item.Sockets - 1) / 2f) * d, Size.Y - 7);
                if (k < Item.Gems.Count) DrawCircle(c, 3.2f, GemColour(Item.GemOf(Item.Gems[k]).id));
                else DrawArc(c, 3, 0, Mathf.Tau, 12, UITheme.Bone, 1.2f);
            }
        }
        if (Picked) DrawRect(r.Grow(-1), UITheme.Ember, false, 3);
        if (Key != null) DrawString(UITheme.Body, new Vector2(Size.X - 12, 14), Key, HorizontalAlignment.Left, -1, 12, UITheme.Dim);
    }

    public static Color GemColour(string id) => id switch
    {
        "ruby" => new Color(0.9f, 0.15f, 0.15f), "sapphire" => new Color(0.25f, 0.45f, 1f), "topaz" => new Color(1f, 0.8f, 0.2f),
        "emerald" => new Color(0.2f, 0.8f, 0.35f), "amethyst" => new Color(0.7f, 0.3f, 0.9f), "diamond" => new Color(0.95f, 0.95f, 1f),
        _ => new Color(0.5f, 0.05f, 0.08f),
    };

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } mb) return;
        if (mb.ButtonIndex == MouseButton.Left) { Clicked?.Invoke(); AcceptEvent(); }
        if (mb.ButtonIndex == MouseButton.Right) { RightClicked?.Invoke(); AcceptEvent(); }
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Item == null ? null : Tooltip(Item, Extra?.Invoke(Item), Game);

    /// <summary>An item's tooltip: the name in its rarity's colour, the base, the affixes, a last line.</summary>
    public static Control Tooltip(Item it, string extra, Game g)
    {
        var box = UITheme.V(2);
        box.AddChild(UITheme.L(it.Title, 18, World3D.RarityColour(it)));
        if (it.Rarity >= Rarity.Rare || it.Rarity == Rarity.Magic && it.Name != it.B.Name) box.AddChild(UITheme.L(it.B.Name, 14, UITheme.Dim));
        foreach (string line in it.Lines())
        {
            bool bad = line.StartsWith("Requires") && g != null && it.Requires > g.Hero.Level;
            bool affix = it.Mods.Any(m => Data.Describe(m.Stat, m.Value) == line);
            box.AddChild(UITheme.L(line, 15, bad ? UITheme.Bad : affix ? new Color(0.55f, 0.65f, 1f) : UITheme.Text));
        }
        if (extra != null) box.AddChild(UITheme.L(extra, 14, UITheme.Accent));
        return box;
    }
}

/// <summary>A skill's icon: dim when not learned, a frame when it is on the right button, a key in the corner.</summary>
public partial class SkillIcon : Control
{
    private readonly string _skill;
    private readonly bool _known;
    private readonly Game _game;
    public string Key, Tip;
    public bool Chosen;
    public Action Clicked;

    public SkillIcon() { }

    public SkillIcon(string skill, bool known, Game g) { _skill = skill; _known = known; _game = g; }

    public override void _Ready()
    {
        TooltipText = Tip ?? GameScreen.SkillName(_skill);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, new Color(0.07f, 0.06f, 0.06f));
        string icon = _skill == "attack" || _skill == null ? _game?.Hero.Weapon?.B.Icon ?? "gauntlets" : Data.Skill(_skill)?.Icon;
        Texture2D t = Kit.Icon(icon);
        if (t != null) DrawTextureRect(t, r.Grow(-3), false, _known ? Colors.White : new Color(0.3f, 0.3f, 0.3f));
        DrawRect(r, Chosen ? UITheme.Accent : UITheme.Edge, false, Chosen ? 3 : 1);
        if (Key != null) DrawString(UITheme.Body, new Vector2(4, 15), Key, HorizontalAlignment.Left, -1, 13, UITheme.Text);
        if (_known && _skill != "attack" && _skill != null && Data.Skill(_skill) is SkillDef s && !s.Passive && _game != null)
            DrawString(UITheme.Body, new Vector2(0, Size.Y - 4), Data.ManaCost(s, _game.Hero.BaseRank(_skill)).ToString(), HorizontalAlignment.Right, Size.X - 4, 12, new Color(0.5f, 0.65f, 1f));
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { Clicked?.Invoke(); AcceptEvent(); }
    }
}

/// <summary>
/// A button of the band: an icon in an iron-rimmed frame (its own UI icon once assetgen has made it, an item's until
/// then), lit while its panel is open, with an ember mark when there are points to spend. Its name shows on hover.
/// </summary>
public partial class BandButton : Control
{
    private readonly string _icon, _fallback;
    private readonly Action _press;
    public bool Badge, Lit;
    private bool _hover;

    public BandButton() { }

    public BandButton(string icon, string fallback, string tip, Action press)
    {
        _icon = icon; _fallback = fallback; _press = press;
        TooltipText = tip;
        CustomMinimumSize = new Vector2(50, 50);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        var sb = new StyleBoxFlat { BgColor = UITheme.Soot, BorderColor = Lit ? UITheme.Ember : _hover ? UITheme.EdgeLit : UITheme.Edge };
        sb.SetBorderWidthAll(Lit ? 2 : 1);
        sb.SetCornerRadiusAll(6);
        DrawStyleBox(sb, r);
        Texture2D t = Kit.Icon(_icon) ?? Kit.Icon(_fallback);
        if (t != null) DrawTextureRect(t, r.Grow(-5), false, _hover || Lit ? Colors.White : new Color(0.85f, 0.82f, 0.78f));
        if (Badge)
        {
            DrawCircle(new Vector2(Size.X - 7, 7), 6, UITheme.Ember);
            DrawString(UITheme.Body, new Vector2(Size.X - 12, 12), "+", HorizontalAlignment.Center, 10, 13, UITheme.Soot);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { Sounds.Play("click"); _press?.Invoke(); AcceptEvent(); }
    }
}

/// <summary>The panels share a frame: a heading, a close button, a body.</summary>
public abstract partial class GamePanel : PanelContainer
{
    protected readonly GameScreen S;
    protected Game G => S.Game;
    protected VBoxContainer Body;

    protected GamePanel() { }

    protected GamePanel(GameScreen s, string title, string where)
    {
        S = s;
        Theme = UITheme.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        var outer = UITheme.V(10);
        AddChild(outer);
        var top = UITheme.H(8);
        Label h = UITheme.Heading(title, 26);
        h.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(h);
        top.AddChild(UITheme.B("x", () => s.Open(where, ""), 15));
        outer.AddChild(top);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        outer.AddChild(scroll);
        Body = UITheme.V(8);
        Body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(Body);
    }

    protected static Label Line(string text, Color? c = null, int size = 16) => UITheme.L(text, size, c ?? UITheme.Text);

    /// <summary>An order's result: refresh on success, say why not otherwise.</summary>
    protected void Done(string why)
    {
        if (why != null) { S.Say(why, UITheme.Bad); Sounds.Play("nomana", 0.4f); }
        S.Refresh();
    }
}

public partial class InventoryPanel : GamePanel
{
    public InventoryPanel() { }

    public InventoryPanel(GameScreen s) : base(s, "Inventory", "right")
    {
        Hero h = G.Hero;
        string trade = s.Trading;
        Body.AddChild(Line(trade switch
        {
            "smith" or "healer" => "Click a thing in your pack to sell it.",
            "stash" => "Click a thing in your pack to put it in the stash.",
            _ => "Click to wear or drink; right-click to drop. Click what you wear to take it off. A gem: click it, then an item with an empty socket.",
        }, UITheme.Dim, 14));
        var doll = new GridContainer { Columns = 5 };
        doll.AddThemeConstantOverride("h_separation", 6);
        doll.AddThemeConstantOverride("v_separation", 6);
        foreach (Slot sl in new[] { Slot.Weapon, Slot.Head, Slot.Body, Slot.Shield, Slot.Amulet, Slot.Hands, Slot.Feet, Slot.Ring1, Slot.Ring2 })
        {
            Slot here = sl;
            doll.AddChild(new ItemSlot
            {
                Item = h.Equip[(int)sl], Empty = sl.ToString().Replace("1", "").Replace("2", ""), Game = G, CustomMinimumSize = new Vector2(72, 72),
                Clicked = () =>
                {
                    if (h.Equip[(int)here] == null) return;
                    // A gem picked up goes into what is worn; otherwise a click takes it off.
                    if (s.GemPick >= 0) { int gp = s.GemPick; s.GemPick = -1; Done(G.Socket(gp, -1, here)); }
                    else Done(h.Unequip(here));
                },
            });
        }
        Body.AddChild(doll);
        var pack = new GridContainer { Columns = 10 };
        pack.AddThemeConstantOverride("h_separation", 3);
        pack.AddThemeConstantOverride("v_separation", 3);
        for (int k = 0; k < h.Pack.Length; k++)
        {
            int place = k;
            pack.AddChild(new ItemSlot
            {
                Item = h.Pack[k], Game = G, CustomMinimumSize = new Vector2(53, 53), Picked = s.GemPick == k,
                Extra = it => trade is "smith" or "healer" ? $"Sells for {it.Worth} gold" : it.B.Wearable ? Compare(it) : null,
                Clicked = () =>
                {
                    if (h.Pack[place] == null) return;
                    // Gems: click one to pick it up, then the item to set it in (the same gem again: put it down).
                    if (trade == null && s.GemPick >= 0)
                    {
                        int gp = s.GemPick;
                        s.GemPick = -1;
                        if (gp != place) Done(G.Socket(gp, place)); else S.Refresh();
                        return;
                    }
                    if (trade == null && h.Pack[place].B?.Kind == Kind.Gem)
                    {
                        s.GemPick = place;
                        S.Say($"Now click an item with an empty socket for the {h.Pack[place].Name}.", UITheme.Dim);
                        S.Refresh();
                        return;
                    }
                    if (trade is "smith" or "healer") Done(G.Sell(place));
                    else if (trade == "stash") Done(G.ToStash(place));
                    else Done(G.UsePack(place));
                },
                RightClicked = () => { if (h.Pack[place] != null && trade == null) Done(G.Discard(place)); },
            });
        }
        Body.AddChild(pack);
        Body.AddChild(Line($"{h.Gold} gold", new Color(0.95f, 0.8f, 0.35f)));
    }

    /// <summary>What wearing it would change, in a word or two.</summary>
    private string Compare(Item it)
    {
        Slot? sl = G.Hero.SlotFor(it);
        Item worn = sl == null ? null : G.Hero.Equip[(int)sl];
        if (worn == null) return "Nothing worn there";
        if (it.B.Weapon && worn.B.Weapon)
        {
            double a = (it.Damage.lo + it.Damage.hi) / 2.0, b = (worn.Damage.lo + worn.Damage.hi) / 2.0;
            return $"Damage {(a >= b ? "+" : "")}{a - b:0.#} against {worn.Name}";
        }
        if (it.B.Armour > 0) return $"Armour {(it.Armour >= worn.Armour ? "+" : "")}{it.Armour - worn.Armour} against {worn.Name}";
        return $"Instead of {worn.Name}";
    }
}

public partial class CharacterPanel : GamePanel
{
    public CharacterPanel() { }

    public CharacterPanel(GameScreen s) : base(s, "Character", "left")
    {
        Hero h = G.Hero;
        Stats st = h.S;
        Body.AddChild(Line(h.Name == h.Class.ToString() ? $"Level {h.Level} {h.Class}" : $"{h.Name}, level {h.Level} {h.Class}", UITheme.Accent, 18));
        Body.AddChild(Line(h.Level >= Data.MaxLevel ? "The highest level." : $"Experience {h.Xp} / {Data.XpToNext(h.Level)}", UITheme.Dim, 14));
        Body.AddChild(Line(h.AttrPoints > 0 ? $"{h.AttrPoints} points to spend" : "No points to spend", h.AttrPoints > 0 ? UITheme.Good : UITheme.Faint, 15));
        foreach (var (name, val, about) in new[]
                 {
                     ("Might", st.Might, "melee damage"), ("Agility", st.Agility, "accuracy, armour, bow damage"),
                     ("Vigour", st.Vigour, "life"), ("Spirit", st.Spirit, "mana, spell damage"),
                 })
        {
            var row = UITheme.H(8);
            Label l = Line($"{name} {val}", UITheme.Text, 17);
            l.CustomMinimumSize = new Vector2(130, 0);
            row.AddChild(l);
            Label a = Line(about, UITheme.Dim, 13);
            a.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(a);
            string attr = name;
            Button plus = UITheme.B("+", () => Done(h.Raise(attr)), 15);
            plus.Disabled = h.AttrPoints <= 0;
            row.AddChild(plus);
            Body.AddChild(row);
        }
        Body.AddChild(new HSeparator());
        string[] lines =
        {
            $"Life {Math.Round(st.MaxLife)}    Mana {Math.Round(st.MaxMana)}",
            $"Damage {st.DMin:0}-{st.DMax:0}{(st.FireMax > 0 ? $"  +{st.FireMin:0}-{st.FireMax:0} fire" : "")}{(st.ColdMax > 0 ? $"  +{st.ColdMin:0}-{st.ColdMax:0} cold" : "")}{(st.LightMax > 0 ? $"  +{st.LightMin:0}-{st.LightMax:0} lightning" : "")}",
            $"Attacks a second {1 / st.AttackTime:0.0}    Casts a second {1 / st.CastTime:0.0}",
            $"Accuracy {st.Accuracy:0}    Critical {st.Crit:0}%",
            $"Armour {st.Armour:0}    Block {st.Block:0}%    Dodge {st.Evade:0}%",
            $"Spell damage +{st.SpellPct:0}%",
            $"Resist fire {st.FireRes}%   cold {st.ColdRes}%   lightning {st.LightRes}%",
            $"Life a second {st.LifeRegen:0.0}    Mana a second {st.ManaRegen:0.0}",
            $"Better items {st.MagicFind}%    More gold {st.GoldFind}%    Walk {st.MoveSpeed:0.0} m/s",
            $"Foes slain {h.Kills}    Deaths {h.Deaths}    Deepest floor {h.Deepest}",
        };
        foreach (string line in lines) Body.AddChild(Line(line, UITheme.Text, 15));
        Body.AddChild(new HSeparator());
        Body.AddChild(Line("Quests", UITheme.Accent, 17));
        foreach (var q in Data.Quests)
        {
            int state = h.Quests.TryGetValue(q.id, out int v) ? v : 0;
            if (state == 0) continue;
            Body.AddChild(Line($"{q.name}: {state switch { 1 => "under way", 2 => "done, see the elder", _ => "finished" }}", state >= 3 ? UITheme.Faint : UITheme.Text, 15));
        }
        if (h.Quests.Count == 0) Body.AddChild(Line("Talk to the elder by the waystone.", UITheme.Dim, 14));
    }
}

public partial class SkillsPanel : GamePanel
{
    public SkillsPanel() { }

    public SkillsPanel(GameScreen s) : base(s, "Skills", "left")
    {
        Hero h = G.Hero;
        Body.AddChild(Line(h.SkillPoints > 0 ? $"{h.SkillPoints} skill points to spend" : "No skill points to spend", h.SkillPoints > 0 ? UITheme.Good : UITheme.Faint, 15));
        Body.AddChild(Line("Click an icon to put the skill on the right mouse button (or press its key).", UITheme.Dim, 13));
        foreach (SkillDef sk in Data.SkillsOf(h.Class))
        {
            int rank = h.Rank(sk.Id), baseRank = h.BaseRank(sk.Id);
            var row = UITheme.H(10);
            string id = sk.Id;
            row.AddChild(new SkillIcon(sk.Id, rank > 0, G)
            {
                CustomMinimumSize = new Vector2(56, 56), Chosen = h.Right == sk.Id,
                Clicked = () => { if (!sk.Passive && h.Rank(id) > 0) { h.Right = id; S.Refresh(); } },
                Tip = sk.Passive ? $"{sk.Name} (always at work)" : sk.Name,
            });
            var col = UITheme.V(1);
            col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            col.AddChild(Line($"{sk.Name}{(rank > 0 ? $"  rank {rank}" : "")}{(sk.Passive ? "  (passive)" : $"  mana {Data.ManaCost(sk, Math.Max(1, baseRank))}")}", rank > 0 ? UITheme.Text : UITheme.Dim, 16));
            col.AddChild(UITheme.Wrap(sk.Text, 13, UITheme.Dim, 300));
            if (rank > 0) col.AddChild(Line("Now: " + Data.SkillNumbers(sk.Id, rank), new Color(0.6f, 0.7f, 1f), 13));
            col.AddChild(Line((rank > 0 ? "Next: " : "Rank 1: ") + Data.SkillNumbers(sk.Id, rank + 1), UITheme.Faint, 13));
            if (h.Level < sk.Level) col.AddChild(Line($"Needs level {sk.Level}", UITheme.Bad, 13));
            row.AddChild(col);
            Button plus = UITheme.B("+", () => Done(h.Learn(id)), 15);
            plus.Disabled = h.CannotLearn(id) != null;
            row.AddChild(plus);
            Body.AddChild(row);
        }
    }
}

public partial class ShopPanel : GamePanel
{
    public ShopPanel() { }

    public ShopPanel(GameScreen s) : base(s, s.Trading switch { "healer" => "Maren's Remedies", "peddler" => "Tobin's Wares", _ => "Oskar's Forge" }, "left")
    {
        bool healer = s.Trading == "healer", peddler = s.Trading == "peddler";
        Body.AddChild(Line("Click a ware to buy it.", UITheme.Dim, 14));
        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        var wares = healer ? Loot.HealerStock() : peddler ? G.PeddlerWares : G.SmithWares;
        for (int k = 0; k < wares.Count; k++)
        {
            int i = k;
            string from = healer ? "healer" : peddler ? "peddler" : "smith";
            grid.AddChild(new ItemSlot
            {
                Item = wares[k], Game = G, CustomMinimumSize = new Vector2(64, 64),
                Extra = it => $"Costs {Game.Price(it)} gold",
                Clicked = () => Done(G.Buy(from, i)),
            });
        }
        Body.AddChild(grid);
        Body.AddChild(Line($"You have {G.Hero.Gold} gold", new Color(0.95f, 0.8f, 0.35f)));
        if (!healer && !peddler)
        {
            // Oskar joins three gems of a kind and grade into one of the next grade.
            Body.AddChild(new HSeparator());
            Body.AddChild(UITheme.Wrap($"Three gems of a kind and grade, joined into one better: {Game.CombineCost(0)} gold for chipped, {Game.CombineCost(1)} for plain.", 14, UITheme.Dim, 400));
            Body.AddChild(UITheme.B("Join gems", () =>
            {
                int made = G.CombineGems();
                S.Say(made > 0 ? $"Oskar joins {made} gem{(made > 1 ? "s" : "")}." : "Oskar shrugs: three of a kind and grade, and the gold for it.", made > 0 ? UITheme.Accent : UITheme.Dim);
                Done(null);
            }, 16));
        }
    }
}

public partial class StashPanel : GamePanel
{
    public StashPanel() { }

    public StashPanel(GameScreen s) : base(s, "Stash", "left")
    {
        Body.AddChild(Line("What you leave here waits for you. Click to take it back.", UITheme.Dim, 14));
        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 3);
        grid.AddThemeConstantOverride("v_separation", 3);
        for (int k = 0; k < Game.StashSize; k++)
        {
            int i = k;
            grid.AddChild(new ItemSlot
            {
                Item = k < G.Hero.Stash.Count ? G.Hero.Stash[k] : null, Game = G, CustomMinimumSize = new Vector2(56, 56),
                Clicked = () => { if (i < G.Hero.Stash.Count) Done(G.FromStash(i)); },
            });
        }
        Body.AddChild(grid);
    }
}

public partial class TalkPanel : GamePanel
{
    public TalkPanel() { }

    public TalkPanel(GameScreen s) : base(s, Floor.FolkName(s.TalkingTo), "centre")
    {
        CustomMinimumSize = new Vector2(600, 0);
        Body.AddChild(UITheme.Wrap(s.TalkText ?? "", 17, UITheme.Text, 560));
        var row = UITheme.H(10);
        if (s.TalkingTo is "smith" or "healer" or "peddler")
        {
            string who = s.TalkingTo;
            row.AddChild(UITheme.B("Trade", () =>
            {
                s.Open("centre", "");
                s.Trading = who;
                s.Open("left", "shop");
                s.Trading = who;
                s.Open("right", "inventory");
            }));
        }
        if (s.TalkingTo == "priest")
        {
            // Brother Aldric's blessings: a shrine's, for five minutes, for gold.
            var bless = new GridContainer { Columns = 3 };
            bless.AddThemeConstantOverride("h_separation", 6);
            bless.AddThemeConstantOverride("v_separation", 6);
            foreach (var sh in Data.Shrines.Where(x => x.seconds > 0))
            {
                string id = sh.id;
                bless.AddChild(UITheme.B(sh.name.Replace("Shrine of ", ""), () =>
                {
                    string why = G.Bless(id);
                    if (why != null) s.Say(why, UITheme.Bad);
                    s.Open("centre", "");
                }, 15, $"{sh.about}, for five minutes: {G.BlessingCost} gold"));
            }
            Body.AddChild(Line($"A blessing for five minutes: {G.BlessingCost} gold.", UITheme.Dim, 14));
            Body.AddChild(bless);
        }
        if (s.TalkingTo == "elder" && G.CanGoDeeper)
            row.AddChild(UITheme.B($"Go down again: {Data.Tiers[G.Tier + 1]}", () =>
            {
                G.GoDeeper();
                s.Open("centre", "");
            }, 15, "Monsters 20 levels stronger, your resistances 20 lower; the quests start over. You keep everything you carry."));
        if (s.TalkingTo == "elder")
        {
            // The elder can unlearn the hero's points: free once, then for gold.
            Hero h = G.Hero;
            string cost = h.RespecCost == 0 ? "free, once" : $"{h.RespecCost} gold";
            row.AddChild(UITheme.B($"Unlearn every point ({cost})", () =>
            {
                string why = h.Respec();
                s.TalkText = why == null ? "Sit. Breathe. Forget what the deep taught you, and choose again." : $"Not today: {why}.";
                if (why == null) { Sounds.Play("waystone", 0.6f); s.Say("Every point is yours to spend again.", UITheme.Accent); }
                s.Refresh();
                s.Open("centre", "talk");
            }, 15));
        }
        row.AddChild(UITheme.B("Farewell", () => s.Open("centre", "")));
        Body.AddChild(row);
        GetChild<VBoxContainer>(0).GetChild<ScrollContainer>(1).CustomMinimumSize = new Vector2(0, 170);
    }
}

public partial class WaystonePanel : GamePanel
{
    public WaystonePanel() { }

    public WaystonePanel(GameScreen s) : base(s, "Waystone", "centre")
    {
        CustomMinimumSize = new Vector2(600, 0);
        Body.AddChild(Line("The runes wake under your hand. Where to?", UITheme.Dim, 15));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 6);
        for (int d = 0; d < Data.Bottom + 1; d++)
        {
            int depth = d;
            bool known = G.Hero.Waystones.Contains(d);
            Button b = UITheme.B(known ? Data.Floor(d).Name : "(asleep)", () => { s.Open("centre", ""); string why = G.Travel(depth); if (why != null) s.Say(why, UITheme.Bad); }, 15);
            b.Disabled = !known || d == G.Depth;
            b.CustomMinimumSize = new Vector2(280, 0);
            grid.AddChild(b);
        }
        Body.AddChild(grid);
        GetChild<VBoxContainer>(0).GetChild<ScrollContainer>(1).CustomMinimumSize = new Vector2(0, 260);
    }
}

/// <summary>The in-game menu (Esc, the back button): the game waits while it is open.</summary>
public partial class MenuPanel : GamePanel
{
    public MenuPanel() { }

    public MenuPanel(GameScreen s) : base(s, "Paused", "centre")
    {
        CustomMinimumSize = new Vector2(440, 0);
        Body.AddChild(UITheme.Flat("Resume", () => s.Open("centre", ""), 26, UITheme.Ember, UITheme.Title));
        Body.AddChild(new HSeparator());
        Body.AddChild(Slider("Sound", Profile.Current.Sound, Profile.Current.Volume, (on, v) => { Profile.Current.Sound = on; Profile.Current.Volume = v; Profile.Current.Apply(); }));
        Body.AddChild(Slider("Music", Profile.Current.MusicOn, Profile.Current.MusicVolume, (on, v) => { Profile.Current.MusicOn = on; Profile.Current.MusicVolume = v; Sounds.ApplyMusic(); }));
        if (!GameScreen.Touch)
        {
            var full = new CheckBox { Text = "Full screen (F11)", ButtonPressed = Profile.Current.Fullscreen, FocusMode = FocusModeEnum.None };
            full.Toggled += on => { Profile.Current.Fullscreen = on; Profile.Current.Apply(); Profile.Current.Save(); };
            Body.AddChild(full);
        }
        foreach (var (text, get, set) in new (string, Func<bool>, Action<bool>)[]
                 {
                     ("Damage numbers", () => Profile.Current.DamageNumbers, v => Profile.Current.DamageNumbers = v),
                     ("Show every drop's name", () => Profile.Current.AllNames, v => Profile.Current.AllNames = v),
                 })
        {
            var cb = new CheckBox { Text = text, ButtonPressed = get(), FocusMode = FocusModeEnum.None };
            cb.Toggled += v => { set(v); Profile.Current.Save(); };
            Body.AddChild(cb);
        }
        Body.AddChild(new HSeparator());
        Body.AddChild(Line(GameScreen.Touch
            ? "Tap to walk, fight, pick up and use. Tap the right-hand skill icon to make taps use that skill. Two fingers: pinch to zoom, twist to turn the view. Back closes panels."
            : "Left click: walk, fight, pick up, use, talk (hold to keep walking). Right click: your skill (hold to repeat). Shift: fight where you stand. Q W E R T: choose a skill. 1-4: the belt. I C S: pack, character, skills. Tab: the map. Wheel: zoom. Middle button drag, [ and ]: turn the view (Backspace: back).",
            UITheme.Dim, 14));
        Body.GetChild<Label>(Body.GetChildCount() - 1).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Body.AddChild(new HSeparator());
        var row = UITheme.H(24);
        row.AddChild(UITheme.Flat("Save and go to the title", () => Root.Instance.ShowTitle(), 19, UITheme.Bone));
        row.AddChild(UITheme.Flat("Quit", () => { s.Autosave(); GetTree().Quit(); }, 19, UITheme.Ash));
        Body.AddChild(row);
        GetChild<VBoxContainer>(0).GetChild<ScrollContainer>(1).CustomMinimumSize = new Vector2(0, 470);
    }

    private static Control Slider(string name, bool on, float volume, System.Action<bool, float> set)
    {
        var row = UITheme.H(12);
        var cb = new CheckBox { Text = name, ButtonPressed = on, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(110, 0) };
        var sl = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = volume, CustomMinimumSize = new Vector2(220, 24), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        cb.Toggled += v => { set(v, (float)sl.Value); Profile.Current.Save(); };
        sl.ValueChanged += v => { set(cb.ButtonPressed, (float)v); Profile.Current.Save(); };
        row.AddChild(cb);
        row.AddChild(sl);
        return row;
    }
}

public partial class NoticePanel : GamePanel
{
    public NoticePanel() { }

    public NoticePanel(GameScreen s) : base(s, "The Warden Falls", "centre")
    {
        CustomMinimumSize = new Vector2(600, 0);
        Body.AddChild(UITheme.Wrap("Vorgath the Ashen Warden is dead, and the fire under Emberwick sinks back to embers. " +
                                   "The village will remember you. The deep is still there, and so is its loot, if you want it.", 17, UITheme.Text, 560));
        var row = UITheme.H(10);
        row.AddChild(UITheme.B("Keep playing", () => s.Open("centre", "")));
        row.AddChild(UITheme.B("To the title", () => Root.Instance.ShowTitle()));
        Body.AddChild(row);
        GetChild<VBoxContainer>(0).GetChild<ScrollContainer>(1).CustomMinimumSize = new Vector2(0, 150);
    }
}
