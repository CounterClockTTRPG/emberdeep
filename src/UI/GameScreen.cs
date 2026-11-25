using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotAgent;

namespace Emberdeep;

/// <summary>
/// Playing: the 3D world in a viewport, the HUD over it (life and mana globes, belt, skills, experience, the map in the
/// corner, the foe under the cursor), the panels, and the mouse and keys turned into orders.
/// Left click: walk, fight, pick up, use, talk (hold to keep walking). Right click: the chosen skill (hold to repeat).
/// Shift + left: fight where you stand. Q W E R T: choose a skill. 1-4: the belt. I C S: inventory, character,
/// skills. Tab: the map. Wheel: zoom. Esc: close a panel, or the title.
/// </summary>
public partial class GameScreen : Control
{
    public Game Game;
    public World3D World;
    private SubViewportContainer _vc;
    private SubViewport _vp;
    private Control _hud;
    private Control _left, _right, _centre;
    public string LeftPanel = "", RightPanel = "", CentrePanel = "";
    private bool _bigMap;
    private bool _lmb, _rmb, _lmbMove;
    /// <summary>On a touchscreen, with no right button: taps use the right-hand skill while this is on.</summary>
    private bool _skillTaps;
    public static bool Touch => DisplayServer.IsTouchscreenAvailable();
    private double _hold, _sinceSave;
    private Actor _hover;
    private Drop _hoverDrop;
    private readonly List<(string text, Color c, double t)> _log = new();
    private string _banner;
    private double _bannerT;
    private HBoxContainer _belt, _skills;
    private Button _charB, _skillB, _invB;
    private int _beltSig = -1;
    private string _skillSig;
    /// <summary>Who the open trade is with (smith, healer) or the stash.</summary>
    public string Trading;

    public const int Band = 112;

    public override void _Ready()
    {
        Theme = UITheme.Theme;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        _vc = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_vc);
        UITheme.Anchor(_vc, 0, 0, 1, 1);
        _vp = new SubViewport { HandleInputLocally = false, Msaa3D = Viewport.Msaa.Msaa2X, OwnWorld3D = true };
        _vc.AddChild(_vp);
        World = new World3D { Game = Game };
        _vp.AddChild(World);
        _hud = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_hud);
        UITheme.Anchor(_hud, 0, 0, 1, 1);
        _hud.Draw += DrawHud;
        BuildBand();
        _left = Holder(); UITheme.Anchor(_left, 0, 0, 0, 1, 16, 16, 476, -Band - 8);
        _right = Holder(); UITheme.Anchor(_right, 1, 0, 1, 1, -600, 16, -16, -Band - 8);
        _centre = Holder(); UITheme.Anchor(_centre, 0.5f, 0.2f, 0.5f, 0.2f, -300, 0, 300, 0);
        Sounds.Mood(Sounds.MoodFor(Game.Depth));
        Banner(Game.Map.Def.Name);
        GrabFocus();
    }

    private Control Holder()
    {
        var c = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(c);
        return c;
    }

    // ---- the band at the bottom: belt, skills, panel buttons --------------------------------------------------------

    private void BuildBand()
    {
        _belt = UITheme.H(6);
        AddChild(_belt);
        UITheme.Anchor(_belt, 0.5f, 1, 0.5f, 1, -470, -Band + 28, -230, -Band + 90);
        _skills = UITheme.H(6);
        AddChild(_skills);
        UITheme.Anchor(_skills, 0.5f, 1, 0.5f, 1, -190, -Band + 22, 300, -Band + 96);
        var buttons = UITheme.H(6);
        AddChild(buttons);
        UITheme.Anchor(buttons, 0.5f, 1, 0.5f, 1, 330, -Band + 34, 600, -Band + 78);
        _charB = UITheme.B("Char", () => Toggle("left", "character"), 15, "Character (C)");
        _invB = UITheme.B("Pack", () => Toggle("right", "inventory"), 15, "Inventory (I)");
        _skillB = UITheme.B("Skills", () => Toggle("left", "skills"), 15, "Skills (S)");
        buttons.AddChild(_charB);
        buttons.AddChild(_invB);
        buttons.AddChild(_skillB);
        buttons.AddChild(UITheme.B("Map", () => _bigMap = !_bigMap, 15, "Map (Tab)"));
        buttons.AddChild(UITheme.B("Menu", () => Toggle("centre", "menu"), 15, "Menu (Esc)"));
    }

    private void RefreshBand()
    {
        Hero h = Game.Hero;
        int sig = h.Belt.Aggregate(17, (a, i) => a * 31 + (i == null ? 0 : i.Base.GetHashCode() + i.Count));
        if (sig != _beltSig)
        {
            _beltSig = sig;
            foreach (Node n in _belt.GetChildren()) n.QueueFree();
            for (int k = 0; k < 4; k++)
            {
                int kk = k;
                var slot = new ItemSlot { Item = h.Belt[k], Key = (k + 1).ToString(), CustomMinimumSize = new Vector2(56, 56) };
                slot.Clicked = () => UseBelt(kk);
                _belt.AddChild(slot);
            }
        }
        string ssig = $"{h.Left}|{h.Right}|{_skillTaps}|{string.Join(",", h.Skills.Select(kv => kv.Key + kv.Value))}";
        if (ssig != _skillSig)
        {
            _skillSig = ssig;
            foreach (Node n in _skills.GetChildren()) n.QueueFree();
            _skills.AddChild(new SkillIcon(h.Left, true, Game) { CustomMinimumSize = new Vector2(66, 66), Clicked = () => Toggle("left", "skills"), Tip = "Left button: " + SkillName(h.Left) });
            _skills.AddChild(new SkillIcon(h.Right, true, Game)
            {
                CustomMinimumSize = new Vector2(66, 66), Chosen = _skillTaps,
                // On a touchscreen this icon is the right button: tap it and taps use the skill, tap again to walk.
                Clicked = () =>
                {
                    if (!Touch) { Toggle("left", "skills"); return; }
                    _skillTaps = !_skillTaps;
                    _skillSig = null;
                    Say(_skillTaps ? $"Taps use {SkillName(Game.Hero.Right)}" : "Taps walk and fight", UITheme.Dim);
                },
                Tip = Touch ? "Tap: taps use " + SkillName(h.Right) : "Right button: " + SkillName(h.Right),
            });
            _skills.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0) });
            string keys = "QWERT";
            var actives = Data.SkillsOf(h.Class).Where(s => !s.Passive).ToList();
            for (int k = 0; k < actives.Count; k++)
            {
                SkillDef s = actives[k];
                bool known = h.Rank(s.Id) > 0;
                string id = s.Id;
                _skills.AddChild(new SkillIcon(s.Id, known, Game)
                {
                    CustomMinimumSize = new Vector2(48, 48), Key = keys[k].ToString(), Chosen = h.Right == s.Id,
                    Clicked = () => { if (Game.Hero.Rank(id) > 0) { Game.Hero.Right = id; _skillSig = null; } },
                    Tip = known ? $"{s.Name} ({keys[k]}): put it on the right button" : $"{s.Name}: not learned (level {s.Level})",
                });
            }
        }
        _charB.Text = h.AttrPoints > 0 ? "Char +" : "Char";
        _skillB.Text = h.SkillPoints > 0 ? "Skills +" : "Skills";
    }

    public static string SkillName(string id) => id == "attack" || id == null ? "Attack" : Data.Skill(id)?.Name ?? id;

    // ---- panels -----------------------------------------------------------------------------------------------------

    /// <summary>Open or close a panel in a place (left, right, centre).</summary>
    public void Toggle(string where, string what)
    {
        string now = where switch { "left" => LeftPanel, "right" => RightPanel, _ => CentrePanel };
        Open(where, now == what ? "" : what);
    }

    public void Open(string where, string what)
    {
        Control holder = where switch { "left" => _left, "right" => _right, _ => _centre };
        foreach (Node n in holder.GetChildren()) n.QueueFree();
        if (where == "left") LeftPanel = what; else if (where == "right") RightPanel = what; else CentrePanel = what;
        if (where == "left" && what is not ("shop" or "stash")) Trading = null;
        Control p = what switch
        {
            "inventory" => new InventoryPanel(this),
            "character" => new CharacterPanel(this),
            "skills" => new SkillsPanel(this),
            "shop" => new ShopPanel(this),
            "stash" => new StashPanel(this),
            "talk" => new TalkPanel(this),
            "waystone" => new WaystonePanel(this),
            "notice" => new NoticePanel(this),
            "menu" => new MenuPanel(this),
            _ => null,
        };
        if (p == null) return;
        holder.AddChild(p);
        if (where == "centre") UITheme.Anchor(p, 0, 0, 1, 0);
        else UITheme.Anchor(p, 0, 0, 1, 1);
    }

    /// <summary>Rebuild the open panels (after anything changed what they show).</summary>
    public void Refresh()
    {
        if (LeftPanel != "") Open("left", LeftPanel);
        if (RightPanel != "") Open("right", RightPanel);
        _beltSig = -1;
        _skillSig = null;
    }

    public void CloseAll()
    {
        Open("left", "");
        Open("right", "");
        Open("centre", "");
        Trading = null;
    }

    public bool AnyOpen => LeftPanel != "" || RightPanel != "" || CentrePanel != "";

    /// <summary>A line in the message log (and a word on the screen for a moment).</summary>
    public void Say(string text, Color? c = null) { _log.Add((text, c ?? UITheme.Text, 0)); if (_log.Count > 6) _log.RemoveAt(0); }

    public void Banner(string text) { _banner = text; _bannerT = 3.5; }

    /// <summary>A talk with a village folk: the elder's words, the healer's mending, the smith's or healer's wares.</summary>
    public string TalkingTo;
    public string TalkText;

    // ---- every frame ------------------------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        // Under an agent, time only passes while a command runs (AgentServer.Running is always true otherwise).
        // The menu pauses the game (it is played alone); under an agent, time passes only while a command runs.
        if (AgentServer.Running && CentrePanel != "menu") Game.Step(delta);
        // Saved every two minutes of play (and on every floor change), so a crash loses little.
        _sinceSave += delta;
        if (_sinceSave > 120) Autosave();
        foreach (Ev e in Game.Events) Happened(e);
        Game.Events.Clear();
        World.Sync(delta);
        Hover();
        Holding(delta);
        for (int k = 0; k < _log.Count; k++) _log[k] = (_log[k].text, _log[k].c, _log[k].t + delta);
        _log.RemoveAll(l => l.t > 9);
        _bannerT -= delta;
        RefreshBand();
        _hud.QueueRedraw();
    }

    private void Happened(Ev e)
    {
        World.OnEvent(e);
        Actor a = e.A >= 0 ? Game.Find(e.A) : null;
        switch (e.Kind)
        {
            case "swing": Sounds.Play(a?.M?.Radius > 0.55 ? "heavy" : "swing", 0.45f, 0.12f); break;
            case "shoot": Sounds.Play("bow", 0.5f, 0.1f); break;
            case "cast":
                Sounds.Play(e.Text switch { "nova" => "nova", "chain" => "light", "shield" => "shield", "warcry" => "warcry", "quake" => "quake", _ => "fire" }, 0.55f, 0.08f);
                break;
            case "hit": Sounds.Play(e.Crit ? "crit" : a?.M?.Armour >= 20 ? "hitmetal" : "hit", 0.5f, 0.15f, 30); break;
            case "hurt": Sounds.Play("hurt", 0.6f, 0.1f, 80); break;
            case "block": Sounds.Play("block", 0.6f, 0.1f); break;
            case "miss" or "dodge": Sounds.Play("miss", 0.3f, 0.1f); break;
            case "blast": Sounds.Play(e.V >= 3 ? "bigblast" : "blast", 0.55f, 0.1f); break;
            case "fizzle": break;
            case "die": Sounds.Play(a?.Def is "sentry" or "archer" or "knight" ? "bones" : "die", 0.5f, 0.15f); break;
            case "death":
                Sounds.Play("death", 0.8f);
                Banner("You have fallen");
                Say($"Slain by {e.Text}. You wake in Emberwick{(e.V > 0 ? $", {e.V} gold the poorer" : "")}.", UITheme.Bad);
                CloseAll();
                break;
            case "gold": Sounds.Play("gold", 0.5f, 0.05f); if (e.Text != null && !e.Text.StartsWith("Sold") && !e.Text.StartsWith("Bought")) Say($"{e.V} gold", new Color(0.95f, 0.8f, 0.35f)); break;
            case "pickup": Sounds.Play("pickup", 0.5f); Say($"Picked up {e.Text}", e.V >= 1 ? World3D.RarityColour(new Item { Rarity = (Rarity)e.V, Base = "x" }) : UITheme.Dim); Refresh(); break;
            case "drop": if (e.V >= (int)Rarity.Rare) Sounds.Play("raredrop", 0.7f); else if (e.V == (int)Rarity.Magic) Sounds.Play("magicdrop", 0.5f); break;
            case "full": Sounds.Play("nomana", 0.5f); Say(e.Text, UITheme.Bad); break;
            case "potion": Sounds.Play("potion", 0.6f); break;
            case "level":
                Sounds.Play("level", 0.8f);
                Banner($"Level {e.V}");
                Say($"You reach level {e.V}: 5 attribute points and a skill point to spend.", UITheme.Accent);
                Refresh();
                break;
            case "nomana": Sounds.Play("nomana", 0.4f, 0, 250); Say("Not enough mana.", UITheme.Dim); break;
            case "enter":
                Autosave();
                Sounds.Play("stairs", 0.6f);
                Sounds.Mood(Sounds.MoodFor(e.V));
                Banner(e.Text);
                CloseAll();
                break;
            case "waystone": Sounds.Play("waystone", 0.7f); Say(e.Text, new Color(0.5f, 0.7f, 1f)); break;
            case "waymenu": Open("centre", "waystone"); break;
            case "chest": Sounds.Play("chest", 0.6f); break;
            case "stash": Trading = "stash"; Open("left", "stash"); Open("right", "inventory"); break;
            case "talk": StartTalk(e.Text); break;
            case "quest": Sounds.Play("quest", 0.7f); Say(e.Text, UITheme.Accent); Banner(e.Text); break;
            case "reward": Say("Your reward is given.", UITheme.Accent); Refresh(); break;
            case "say": Say(e.Text, a != null ? new Color(1f, 0.6f, 0.4f) : UITheme.Dim); if (a != null) Banner($"\"{e.Text}\""); break;
            case "boss": Sounds.Play("boss", 0.9f); Banner(e.Text); break;
            case "won": Sounds.Play("win", 0.9f); Banner("Victory"); Say(e.Text, UITheme.Accent); Open("centre", "notice"); break;
            case "portal": Sounds.Play("waystone", 0.6f); break;
            case "shieldbreak": Say("Your arcane shield breaks.", UITheme.Dim); break;
            case "restock": Say("The deep has filled again since you were here.", UITheme.Dim); break;
            case "shrine": Sounds.Play("waystone", 0.8f); Banner(e.Text.Split(':')[0]); Say(e.Text, UITheme.Accent); break;
            case "blessing-end": Say("The shrine's blessing fades.", UITheme.Dim); break;
        }
    }

    /// <summary>Write the game to its slot now (not in the self-check or the walkthrough).</summary>
    public void Autosave()
    {
        _sinceSave = 0;
        if (!Profile.Scratch) Profile.SaveGame(Game);
    }

    private void StartTalk(string who)
    {
        TalkingTo = who;
        switch (who)
        {
            case "elder": TalkText = Game.TalkElder(); break;
            case "healer":
                Game.Me.Life = Game.Me.MaxLife;
                Game.Me.Mana = Game.Me.MaxMana;
                TalkText = "Sit, let me see those wounds. There. Whole again, or near enough. Do you need draughts for the dark?";
                Sounds.Play("potion", 0.5f);
                break;
            default:
                TalkText = Game.Hero.Level < 3 ? "New blade? Old blade? I've got both, and I'll buy what you drag up from the graves."
                    : "Back again. Show me what the deep gave you; I'll pay fair, mostly.";
                break;
        }
        Open("centre", "talk");
    }

    // ---- the mouse --------------------------------------------------------------------------------------------------

    private Vector2 Mouse => GetLocalMousePosition();

    private void Hover()
    {
        Vector2 m = Mouse;
        _hover = Over(m) ? null : World.ActorAt(m);
        _hoverDrop = _hover == null && !Over(m) ? World.DropAt(m) : null;
        // A usable thing under the cursor lights up and says its name.
        int prop = 0;
        if (_hover == null && _hoverDrop == null && !Over(m) && World.Ground(m) is var (gx, gy)) prop = World.PropAt(gx, gy);
        World.HoverProp = prop > 0 ? prop : 0;
        World.HoverDrop = _hoverDrop?.Id ?? 0;
        World.AllLabels = Touch || Input.IsKeyPressed(Key.Alt);
        MouseDefaultCursorShape = _hover != null || _hoverDrop != null || prop != 0 ? CursorShape.PointingHand : CursorShape.Arrow;
    }

    /// <summary>Is the mouse over the band or an open panel (so it does not order the hero)?</summary>
    private bool Over(Vector2 m)
    {
        if (m.Y > Size.Y - Band) return true;
        foreach (Control holder in new[] { _left, _right, _centre })
            foreach (Node n in holder.GetChildren())
                if (n is Control c && c.Visible && c.GetGlobalRect().HasPoint(GetGlobalMousePosition())) return true;
        return false;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) { World.Zoom = Mathf.Max(0.55f, World.Zoom - 0.08f); return; }
            if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) { World.Zoom = Mathf.Min(1.5f, World.Zoom + 0.08f); return; }
            if (mb.ButtonIndex == MouseButton.Left && _skillTaps)
            {
                // Skill taps: the left button (a finger) acts as the right one.
                _rmb = mb.Pressed;
                if (mb.Pressed && !Over(mb.Position)) { RightClick(mb.Position); _hold = 0; }
                AcceptEvent();
                return;
            }
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _lmb = mb.Pressed;
                if (mb.Pressed && !Over(mb.Position)) { GrabFocus(); Click(mb.Position, mb.ShiftPressed); _hold = 0; }
                if (!mb.Pressed) _lmbMove = false;
                AcceptEvent();
            }
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _rmb = mb.Pressed;
                if (mb.Pressed && !Over(mb.Position)) { RightClick(mb.Position); _hold = 0; }
                AcceptEvent();
            }
        }
    }

    /// <summary>A left click on the world: what is under it decides the order.</summary>
    public void Click(Vector2 p, bool here = false)
    {
        Actor a = World.ActorAt(p);
        var g = World.Ground(p);
        if (here && g != null) { Game.OrderSkill(Game.Hero.Left == "attack" ? "attack" : Game.Hero.Left, g.Value.x, g.Value.y); ForceAttack(g.Value); return; }
        if (a != null) { if (a.Npc) Game.OrderTalk(a.Id); else Game.OrderAttack(a.Id, Game.Hero.Left); return; }
        Drop d = World.DropAt(p);
        if (d != null) { Game.OrderPick(d.Id); return; }
        if (g == null) return;
        int prop = World.PropAt(g.Value.x, g.Value.y);
        if (prop != 0) { Game.OrderUse(prop); return; }
        Game.OrderMove(g.Value.x, g.Value.y);
        _lmbMove = true;
    }

    /// <summary>Shift + click: swing or shoot toward the place without walking.</summary>
    private void ForceAttack((double x, double y) at)
    {
        if (Game.Me.Act is "idle" or "walk") Game.Begin(Game.Me, Game.Hero.Left ?? "attack", at.x, at.y, -1);
        Game.Halt();
    }

    public void RightClick(Vector2 p)
    {
        string skill = Game.Hero.Right;
        if (skill == null) return;
        SkillDef s = Data.Skill(skill);
        Actor a = World.ActorAt(p);
        var g = World.Ground(p);
        if (a != null && !a.Npc && s?.Aim is "Melee" or "Ranged" or "Spell") { Game.OrderAttack(a.Id, skill); return; }
        if (g == null) return;
        (double x, double y) at = a != null ? (a.X, a.Y) : g.Value;
        Game.OrderSkill(skill, at.x, at.y);
    }

    private void Holding(double dt)
    {
        _hold += dt;
        if (Over(Mouse)) return;
        if (_lmb && _lmbMove && _hold > 0.15)
        {
            _hold = 0;
            var g = World.Ground(Mouse);
            if (g != null) Game.OrderMove(g.Value.x, g.Value.y);
        }
        if (_rmb && _hold > 0.1 && Game.Order.Kind == "none" && Game.Me.Act is "idle" or "walk")
        {
            _hold = 0;
            RightClick(Mouse);
        }
    }

    /// <summary>Esc or Android's back button: the big map, then the panels, then the title (which saves).</summary>
    public void Back()
    {
        if (_bigMap) _bigMap = false;
        else if (CentrePanel == "menu") Open("centre", "");
        else if (AnyOpen) CloseAll();
        else Open("centre", "menu");
    }

    // ---- keys -------------------------------------------------------------------------------------------------------

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.Escape: Back(); break;
            case Key.I or Key.B: Toggle("right", "inventory"); break;
            case Key.C or Key.A: Toggle("left", "character"); break;
            case Key.S or Key.K: Toggle("left", "skills"); break;
            case Key.Tab or Key.M: _bigMap = !_bigMap; break;
            case Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4: UseBelt((int)(k.Keycode - Key.Key1)); break;
            case Key.Q or Key.W or Key.E or Key.R or Key.T:
            {
                var actives = Data.SkillsOf(Game.Hero.Class).Where(s => !s.Passive).ToList();
                int i = "QWERT".IndexOf(k.Keycode.ToString(), StringComparison.Ordinal);
                if (i >= 0 && i < actives.Count && Game.Hero.Rank(actives[i].Id) > 0) { Game.Hero.Right = actives[i].Id; _skillSig = null; Say($"Right button: {actives[i].Name}", UITheme.Dim); }
                break;
            }
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    public void UseBelt(int k)
    {
        string why = Game.UseBelt(k);
        if (why != null) Say(why, UITheme.Dim);
        _beltSig = -1;
    }

    // ---- drawing the HUD --------------------------------------------------------------------------------------------

    private void DrawHud()
    {
        Vector2 size = Size;
        Font font = UITheme.Body;
        // The band: dark stone with an ember edge.
        _hud.DrawRect(new Rect2(0, size.Y - Band, size.X, Band), new Color(0.06f, 0.05f, 0.05f, 0.94f));
        _hud.DrawRect(new Rect2(0, size.Y - Band, size.X, 2), new Color(0.55f, 0.25f, 0.1f));
        Hero h = Game.Hero;
        Actor me = Game.Me;
        Globe(new Vector2(92, size.Y - 74), 74, me.Life / Math.Max(1, me.MaxLife), new Color(0.75f, 0.1f, 0.08f), $"{Math.Ceiling(me.Life)}/{Math.Round(me.MaxLife)}");
        Globe(new Vector2(size.X - 92, size.Y - 74), 74, me.Mana / Math.Max(1, me.MaxMana), new Color(0.15f, 0.25f, 0.85f), $"{Math.Floor(me.Mana)}/{Math.Round(me.MaxMana)}");
        // Experience, a thin bar along the top of the band.
        float x0 = 190, x1 = size.X - 190;
        double frac = h.Level >= Data.MaxLevel ? 1 : h.Xp / (double)Data.XpToNext(h.Level);
        _hud.DrawRect(new Rect2(x0, size.Y - Band + 8, x1 - x0, 7), new Color(0.12f, 0.1f, 0.08f));
        _hud.DrawRect(new Rect2(x0, size.Y - Band + 8, (float)((x1 - x0) * frac), 7), new Color(0.85f, 0.65f, 0.25f));
        _hud.DrawString(font, new Vector2(x0, size.Y - 10), $"Level {h.Level} {h.Class}   {h.Gold} gold   {Game.Map.Def.Name}", HorizontalAlignment.Left, -1, 15, UITheme.Dim);
        if (me.ShieldT > 0) _hud.DrawString(font, new Vector2(x1 - 220, size.Y - 10), $"Arcane shield {me.Shield:0}", HorizontalAlignment.Left, -1, 15, new Color(0.5f, 0.7f, 1f));
        if (h.BlessingLeft > 0)
        {
            var bl = Data.Shrines.FirstOrDefault(x => x.id == h.Blessing);
            _hud.DrawString(font, new Vector2(x1 - 460, size.Y - 10), $"{bl.name} {h.BlessingLeft:0} s", HorizontalAlignment.Left, -1, 15, UITheme.Accent);
        }
        // The foe (or thing) under the cursor, at the top.
        Actor t = _hover;
        if (t == null && Game.Order.Kind == "attack") t = Game.Find(Game.Order.Target);
        if (t != null && t.Alive && !t.Npc)
        {
            float w = 340, cx = size.X / 2;
            _hud.DrawRect(new Rect2(cx - w / 2, 14, w, 26), new Color(0.1f, 0.03f, 0.03f, 0.9f));
            _hud.DrawRect(new Rect2(cx - w / 2, 14, (float)(w * t.Life / t.MaxLife), 26), new Color(0.55f, 0.08f, 0.06f, 0.95f));
            Color nc = t.Title != null ? (t.Champion ? new Color(0.5f, 0.65f, 1f) : new Color(0.95f, 0.8f, 0.4f)) : UITheme.Text;
            _hud.DrawString(font, new Vector2(cx - w / 2, 34), t.Name, HorizontalAlignment.Center, w, 17, nc);
            string traits = t.Traits.Count > 0 ? string.Join(", ", t.Traits.Select(Data.TraitWord)) + ", " : "";
            _hud.DrawString(font, new Vector2(cx - w / 2, 58), $"{traits}level {t.Level}", HorizontalAlignment.Center, w, 13, t.Traits.Count > 0 ? World3D.TraitColour(t.Traits[0]) : UITheme.Dim);
        }
        else if (_hover?.Npc == true)
            _hud.DrawString(font, new Vector2(size.X / 2 - 200, 34), "Click to talk", HorizontalAlignment.Center, 400, 16, UITheme.Accent);
        if (_bannerT > 0 && _banner != null)
        {
            float a = (float)Math.Min(1, _bannerT);
            _hud.DrawString(UITheme.Title, new Vector2(0, size.Y * 0.28f), _banner, HorizontalAlignment.Center, size.X, 40, UITheme.Accent with { A = a });
        }
        float ly = size.Y - Band - 16;
        for (int k = _log.Count - 1; k >= 0; k--)
        {
            var (text, c, age) = _log[k];
            float a = (float)Math.Clamp(9 - age, 0, 1);
            _hud.DrawString(font, new Vector2(LeftPanel == "" ? 20 : 490, ly), text, HorizontalAlignment.Left, -1, 16, c with { A = a });
            ly -= 22;
        }
        if (h.AttrPoints > 0 || h.SkillPoints > 0)
            _hud.DrawString(font, new Vector2(size.X / 2 + 330, size.Y - Band + 28), "points to spend", HorizontalAlignment.Left, -1, 13, UITheme.Accent);
        if (_bigMap) Map(new Rect2(size.X / 2 - 330, 60, 660, 660), 10, true);
        else if (RightPanel == "") Map(new Rect2(size.X - 214, 14, 200, 200), 3.2f, false);
    }

    private void Globe(Vector2 c, float r, double frac, Color fill, string text)
    {
        _hud.DrawCircle(c, r + 4, new Color(0.2f, 0.15f, 0.12f));
        _hud.DrawCircle(c, r, new Color(0.05f, 0.04f, 0.04f));
        float level = (float)Math.Clamp(frac, 0, 1);
        // The liquid: the part of the circle below its level.
        var pts = new List<Vector2>();
        float top = c.Y + r - 2 * r * level;
        for (int i = 0; i <= 40; i++)
        {
            float ang = Mathf.Pi * i / 40;
            Vector2 p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
            pts.Add(p);
        }
        for (int i = 0; i <= 40; i++)
        {
            float ang = Mathf.Pi + Mathf.Pi * i / 40;
            Vector2 p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
            pts.Add(p);
        }
        var clipped = pts.Select(p => new Vector2(p.X, Mathf.Max(p.Y, top))).ToArray();
        if (level > 0.005f)
        {
            // Points clamped to the level line make a convex shape when kept in order around the circle.
            var poly = new List<Vector2>();
            foreach (Vector2 p in clipped) if (poly.Count == 0 || poly[^1].DistanceTo(p) > 0.3f) poly.Add(p);
            if (poly.Count >= 3)
                try { _hud.DrawColoredPolygon(poly.ToArray(), fill); } catch (Exception) { }
        }
        _hud.DrawCircle(c + new Vector2(-r * 0.35f, -r * 0.4f), r * 0.18f, new Color(1, 1, 1, 0.12f));
        _hud.DrawArc(c, r + 2, 0, Mathf.Tau, 48, new Color(0.45f, 0.3f, 0.15f), 3);
        _hud.DrawString(UITheme.Body, c + new Vector2(-r, 6), text, HorizontalAlignment.Center, 2 * r, 16, UITheme.Text);
    }

    /// <summary>The seen part of the floor around the hero (or all of it, large): open cells, walls, the stairs, the hero.</summary>
    private void Map(Rect2 box, float cell, bool whole)
    {
        Floor f = Game.Map;
        byte[] seen = Game.Here.Seen;
        _hud.DrawRect(box, new Color(0, 0, 0, whole ? 0.75f : 0.45f));
        Vector2 centre = box.GetCenter();
        Vector2 Of(double x, double y) => whole
            ? box.Position + new Vector2((float)x, (float)y) * Math.Min(box.Size.X / f.W, box.Size.Y / f.H)
            : centre + new Vector2((float)(x - Game.Me.X), (float)(y - Game.Me.Y)) * cell;
        float cs = whole ? Math.Min(box.Size.X / f.W, box.Size.Y / f.H) : cell;
        for (int y = 0; y < f.H; y++)
            for (int x = 0; x < f.W; x++)
            {
                if (seen == null || seen[y * f.W + x] == 0) continue;
                Vector2 p = Of(x, y);
                if (!box.HasPoint(p) || !box.HasPoint(p + new Vector2(cs, cs))) continue;
                bool open = f.Floor_(x, y);
                if (open && f.Blocked[y * f.W + x]) _hud.DrawRect(new Rect2(p, new Vector2(cs, cs)), new Color(0.35f, 0.3f, 0.25f, 0.7f));
                else if (open) _hud.DrawRect(new Rect2(p, new Vector2(cs, cs)), new Color(0.25f, 0.22f, 0.2f, 0.55f));
                else if (f.Floor_(x + 1, y) || f.Floor_(x - 1, y) || f.Floor_(x, y + 1) || f.Floor_(x, y - 1))
                    _hud.DrawRect(new Rect2(p, new Vector2(cs, cs)), new Color(0.7f, 0.6f, 0.45f, 0.8f));
            }
        foreach (Prop p in f.Props.Where(p => p.Kind is "down" or "up" or "waystone"))
        {
            if (seen == null || seen[p.Y * f.W + p.X] == 0) continue;
            Vector2 q = Of(p.X + p.W / 2.0, p.Y + p.H / 2.0);
            if (box.HasPoint(q)) _hud.DrawCircle(q, Math.Max(3, cs), p.Kind == "waystone" ? new Color(0.4f, 0.6f, 1f) : new Color(1f, 0.5f, 0.2f));
        }
        foreach (Actor a in Game.Here.Actors.Where(a => a.Alive && (a.Npc || a.Awake)))
        {
            Vector2 q = Of(a.X, a.Y);
            if (box.HasPoint(q) && (a.Npc || Game.Dist(a, Game.Me) < 14)) _hud.DrawCircle(q, Math.Max(2, cs * 0.5f), a.Npc ? new Color(0.9f, 0.8f, 0.4f) : new Color(0.9f, 0.2f, 0.15f));
        }
        Vector2 me = Of(Game.Me.X, Game.Me.Y);
        _hud.DrawCircle(me, Math.Max(3, cs * 0.7f), new Color(1f, 1f, 1f));
        _hud.DrawRect(box, UITheme.Edge, false, 1);
    }

    // ---- for the agent ----------------------------------------------------------------------------------------------

    /// <summary>Where a sim point is on the screen (layout pixels).</summary>
    public Vector2 ScreenOf(double x, double y, float h = 0) => GetGlobalTransformWithCanvas() * World.ScreenOf(x, y, h);
}
