using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Emberdeep;

/// <summary>
/// The game in 3D: the floor and its walls (built per 12 x 12 chunk, so each chunk is lit by the braziers near it), the
/// props, a figure for every actor, the drops with their names, missiles, spell effects, the camera over the hero.
/// Sim (x, y) is 3D (x, 0, y): one cell a metre. It only shows; <see cref="GameScreen"/> turns clicks into orders.
/// </summary>
public partial class World3D : Node3D
{
    public Game Game;
    public Camera3D Cam;
    public float Zoom = 1;
    private WorldEnvironment _env;
    private DirectionalLight3D _sun;
    private OmniLight3D _heroLight;
    private Node3D _level, _dyn;
    private ShaderMaterial _walls, _glow;
    private StandardMaterial3D _ground;
    private int _builtDepth = -1;
    private ulong _builtSeed;
    private readonly Dictionary<int, Figure3D> _figs = new();
    private readonly Dictionary<int, (float t, string clip)> _last = new();
    private readonly Dictionary<int, Node3D> _drops = new(), _missiles = new(), _zones = new();
    private readonly Dictionary<int, Node3D> _props = new();
    private readonly List<Fx> _fx = new();
    private Node3D _portal, _shield, _target;
    private bool _heroHidden;
    private Vector3 _camAt;
    private float _shake;
    private double _clock;
    private readonly List<(OmniLight3D light, float energy, float phase)> _flicker = new();

    private sealed class Fx
    {
        public Node3D Node;
        public float T, Life;
        public Action<Fx, float> Tick;
    }

    public const float WallH = 2.6f;

    public override void _Ready()
    {
        Cam = new Camera3D { Fov = 38, Near = 0.3f, Far = 120, Current = true };
        AddChild(Cam);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.02f, 0.02f, 0.025f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.42f, 0.4f, 0.48f),
            AmbientLightEnergy = 0.55f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            GlowEnabled = true,
            GlowIntensity = 0.6f,
            GlowBloom = 0.05f,
            FogEnabled = true,
            FogLightColor = new Color(0.04f, 0.035f, 0.04f),
            FogDensity = 0.012f,
        };
        _env = new WorldEnvironment { Environment = env };
        AddChild(_env);
        _sun = new DirectionalLight3D { LightEnergy = 0.25f, LightColor = new Color(0.7f, 0.72f, 0.9f), ShadowEnabled = false };
        _sun.RotationDegrees = new Vector3(-55, -30, 0);
        AddChild(_sun);
        _heroLight = new OmniLight3D { LightColor = new Color(1f, 0.78f, 0.55f), LightEnergy = 1.4f, OmniRange = 9, OmniAttenuation = 1.2f };
        AddChild(_heroLight);
        _walls = new ShaderMaterial { Shader = new Shader { Code = WallShader } };
        _ground = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.95f };
        _glow = new ShaderMaterial { Shader = new Shader { Code = GlowShader } };
        _dyn = new Node3D { Name = "Dynamic" };
        AddChild(_dyn);
    }

    // ---- building a floor -------------------------------------------------------------------------------------------

    private static (Color floor, Color wall, Color edge) Palette(string theme) => theme switch
    {
        "village" => (new Color(0.24f, 0.22f, 0.17f), new Color(0.2f, 0.19f, 0.17f), new Color(0.12f, 0.11f, 0.1f)),
        "barrow" => (new Color(0.25f, 0.22f, 0.19f), new Color(0.33f, 0.3f, 0.27f), new Color(0.16f, 0.14f, 0.12f)),
        "catacomb" => (new Color(0.22f, 0.23f, 0.25f), new Color(0.36f, 0.36f, 0.38f), new Color(0.13f, 0.13f, 0.15f)),
        "ember" => (new Color(0.17f, 0.13f, 0.12f), new Color(0.27f, 0.2f, 0.18f), new Color(0.1f, 0.07f, 0.06f)),
        _ => (new Color(0.15f, 0.1f, 0.1f), new Color(0.26f, 0.16f, 0.14f), new Color(0.08f, 0.05f, 0.05f)),
    };

    private static float Hash(int x, int y, int k = 0)
    {
        uint h = (uint)(x * 73856093 ^ y * 19349663 ^ k * 83492791);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xffff) / 65535f;
    }

    /// <summary>Smooth value noise in 0..1 (patches of lighter and darker floor).</summary>
    private static float Noise(float x, float y, int k = 0)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float a = Mathf.Lerp(Hash(x0, y0, 20 + k), Hash(x0 + 1, y0, 20 + k), fx), b = Mathf.Lerp(Hash(x0, y0 + 1, 20 + k), Hash(x0 + 1, y0 + 1, 20 + k), fx);
        return Mathf.Lerp(a, b, fy);
    }

    private void Build()
    {
        _level?.QueueFree();
        foreach (Figure3D fig in _figs.Values) fig.QueueFree();
        _figs.Clear();
        _last.Clear();
        _heroHidden = false;
        foreach (Node3D n in _drops.Values.Concat(_missiles.Values).Concat(_zones.Values)) n.QueueFree();
        _drops.Clear(); _missiles.Clear(); _zones.Clear(); _props.Clear(); _usable.Clear(); _tweens.Clear();
        foreach (Fx fx in _fx) fx.Node.QueueFree();
        _fx.Clear();
        _flicker.Clear();
        _portal = null;
        _level = new Node3D { Name = "Level" };
        AddChild(_level);
        Floor f = Game.Map;
        _builtDepth = Game.Depth;
        _builtSeed = Game.Seed;
        var (floorC, wallC, edgeC) = Palette(f.Def.Theme);
        bool village = f.Village;
        Godot.Environment env = _env.Environment;
        env.AmbientLightEnergy = village ? 0.65f : 0.5f;
        env.AmbientLightColor = village ? new Color(0.55f, 0.52f, 0.58f) : new Color(0.42f, 0.4f, 0.48f);
        env.FogDensity = village ? 0.008f : 0.014f;
        _sun.LightEnergy = village ? 0.55f : 0.2f;
        _sun.LightColor = village ? new Color(0.95f, 0.75f, 0.6f) : new Color(0.7f, 0.72f, 0.9f);
        _heroLight.OmniRange = village ? 6 : 9;

        var holes = new HashSet<int>();
        foreach (Prop p in f.Props.Where(p => p.Kind == "down"))
            for (int y = p.Y; y < p.Y + p.H; y++)
                for (int x = p.X; x < p.X + p.W; x++) holes.Add(y * f.W + x);
        const int chunk = 12;
        for (int cy = 0; cy < f.H; cy += chunk)
            for (int cx = 0; cx < f.W; cx += chunk)
            {
                var ground = new SurfaceTool();
                ground.Begin(Mesh.PrimitiveType.Triangles);
                var wall = new SurfaceTool();
                wall.Begin(Mesh.PrimitiveType.Triangles);
                bool anyG = false, anyW = false;
                for (int y = cy; y < Math.Min(f.H, cy + chunk); y++)
                    for (int x = cx; x < Math.Min(f.W, cx + chunk); x++)
                    {
                        bool open = f.Floor_(x, y);
                        if (open || village)
                        {
                            if (holes.Contains(y * f.W + x)) continue;
                            float j = (0.94f + 0.1f * Hash(x, y)) * (0.86f + 0.28f * Noise(x / 5f, y / 5f));
                            Color c = open ? floorC * j : edgeC * j;
                            if (village && open)
                            {
                                // A trodden square around the waystone, paler; moss toward the edges.
                                float dc = new Vector2(x - f.W / 2f, y - f.H / 2f).Length();
                                c = c.Lerp(new Color(0.33f, 0.3f, 0.25f), Mathf.Clamp(1 - dc / 9, 0, 0.6f));
                                if (Hash(x, y, 3) < 0.3f) c = c.Lerp(new Color(0.16f, 0.2f, 0.12f), 0.35f);
                            }
                            if (f.Def.Theme == "ember" && open && Hash(x, y, 7) < 0.03f) c = c.Lerp(new Color(0.6f, 0.2f, 0.05f), 0.45f);
                            c.A = 1;
                            Quad(ground, new Vector3(x, 0, y), new Vector3(x + 1, 0, y), new Vector3(x + 1, 0, y + 1), new Vector3(x, 0, y + 1), c);
                            anyG = true;
                            continue;
                        }
                        if (!NearFloor(f, x, y)) continue;
                        float k = (0.9f + 0.15f * Hash(x, y, 1)) * (0.85f + 0.3f * Noise(x / 4f, y / 4f, 4));
                        Color wc = wallC * k, top = new Color(0.045f, 0.04f, 0.04f);
                        wc.A = 1;
                        float h = WallH;
                        Quad(wall, new Vector3(x, h, y), new Vector3(x + 1, h, y), new Vector3(x + 1, h, y + 1), new Vector3(x, h, y + 1), top);
                        if (f.Floor_(x, y + 1)) Quad(wall, new Vector3(x, h, y + 1), new Vector3(x + 1, h, y + 1), new Vector3(x + 1, 0, y + 1), new Vector3(x, 0, y + 1), wc);
                        if (f.Floor_(x, y - 1)) Quad(wall, new Vector3(x + 1, h, y), new Vector3(x, h, y), new Vector3(x, 0, y), new Vector3(x + 1, 0, y), wc * 0.8f);
                        if (f.Floor_(x + 1, y)) Quad(wall, new Vector3(x + 1, h, y + 1), new Vector3(x + 1, h, y), new Vector3(x + 1, 0, y), new Vector3(x + 1, 0, y + 1), wc * 0.9f);
                        if (f.Floor_(x - 1, y)) Quad(wall, new Vector3(x, h, y), new Vector3(x, h, y + 1), new Vector3(x, 0, y + 1), new Vector3(x, 0, y), wc * 0.75f);
                        anyW = true;
                    }
                if (anyG)
                {
                    ground.GenerateNormals();
                    _level.AddChild(new MeshInstance3D { Mesh = ground.Commit(), MaterialOverride = _ground });
                }
                if (anyW)
                {
                    wall.GenerateNormals();
                    _level.AddChild(new MeshInstance3D { Mesh = wall.Commit(), MaterialOverride = _walls });
                }
            }
        foreach (Prop p in f.Props) AddProp(f, p);
        if (village) Woods(f);
    }

    private static bool NearFloor(Floor f, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (f.Floor_(x + dx, y + dy)) return true;
        return false;
    }

    /// <summary>A quad, its corners clockwise seen from the side it faces.</summary>
    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        st.SetColor(col);
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
    }

    private static void Box(SurfaceTool st, Vector3 lo, Vector3 hi, Color col)
    {
        Quad(st, new Vector3(lo.X, hi.Y, lo.Z), new Vector3(hi.X, hi.Y, lo.Z), new Vector3(hi.X, hi.Y, hi.Z), new Vector3(lo.X, hi.Y, hi.Z), col);
        Quad(st, new Vector3(lo.X, hi.Y, hi.Z), new Vector3(hi.X, hi.Y, hi.Z), new Vector3(hi.X, lo.Y, hi.Z), new Vector3(lo.X, lo.Y, hi.Z), col * 0.8f);
        Quad(st, new Vector3(hi.X, hi.Y, lo.Z), new Vector3(lo.X, hi.Y, lo.Z), new Vector3(lo.X, lo.Y, lo.Z), new Vector3(hi.X, lo.Y, lo.Z), col * 0.7f);
        Quad(st, new Vector3(hi.X, hi.Y, hi.Z), new Vector3(hi.X, hi.Y, lo.Z), new Vector3(hi.X, lo.Y, lo.Z), new Vector3(hi.X, lo.Y, hi.Z), col * 0.85f);
        Quad(st, new Vector3(lo.X, hi.Y, lo.Z), new Vector3(lo.X, hi.Y, hi.Z), new Vector3(lo.X, lo.Y, hi.Z), new Vector3(lo.X, lo.Y, lo.Z), col * 0.65f);
    }

    private void AddProp(Floor f, Prop p)
    {
        var at = new Vector3(p.X + p.W / 2f, 0, p.Y + p.H / 2f);
        Node3D node;
        if (p.Kind is "down" or "up")
        {
            node = Stairs(p, Palette(f.Def.Theme).wall);
        }
        else
        {
            node = Kit.Prop(p.Model, (float)p.Size);
            if (node == null)
            {
                node = new Node3D();
                node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(p.W * 0.9f, (float)Math.Min(p.Size, 2), p.H * 0.9f) }, Position = new Vector3(0, (float)Math.Min(p.Size, 2) / 2, 0) });
            }
            node.Position = at;
            node.Rotation = new Vector3(0, (float)p.Rot, 0);
        }
        _level.AddChild(node);
        _props[p.Id] = node;
        if (p.Kind == "brazier") Light(at + new Vector3(0, 1.4f, 0), new Color(1f, 0.55f, 0.2f), 2.2f, 7.5f, true);
        if (p.Kind == "waystone") Light(at + new Vector3(0, 2.0f, 0), new Color(0.4f, 0.6f, 1f), Game.Hero.Waystones.Contains(Game.Depth) ? 1.6f : 0.5f, 6, false);
        if (p.Kind == "down") Light(at + new Vector3(0, -0.6f, 0), new Color(1f, 0.35f, 0.1f), 1.8f, 4, true);
        bool spent = p.OneUse && Game.Here.Opened.Contains(p.Id);
        if (spent && (p.Kind is "chest" or "crate")) OpenLid(node, false);
        if (spent && p.Breaks) Shatter(p, node, false);
        if (p.Kind == "shrine")
        {
            OmniLight3D fire = new() { Name = "ShrineLight", LightColor = new Color(1f, 0.55f, 0.2f), LightEnergy = spent ? 0 : 2.2f, OmniRange = 6, Position = new Vector3(0, 1.8f, 0) };
            node.AddChild(fire);
            if (spent) QuenchShrine(node);
        }
        if (p.Usable)
        {
            var meshes = node.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToList();
            foreach (MeshInstance3D mi in meshes) mi.MaterialOverlay = _glow;
            _usable[p.Id] = (p, meshes);
        }
    }

    // ---- things to use: a glow on them, a name under the cursor, lids that open, things that break --------------

    /// <summary>The usable props of the floor and their meshes (for the glow).</summary>
    private readonly Dictionary<int, (Prop p, List<MeshInstance3D> meshes)> _usable = new();
    /// <summary>The usable prop under the cursor (0: none), set by the screen each frame.</summary>
    public int HoverProp;
    private Label3D _hoverName;
    private readonly List<Tween3D> _tweens = new();

    /// <summary>A node moving from one transform to another over a time, along an arc (the pieces of a barrel, a lid).</summary>
    private sealed class Tween3D
    {
        public Node3D Node;
        public Transform3D From, To;
        public float T, Duration, Arc, Spin;
        public Vector3 SpinAxis;
    }

    /// <summary>
    /// The glow that marks a thing as usable: a soft ember rim that breathes, bright under the cursor, gone once it is
    /// spent (opened, broken). Things only to look at never glow.
    /// </summary>
    private void Glow()
    {
        Prop hover = null;
        foreach (var (id, (p, meshes)) in _usable)
        {
            bool spent = p.OneUse && Game.Here.Opened.Contains(p.Id);
            bool near = Game.Dist(p.X + p.W / 2.0, p.Y + p.H / 2.0, Game.Me.X, Game.Me.Y) < 24;
            float g = spent || !near ? 0 : id == HoverProp ? 1.7f : 0.5f + 0.2f * Mathf.Sin((float)_clock * 2.2f + id);
            if (id == HoverProp && !spent) hover = p;
            foreach (MeshInstance3D mi in meshes) if (IsInstanceValid(mi)) mi.SetInstanceShaderParameter("glow", g);
        }
        if (_hoverName == null)
        {
            _hoverName = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 48, PixelSize = 0.006f,
                OutlineSize = 10, Modulate = UITheme.Ember.Lightened(0.3f), Font = UITheme.Title,
            };
            _dyn.AddChild(_hoverName);
        }
        _hoverName.Visible = hover != null;
        if (hover != null)
        {
            _hoverName.Text = hover.Title;
            float h = hover.Kind is "down" ? 0.6f : hover.Kind is "up" ? 1.4f : (float)Math.Min(hover.Size, 2.4) + 0.45f;
            _hoverName.Position = new Vector3(hover.X + hover.W / 2f, h, hover.Y + hover.H / 2f);
        }
    }

    private void Tweens(float dt)
    {
        foreach (Tween3D tw in _tweens.ToList())
        {
            tw.T += dt;
            float t = Mathf.Clamp(tw.T / tw.Duration, 0, 1);
            if (!IsInstanceValid(tw.Node)) { _tweens.Remove(tw); continue; }
            Vector3 pos = tw.From.Origin.Lerp(tw.To.Origin, t) + new Vector3(0, tw.Arc * 4 * t * (1 - t), 0);
            Basis rot = tw.From.Basis.Slerp(tw.To.Basis, t);
            if (tw.Spin != 0) rot = new Basis(tw.SpinAxis, tw.Spin * (1 - t) * t * 4) * rot;
            tw.Node.Transform = new Transform3D(rot, pos);
            if (t >= 1) { tw.Node.Transform = tw.To; _tweens.Remove(tw); }
        }
    }

    /// <summary>A used shrine: its fire and candles go out, its light dies.</summary>
    private static void QuenchShrine(Node3D shrine)
    {
        foreach (Node n in shrine.FindChildren("*", "", true, false))
        {
            string name = n.Name.ToString();
            if (n is Node3D n3 && (name.Contains("flame") || name.Contains("fire_core") || name == "embers")) n3.Visible = false;
        }
        if (shrine.GetNodeOrNull<OmniLight3D>("ShrineLight") is OmniLight3D l) l.LightEnergy = 0;
    }

    /// <summary>A chest's or crate's lid swings up on its hinge (or is simply open, when the floor is built again).</summary>
    private void OpenLid(Node3D prop, bool animate)
    {
        if (Kit.FindNamed(prop, "lid") is not Node3D lid) return;
        Transform3D shut = lid.Transform;
        var open = new Transform3D(new Basis(Vector3.Right, -1.85f) * shut.Basis, shut.Origin);
        if (!animate) { lid.Transform = open; return; }
        _tweens.Add(new Tween3D { Node = lid, From = shut, To = open, Duration = 0.55f });
    }

    /// <summary>
    /// A barrel or an urn bursts: the model goes, and its pieces (staves and hoops, or glazed shards) fly out and come
    /// to rest around where it stood, where they stay. The resting places come from the prop's id, so a floor built
    /// again shows the same wreck.
    /// </summary>
    private void Shatter(Prop p, Node3D node, bool animate)
    {
        node.Visible = false;
        var wreck = new Node3D { Name = $"Wreck{p.Id}", Position = node.Position };
        _level.AddChild(wreck);
        bool urn = p.Kind == "urn";
        float size = (float)p.Size;
        var wood = new StandardMaterial3D { AlbedoColor = urn ? new Color(0.5f, 0.2f, 0.12f) : new Color(0.36f, 0.24f, 0.14f), Roughness = 0.9f };
        var band = new StandardMaterial3D { AlbedoColor = urn ? new Color(0.78f, 0.72f, 0.58f) : new Color(0.24f, 0.22f, 0.21f), Roughness = 0.6f, Metallic = urn ? 0 : 0.6f };
        int n = urn ? 11 : 12;
        for (int k = 0; k < n; k++)
        {
            float h1 = Hash(p.Id, k, 1), h2 = Hash(p.Id, k, 2), h3 = Hash(p.Id, k, 3);
            Mesh mesh;
            Material mat = wood;
            if (!urn && k >= 10) { mesh = new TorusMesh { InnerRadius = size * 0.3f, OuterRadius = size * 0.34f, Rings = 16 }; mat = band; }
            else if (urn) { mesh = new BoxMesh { Size = new Vector3(0.1f + 0.12f * h1, 0.03f, 0.08f + 0.12f * h2) * size / 0.7f }; if (k % 4 == 0) mat = band; }
            else mesh = new BoxMesh { Size = new Vector3(0.09f, 0.025f, size * 0.62f) };
            var piece = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat };
            wreck.AddChild(piece);
            float ang = (k + h1 * 0.8f) / n * Mathf.Tau, dist = 0.25f + 0.65f * h2 * size;
            float lieY = mesh is TorusMesh ? 0.03f : 0.02f;
            var rest = new Transform3D(new Basis(Vector3.Up, h3 * Mathf.Tau), new Vector3(Mathf.Cos(ang) * dist, lieY, Mathf.Sin(ang) * dist));
            if (!animate) { piece.Transform = rest; continue; }
            var start = new Transform3D(new Basis(Vector3.Up, ang) * new Basis(Vector3.Right, 1.4f), new Vector3(Mathf.Cos(ang) * 0.15f, size * (0.25f + 0.5f * h3), Mathf.Sin(ang) * 0.15f));
            piece.Transform = start;
            _tweens.Add(new Tween3D { Node = piece, From = start, To = rest, Duration = 0.45f + 0.25f * h1, Arc = 0.5f + 0.6f * h2, Spin = 6 + 6 * h3, SpinAxis = new Vector3(h1 - 0.5f, 0.3f, h2 - 0.5f).Normalized() });
        }
        if (animate)
        {
            // A puff of dust (or a flash of old ash from an urn).
            var puff = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.6f, Height = 1.2f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.4f, 0.35f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha }, Position = node.Position + new Vector3(0, 0.4f, 0) };
            AddFx(puff, 0.6f, (fx, t) => { fx.Node.Scale = Vector3.One * (0.5f + t * 1.4f); Fade(fx.Node, 0.5f * (1 - t)); });
        }
    }

    private void Light(Vector3 at, Color c, float energy, float range, bool flicker)
    {
        var l = new OmniLight3D { LightColor = c, LightEnergy = energy, OmniRange = range, OmniAttenuation = 1.3f, Position = at };
        _level.AddChild(l);
        if (flicker) _flicker.Add((l, energy, (float)(at.X * 1.7 + at.Z)));
    }

    /// <summary>Stairs: down, a pit stepping into ember light; up, three steps against the wall.</summary>
    private static Node3D Stairs(Prop p, Color stone)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        stone.A = 1;
        if (p.Kind == "down")
        {
            float x0 = p.X, x1 = p.X + p.W, y0 = p.Y, y1 = p.Y + p.H;
            for (int k = 0; k < 4; k++)
            {
                float a = y1 - (y1 - y0) * (k + 1) / 4f, b = y1 - (y1 - y0) * k / 4f;
                Box(st, new Vector3(x0, -2.5f, a), new Vector3(x1, -0.3f - 0.4f * k, b), stone * (0.8f - 0.12f * k));
            }
            Color dark = stone * 0.35f; dark.A = 1;
            Quad(st, new Vector3(x0, 0, y0), new Vector3(x1, 0, y0), new Vector3(x1, -2.5f, y0), new Vector3(x0, -2.5f, y0), dark);
            Quad(st, new Vector3(x1, 0, y0), new Vector3(x1, 0, y1), new Vector3(x1, -2.5f, y1), new Vector3(x1, -2.5f, y0), dark);
            Quad(st, new Vector3(x0, 0, y1), new Vector3(x0, 0, y0), new Vector3(x0, -2.5f, y0), new Vector3(x0, -2.5f, y1), dark);
            // A low kerb around it, so it reads as a hole from above.
            Box(st, new Vector3(x0 - 0.15f, 0, y0 - 0.15f), new Vector3(x1 + 0.15f, 0.12f, y0), stone);
            Box(st, new Vector3(x0 - 0.15f, 0, y0), new Vector3(x0, 0.12f, y1), stone);
            Box(st, new Vector3(x1, 0, y0), new Vector3(x1 + 0.15f, 0.12f, y1), stone);
        }
        else
        {
            for (int k = 0; k < 3; k++)
                Box(st, new Vector3(p.X, 0, p.Y + k * 0.33f), new Vector3(p.X + p.W, 0.9f - 0.3f * k, p.Y + (k + 1) * 0.33f), stone * (1.05f - 0.1f * k));
        }
        st.GenerateNormals();
        var node = new Node3D();
        node.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1 } });
        return node;
    }

    /// <summary>Dead woods all round the village, on the rock past its edge (only to look at).</summary>
    private void Woods(Floor f)
    {
        for (int y = 0; y < f.H; y += 2)
            for (int x = 0; x < f.W; x += 2)
            {
                if (f.Floor_(x, y) || Hash(x, y, 5) < 0.35f) continue;
                if (x > 2 && y > 2 && x < f.W - 3 && y < f.H - 3 && Hash(x, y, 6) < 0.5f) continue;
                Node3D tree = Kit.Prop("tree", 3.5f + 1.5f * Hash(x, y, 8));
                if (tree == null) return;
                tree.Position = new Vector3(x + 0.5f + Hash(x, y, 9) - 0.5f, 0, y + 0.5f + Hash(x, y, 10) - 0.5f);
                tree.Rotation = new Vector3(0, Hash(x, y, 11) * Mathf.Tau, 0);
                _level.AddChild(tree);
            }
    }

    // ---- keeping up with the game -----------------------------------------------------------------------------------

    public static Vector3 At(double x, double y, float h = 0) => new((float)x, h, (float)y);

    /// <summary>Bring the scene up to date with the game; called every frame after the game has stepped.</summary>
    public void Sync(double dt)
    {
        if (Game == null) return;
        _clock += dt;
        if (_builtDepth != Game.Depth || _builtSeed != Game.Seed) Build();
        Actors((float)dt);
        Drops();
        Missiles((float)dt);
        Zones();
        Portal();
        Effects((float)dt);
        Tweens((float)dt);
        Glow();
        foreach (var (l, e, ph) in _flicker) l.LightEnergy = e * (0.85f + 0.15f * Mathf.Sin((float)_clock * 9 + ph) * Mathf.Sin((float)_clock * 5.3f + ph * 2));
        Vector3 hero = At(Game.Me.X, Game.Me.Y);
        _camAt = _camAt == Vector3.Zero || _camAt.DistanceTo(hero) > 12 ? hero : _camAt.Lerp(hero, Mathf.Min(1, (float)dt * 8));
        Vector3 off = new Vector3(9.5f, 14.5f, 9.5f) * Zoom;
        Vector3 shake = _shake > 0 ? new Vector3(Mathf.Sin((float)_clock * 61), Mathf.Sin((float)_clock * 47), 0) * _shake * 0.25f : Vector3.Zero;
        _shake = Mathf.Max(0, _shake - (float)dt * 2);
        Cam.Position = _camAt + off + shake;
        Cam.LookAt(_camAt + new Vector3(0, 0.6f, 0) + shake, Vector3.Up);
        _heroLight.Position = hero + new Vector3(0, 2.6f, 0);
        _walls.SetShaderParameter("hero_pos", hero + new Vector3(0, 1, 0));
        _walls.SetShaderParameter("cam_pos", Cam.GlobalPosition);
        _walls.SetShaderParameter("cut", Game.Depth == 0 ? 0f : 1f);
    }

    private void Actors(float dt)
    {
        var live = new HashSet<int> { 0 };
        foreach (Actor a in Game.Here.Actors) live.Add(a.Id);
        foreach (int id in _figs.Keys.Where(id => !live.Contains(id)).ToList()) { _figs[id].QueueFree(); _figs.Remove(id); }
        Show(Game.Me, dt);
        foreach (Actor a in Game.Here.Actors) Show(a, dt);
        if (_shield == null)
        {
            _shield = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.95f, Height = 1.9f },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.4f, 0.6f, 1f, 0.18f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    EmissionEnabled = true, Emission = new Color(0.3f, 0.5f, 1f), EmissionEnergyMultiplier = 0.6f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            };
            _dyn.AddChild(_shield);
        }
        _shield.Visible = Game.Me.ShieldT > 0 && Game.Me.Alive;
        if (_figs.TryGetValue(0, out Figure3D me)) _shield.Position = me.Position + new Vector3(0, 0.95f, 0);
    }

    private Figure3D MakeFigure(Actor a)
    {
        string body; float h;
        if (a.Hero) { body = Data.HeroBody(Game.Hero); h = Game.Hero.Female ? 1.72f : 1.8f; }
        else if (a.Npc) { body = "body-" + a.Def; h = a.Def == "smith" ? 1.85f : 1.7f; }
        else
        {
            MonDef m = a.M;
            body = m.Body;
            h = (float)m.Height * (a.Champion ? 1.12f : 1) * (a.Title != null && !a.Champion && m.Ai != Ai.Boss ? 1.2f : 1);
        }
        var fig = new Figure3D(body, h) { Name = a.Hero ? "Hero" : $"A{a.Id}" };
        _dyn.AddChild(fig);
        fig.Position = At(a.X, a.Y);
        if (!fig.Loaded)
        {
            // No model yet (still being made): a plain pillar of the right height, so it can be seen and fought.
            fig.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = (float)a.Radius, Height = h }, Position = new Vector3(0, h / 2, 0) });
        }
        if (!a.Hero && !a.Npc)
        {
            MonDef m = a.M;
            fig.Hold(m.Right, null, m.Left == "shield" ? "shield" : null);
            if (a.Champion)
            {
                // A champion glows with its first trait's colour: a tint and a haze of motes around it.
                Color tc = TraitColour(a.Traits.FirstOrDefault());
                fig.Tint(new Color(1, 1, 1).Lerp(tc * 1.4f, 0.35f));
                var aura = Vfx.Trail(tc * 1.6f, tc * 0.5f, 0.18f, 24, 0.9f);
                aura.EmissionSphereRadius = 0.5f;
                aura.Gravity = new Vector3(0, 0.9f, 0);
                aura.Position = new Vector3(0, h * 0.4f, 0);
                fig.AddChild(aura);
            }
            else if (a.Title != null && m.Ai != Ai.Boss) fig.Tint(new Color(1.2f, 1.05f, 0.7f));
        }
        if (a.Npc)
        {
            var name = new Label3D
            {
                Text = a.Def switch { "smith" => "Oskar the Smith", "healer" => "Maren the Healer", _ => "Elder Corwin" },
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 40, PixelSize = 0.006f,
                Modulate = new Color(0.95f, 0.85f, 0.6f), OutlineSize = 10, Position = new Vector3(0, h + 0.35f, 0),
            };
            fig.AddChild(name);
            if (a.Def == "smith") fig.Hold("sword", null, null);
        }
        fig.Rotation = new Vector3(0, (float)a.Facing, 0);
        return fig;
    }

    private void Show(Actor a, float dt)
    {
        if (!_figs.TryGetValue(a.Id, out Figure3D fig)) _figs[a.Id] = fig = MakeFigure(a);
        Vector3 want = At(a.X, a.Y);
        float d = want.DistanceTo(fig.Position);
        fig.Position = d > 3 ? want : fig.Position.Lerp(want, Mathf.Min(1, dt * 18));
        if (a.Act == "leap")
        {
            float t = (float)Math.Min(1, a.ActT / Math.Max(0.01, a.ActAt));
            fig.Position = new Vector3(fig.Position.X, Mathf.Sin(t * Mathf.Pi) * 2.2f, fig.Position.Z);
        }
        fig.Rotation = new Vector3(0, Mathf.LerpAngle(fig.Rotation.Y, (float)a.Facing, Mathf.Min(1, dt * 14)), 0);
        float far = Game.Depth == 0 ? 40 : 26;
        fig.Visible = a.Hero || Game.Dist(a, Game.Me) < far;
        if (!fig.Visible) return;
        if (a.Hero)
        {
            Dress(fig, Game.Hero);
            // Smoke-faded while hidden: tinted only when that changes (not every frame).
            if ((a.Hidden > 0) != _heroHidden) { _heroHidden = a.Hidden > 0; fig.Tint(Colors.White, _heroHidden ? 0.35f : 1); }
        }
        Animate(a, fig);
    }

    /// <summary>
    /// The hero's figure as equipped: the weapon in the right hand (a bow too: the shooting clip holds the right arm
    /// out, the left hand at its wrist), the shield on the left forearm. The title shows the hero the same way.
    /// </summary>
    public static void Dress(Figure3D fig, Hero h)
    {
        string held = h.Weapon?.B.Held;
        fig.Hold(held, null, h.Equip[(int)Slot.Shield] != null ? "shield" : null);
        if (fig.BodyName is "body-man" or "body-woman") fig.Wear(Outfit(h));
    }

    /// <summary>
    /// The pieces a hero on a base body wears (only those that have been made): the armour worn, by its base; with no
    /// helm the rogue's hood or the hair; trousers, or the sorcerer's robe; each class's cloak, cape or mantle.
    /// </summary>
    public static List<(string piece, string slot)> Outfit(Hero h)
    {
        string tag = h.Female ? "f" : "m";
        var list = new List<(string, string)>();
        void Add(string piece, string slot) { if (piece != null && Kit.Has($"wear-{tag}-{piece}")) list.Add(($"wear-{tag}-{piece}", slot)); }
        Add(h.Equip[(int)Slot.Body]?.Base, "torso");
        string helm = h.Equip[(int)Slot.Head]?.Base;
        Add(helm ?? (h.Class == Cls.Rogue ? "rogue-hood" : "hair"), "head");
        if (helm == null && h.Class == Cls.Rogue && !Kit.Has($"wear-{tag}-rogue-hood")) Add("hair", "head");
        Add(h.Equip[(int)Slot.Hands]?.Base, "arms");
        Add(h.Equip[(int)Slot.Feet]?.Base, "feet");
        Add(h.Class == Cls.Sorcerer ? "sorcerer-robe" : "trousers", "legs");
        Add(h.Class switch { Cls.Wanderer => "wanderer-cloak", Cls.Rogue => "rogue-cape", _ => "sorcerer-mantle" }, "back");
        return list;
    }

    private void Animate(Actor a, Figure3D fig)
    {
        bool fresh = !_last.TryGetValue(a.Id, out var last) || a.ActT < last.t - 1e-4 || a.Act != last.clip;
        _last[a.Id] = ((float)a.ActT, a.Act);
        if (fig.Quad)
        {
            switch (a.Act)
            {
                case "dead": fig.Play("death"); return;
                case "attack": fig.Play("bite", Speed(fig, "bite", a.ActDur), fresh); return;
                case "walk": fig.Play(a.M?.Speed > 3 ? "run" : "walk", 1.2f); return;
                default: fig.Play("idle"); return;
            }
        }
        switch (a.Act)
        {
            case "dead": fig.Play("death"); return;
            case "hit": fig.Play("hit", 1.6f, fresh); return;
            case "walk":
                if (a.Hero) fig.Play("run", (float)(Game.Hero.S.MoveSpeed / 4.6) * (a.Slow > 0 ? 0.6f : 1), false, "walk");
                else
                {
                    double spd = a.M?.Speed ?? 2;
                    fig.Play(spd > 3.2 ? "run" : "walk", (float)(spd / (spd > 3.2 ? 4.6 : 1.8)) * (a.Slow > 0 ? 0.6f : 1), false, "walk");
                }
                return;
            case "attack" or "cast" or "leap":
            {
                string clip = ClipFor(a);
                fig.Play(clip, Speed(fig, clip, a.ActDur), fresh, clip == "shoot" ? "point" : "slash", "punch");
                return;
            }
            default:
                if (a.Npc) fig.Play(a.Def == "smith" ? "hammer" : a.Def == "elder" ? "idle" : "idle");
                else if (a.Hero)
                {
                    bool fight = Game.Foes.Any(f => f.Alive && f.Awake && Game.Dist(f, a) < (Game.Hero.S.Ranged ? 12 : 6));
                    fig.Play(!fight || Game.Hero.Weapon == null ? "idle" : Game.Hero.S.Ranged ? "aim" : "ready", 1, false, "idle");
                }
                else fig.Play(a.Awake ? (a.M?.Clip == "shoot" ? "aim" : "ready") : "idle", 1, false, "idle");
                return;
        }
    }

    private static float Speed(Figure3D fig, string clip, double dur) => dur > 0.01 && fig.Length(clip) > 0 ? fig.Length(clip) / (float)dur : 1;

    /// <summary>Which clip an action plays: by the skill for the hero, by the kind for a monster.</summary>
    private string ClipFor(Actor a)
    {
        if (a.Hero)
        {
            string s = a.ActSkill;
            bool ranged = Game.Hero.S.Ranged;
            return s switch
            {
                "leap" => "jump",
                "warcry" or "meteor" => "cheer",
                "quake" => "smash",
                "nova" => "crouch",
                "shield" => "block",
                "step" => "crouch",
                "cleave" => "slash",
                "bash" => "overhead",
                "firebolt" or "chain" => "point",
                "power" or "multi" or "flurry" or "rain" => "shoot",
                _ => ranged ? "shoot" : (a.Id + (int)(Game.Time / 0.9)) % 2 == 0 ? "slash" : "overhead",
            };
        }
        MonDef m = a.M;
        if (m == null) return "slash";
        if (a.Act == "cast") return a.ActSkill == "volley" ? "cheer" : "point";
        return m.Ai == Ai.Boss ? (a.ActT < 0.05 && (int)(Game.Time) % 2 == 0 ? "smash" : "overhead") : m.Clip;
    }

    /// <summary>The drop under the cursor (its name always shows), and whether every name shows (Alt held, a touchscreen).</summary>
    public int HoverDrop;
    public bool AllLabels;

    /// <summary>
    /// A drop's name: shown for gold, draughts and anything magic or better, for plain things only while Alt is held
    /// or it is under the cursor; names of drops lying close together stack up instead of covering each other.
    /// </summary>
    private void Labels(Drop d, Node3D node)
    {
        if (node.GetNodeOrNull<Label3D>("Name") is not Label3D label) return;
        Item it = d.Item;
        bool plain = it.Rarity == Rarity.Plain && it.B?.Wearable == true;
        label.Visible = !plain || AllLabels || d.Id == HoverDrop;
        if (!label.Visible) return;
        int rank = 0;
        foreach (Drop o in Game.Here.Drops)
            if (o.Id < d.Id && Game.Dist(o.X, o.Y, d.X, d.Y) < 1.1 && (o.Item.Rarity != Rarity.Plain || o.Item.B?.Wearable != true || AllLabels || o.Id == HoverDrop)) rank++;
        label.Position = new Vector3(0, 0.6f + 0.24f * rank, 0);
    }

    /// <summary>Drops that have just fallen (from a monster, a chest), by id: when, so they tumble down.</summary>
    private readonly Dictionary<int, double> _falling = new();

    private void Drops()
    {
        var live = new HashSet<int>(Game.Here.Drops.Select(d => d.Id));
        foreach (int id in _drops.Keys.Where(id => !live.Contains(id)).ToList()) { _drops[id].QueueFree(); _drops.Remove(id); _falling.Remove(id); }
        foreach (Drop d in Game.Here.Drops)
        {
            if (!_drops.TryGetValue(d.Id, out Node3D node))
            {
                node = new Node3D { Position = At(d.X, d.Y) };
                Node3D body = DropBody(d.Item);
                body.Name = "Body";
                body.Rotation = new Vector3(0, Hash(d.Id, 7) * Mathf.Tau, 0);
                node.AddChild(body);
                var label = new Label3D
                {
                    Name = "Name",
                    Text = d.Item.Base == "gold" ? $"{d.Item.Count} gold" : d.Item.Title, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true, FontSize = 28, PixelSize = 0.006f, OutlineSize = 8, Modulate = RarityColour(d.Item), Position = new Vector3(0, 0.6f, 0),
                };
                node.AddChild(label);
                _dyn.AddChild(node);
                _drops[d.Id] = node;
            }
            Labels(d, node);
            if (_falling.TryGetValue(d.Id, out double born))
            {
                // Thrown up out of the body and tumbling down onto the floor, about half a second.
                float t = (float)(_clock - born);
                var body = node.GetNode<Node3D>("Body");
                const float land = 0.486f;
                if (t >= land) { _falling.Remove(d.Id); body.Position = Vector3.Zero; body.Rotation = new Vector3(0, body.Rotation.Y, 0); continue; }
                body.Position = new Vector3(0, Mathf.Max(0, 0.8f + 2f * t - 7.5f * t * t), 0);
                body.Rotation = new Vector3((1 - t / land) * Mathf.Tau * 1.25f, body.Rotation.Y, 0);
            }
        }
    }

    /// <summary>
    /// What an item looks like on the floor: its own floor model (assets/models/drop-BASE) if there is one, else the
    /// model a hero holds it as (a sword, a bow, a shield) lying flat, else its icon cut out of its background and laid
    /// on the floor, sized as the thing would be.
    /// </summary>
    public static Node3D DropBody(Item it)
    {
        Base b = it.B;
        Node3D m = Kit.Lying("drop-" + it.Base, "models");
        if (m == null && b?.Held != null) m = Kit.Lying("held-" + b.Held, "figures", 1.1f);
        if (m != null) return m;
        float size = b?.Kind switch
        {
            Kind.Body => 0.75f,
            Kind.Head or Kind.Hands or Kind.Feet => 0.5f,
            Kind.Ring => 0.28f,
            Kind.Amulet or Kind.PotLife or Kind.PotMana => 0.36f,
            Kind.Gem => 0.25f,
            Kind.Gold => it.Count > 100 ? 0.6f : 0.45f,
            _ => 0.45f,
        };
        var holder = new Node3D();
        Texture2D t = Kit.Cutout(b?.Icon);
        if (t != null)
            holder.AddChild(new Sprite3D
            {
                Texture = t, Axis = Vector3.Axis.Y, PixelSize = size / t.GetWidth(), Shaded = true, DoubleSided = false,
                AlphaCut = SpriteBase3D.AlphaCutMode.Disabled, Position = new Vector3(0, 0.02f, 0), TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            });
        return holder;
    }

    public static Color TraitColour(string trait) => trait switch
    {
        "fire" => new Color(1f, 0.45f, 0.12f),
        "frost" => new Color(0.5f, 0.8f, 1f),
        "swift" => new Color(1f, 0.9f, 0.4f),
        "stone" => new Color(0.7f, 0.68f, 0.62f),
        "vampiric" => new Color(0.65f, 0.25f, 0.8f),
        "mighty" => new Color(0.95f, 0.2f, 0.15f),
        _ => new Color(1f, 0.6f, 0.5f),
    };

    public static Color RarityColour(Item it) => it.Base == "gold" ? new Color(0.95f, 0.8f, 0.35f) : it.Rarity switch
    {
        Rarity.Magic => new Color(0.5f, 0.6f, 1f),
        Rarity.Rare => new Color(1f, 0.9f, 0.35f),
        Rarity.Unique => new Color(0.85f, 0.6f, 0.3f),
        _ => new Color(0.88f, 0.88f, 0.86f),
    };

    private static StandardMaterial3D Glow(Color c, float energy = 2.5f) => new()
    {
        AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private static Color ElemColour(Elem e) => e switch
    {
        Elem.Fire => new Color(1f, 0.45f, 0.12f),
        Elem.Cold => new Color(0.55f, 0.8f, 1f),
        Elem.Light => new Color(0.75f, 0.8f, 1f),
        _ => new Color(0.9f, 0.85f, 0.75f),
    };

    private void Missiles(float dt)
    {
        var live = new HashSet<int>(Game.Here.Missiles.Select(m => m.Id));
        foreach (int id in _missiles.Keys.Where(id => !live.Contains(id)).ToList())
        {
            // Its trail stays to fade out where it ended.
            if (_missiles[id].GetNodeOrNull<CpuParticles3D>("Trail") is CpuParticles3D trail)
            {
                trail.Emitting = false;
                Transform3D where = trail.GlobalTransform;
                trail.GetParent().RemoveChild(trail);
                AddFx(trail, (float)trail.Lifetime + 0.1f, null);
                trail.GlobalTransform = where;
            }
            _missiles[id].QueueFree();
            _missiles.Remove(id);
        }
        foreach (Missile m in Game.Here.Missiles)
        {
            if (!_missiles.TryGetValue(m.Id, out Node3D node))
            {
                node = new Node3D();
                if (m.Kind == "arrow")
                    node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.05f, 0.75f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.38f, 0.25f) } });
                else
                {
                    Color c = ElemColour(m.Elem);
                    bool fire = m.Kind is "firebolt" or "ember";
                    node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = fire ? 0.16f : 0.13f, Height = fire ? 0.32f : 0.26f }, MaterialOverride = Glow(fire ? new Color(1f, 0.85f, 0.5f) : c, 4) });
                    // A halo round the core, and a trail behind it: flames for fire, a frosty mist for cold.
                    node.AddChild(new Sprite3D { Texture = Vfx.Soft, PixelSize = 0.012f, Modulate = c * 1.4f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Shaded = false, Transparent = true, AlphaCut = SpriteBase3D.AlphaCutMode.Disabled });
                    CpuParticles3D trail = fire
                        ? Vfx.Trail(new Color(1f, 0.8f, 0.35f) * 1.8f, new Color(0.8f, 0.2f, 0.05f), 0.32f, m.Kind == "ember" ? 30 : 50, 0.4f)
                        : Vfx.Trail(new Color(0.8f, 0.95f, 1f) * 1.5f, new Color(0.3f, 0.5f, 0.9f), 0.22f, 35, 0.45f);
                    trail.Name = "Trail";
                    node.AddChild(trail);
                    if (fire && m.Kind == "firebolt") node.AddChild(new OmniLight3D { LightColor = c, LightEnergy = 1.6f, OmniRange = 3.5f });
                }
                _dyn.AddChild(node);
                _missiles[m.Id] = node;
            }
            node.Position = At(m.X, m.Y, 1.1f);
            node.Rotation = new Vector3(0, Mathf.Atan2((float)m.VX, (float)m.VY), 0);
        }
    }

    private void Zones()
    {
        var live = new HashSet<int>(Game.Here.Zones.Select(z => z.Id));
        foreach (int id in _zones.Keys.Where(id => !live.Contains(id)).ToList()) { _zones[id].QueueFree(); _zones.Remove(id); }
        foreach (Zone z in Game.Here.Zones)
        {
            if (!_zones.TryGetValue(z.Id, out Node3D node))
            {
                node = new Node3D { Position = At(z.X, z.Y) };
                if (z.Kind == "meteor")
                {
                    var rock = new MeshInstance3D { Name = "Rock", Mesh = new SphereMesh { Radius = 0.6f, Height = 1.2f, RadialSegments = 7, Rings = 4 }, MaterialOverride = Glow(new Color(1f, 0.4f, 0.1f), 3) };
                    node.AddChild(rock);
                    rock.AddChild(Vfx.Trail(new Color(1f, 0.8f, 0.4f) * 2f, new Color(0.5f, 0.12f, 0.04f), 1.1f, 90, 0.6f));
                    rock.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.5f, 0.2f), LightEnergy = 3, OmniRange = 8 });
                    var mark = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = (float)z.R - 0.15f, OuterRadius = (float)z.R }, MaterialOverride = Glow(new Color(1f, 0.3f, 0.1f), 1.5f), Position = new Vector3(0, 0.05f, 0) };
                    node.AddChild(mark);
                }
                else if (z.Kind == "burn")
                {
                    // A soft glowing patch (brightest in the middle) for the flames to rise from.
                    MeshInstance3D patch = Vfx.Mark(new Color(1f, 0.35f, 0.08f, 0.7f), (float)z.R * 1.15f);
                    ((StandardMaterial3D)patch.MaterialOverride).BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                    patch.Name = "Disc";
                    patch.Position = new Vector3(0, 0.03f, 0);
                    patch.Visible = false;
                    node.AddChild(patch);
                    CpuParticles3D flames = Vfx.Flames((float)z.R);
                    flames.Name = "Flames";
                    flames.Emitting = false;
                    node.AddChild(flames);
                }
                _dyn.AddChild(node);
                _zones[z.Id] = node;
            }
            if (z.Kind == "meteor" && node.GetNodeOrNull<Node3D>("Rock") is Node3D r) r.Position = new Vector3(-(float)z.Delay * 4, (float)z.Delay * 14 + 0.6f, -(float)z.Delay * 4);
            if (z.Kind == "burn" && node.GetNodeOrNull<Node3D>("Disc") is Node3D disc)
            {
                disc.Visible = z.Delay <= 0;
                disc.Scale = Vector3.One * (0.9f + 0.1f * Mathf.Sin((float)_clock * 10));
                if (node.GetNodeOrNull<CpuParticles3D>("Flames") is CpuParticles3D fl) fl.Emitting = z.Delay <= 0 && z.Left > 0.3;
            }
        }
    }

    private void Portal()
    {
        bool want = Game.Depth == 0 && Game.PortalDepth > 0;
        if (want && _portal == null)
        {
            _portal = new Node3D { Position = At(Game.PortalSpot.x, Game.PortalSpot.y) };
            var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.75f, OuterRadius = 0.95f }, MaterialOverride = Glow(new Color(0.35f, 0.55f, 1f), 3), RotationDegrees = new Vector3(90, 0, 0), Position = new Vector3(0, 1.1f, 0) };
            _portal.AddChild(ring);
            var inner = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.75f, BottomRadius = 0.75f, Height = 0.02f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.35f, 0.9f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }, RotationDegrees = new Vector3(90, 0, 0), Position = new Vector3(0, 1.1f, 0) };
            _portal.AddChild(inner);
            _portal.AddChild(new OmniLight3D { LightColor = new Color(0.4f, 0.6f, 1f), LightEnergy = 2, OmniRange = 5, Position = new Vector3(0, 1.2f, 0) });
            _level.AddChild(_portal);
        }
        if (!want && _portal != null) { _portal.QueueFree(); _portal = null; }
        if (_portal != null) _portal.Rotation = new Vector3(0, (float)_clock * 0.8f, 0);
    }

    // ---- effects ----------------------------------------------------------------------------------------------------

    private void AddFx(Node3D node, float life, Action<Fx, float> tick)
    {
        _dyn.AddChild(node);
        _fx.Add(new Fx { Node = node, Life = life, Tick = tick });
    }

    private void Effects(float dt)
    {
        foreach (Fx fx in _fx.ToList())
        {
            fx.T += dt;
            if (fx.T >= fx.Life) { fx.Node.QueueFree(); _fx.Remove(fx); continue; }
            fx.Tick?.Invoke(fx, fx.T / fx.Life);
        }
    }

    private static void Fade(Node3D n, float a)
    {
        foreach (MeshInstance3D mi in n.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().Prepend(n as MeshInstance3D).Where(m => m != null))
            if (mi.MaterialOverride is StandardMaterial3D m)
            {
                m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                Color c = m.AlbedoColor; c.A = a; m.AlbedoColor = c;
            }
    }

    private void Ring(Vector3 at, float to, Color c, float life, float h = 0.3f)
    {
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.85f, OuterRadius = 1f }, MaterialOverride = Glow(c, 2.5f), Position = at + new Vector3(0, h, 0) };
        AddFx(ring, life, (fx, t) => { fx.Node.Scale = new Vector3(0.3f + to * t, 1, 0.3f + to * t); Fade(fx.Node, 1 - t); });
    }

    /// <summary>Show what the game says happened (the screen does the sounds and the words).</summary>
    public void OnEvent(Ev e)
    {
        Vector3 at = At(e.X, e.Y);
        switch (e.Kind)
        {
            case "nova":
                Ring(at, e.V, ElemColour(Elem.Cold), 0.45f);
                Vfx.Ring(_dyn, at + new Vector3(0, 0.4f, 0), new Color(0.85f, 0.97f, 1f) * 1.8f, new Color(0.35f, 0.6f, 1f), 90, e.V * 2.2f, 0.28f, 0.55f, true, Vfx.Shard);
                Vfx.Ring(_dyn, at + new Vector3(0, 0.15f, 0), new Color(0.7f, 0.85f, 1f, 0.5f), new Color(0.5f, 0.6f, 0.8f), 40, e.V * 1.4f, 0.9f, 0.9f, false);
                Flash(at + new Vector3(0, 1, 0), ElemColour(Elem.Cold), 4, e.V * 1.5f, 0.3f);
                break;
            case "warcry":
                for (int k = 0; k < 3; k++) Ring(at, 5 - k, new Color(1f, 0.8f, 0.35f), 0.35f + 0.12f * k, 0.6f + 0.5f * k);
                Vfx.Ring(_dyn, at + new Vector3(0, 0.1f, 0), new Color(0.6f, 0.5f, 0.4f, 0.6f), new Color(0.4f, 0.35f, 0.3f), 40, 6, 0.8f, 0.7f, false);
                Vfx.Burst(_dyn, at + new Vector3(0, 1.6f, 0), new Color(1f, 0.85f, 0.4f) * 1.6f, new Color(1f, 0.5f, 0.1f), 30, 2, 4, 0.18f, 0.5f, Vector3.Zero);
                break;
            case "quake":
                Ring(at, e.V, new Color(0.7f, 0.5f, 0.3f), 0.6f, 0.1f);
                Ring(at, e.V * 0.6f, new Color(0.9f, 0.6f, 0.3f), 0.45f, 0.1f);
                Vfx.Ring(_dyn, at + new Vector3(0, 0.2f, 0), new Color(0.55f, 0.45f, 0.35f, 0.7f), new Color(0.35f, 0.3f, 0.25f), 70, e.V * 1.6f, 1.1f, 1.1f, false);
                Vfx.Burst(_dyn, at, new Color(0.45f, 0.38f, 0.32f), new Color(0.3f, 0.25f, 0.2f), 40, 3, 7, 0.22f, 1.1f, new Vector3(0, -14, 0), 35, Vector3.Up, false, Vfx.Shard, e.V * 0.6f, 0.9f);
                Cracks(at, e.V);
                _shake = 1.2f;
                break;
            case "arc":
                Ring(At(Game.Me.X, Game.Me.Y), 1.6f, new Color(0.9f, 0.9f, 1f), 0.18f, 1f);
                Vfx.Ring(_dyn, At(Game.Me.X, Game.Me.Y, 1f), new Color(1f, 1f, 1f) * 1.4f, new Color(0.6f, 0.7f, 1f), 30, 5, 0.12f, 0.25f, true, Vfx.Shard);
                break;
            case "cast":
            {
                // A glow gathering at the hands as a spell is cast.
                Actor caster = Game.Find(e.A);
                if (caster == null) break;
                Color c = e.Text switch { "nova" => ElemColour(Elem.Cold), "chain" => ElemColour(Elem.Light), "shield" => new Color(0.4f, 0.6f, 1f), "warcry" => new Color(1f, 0.8f, 0.35f), "quake" => new Color(0.8f, 0.6f, 0.4f), "volley" => ElemColour(Elem.Fire), _ => caster.M?.Elem is Elem m ? ElemColour(m) : ElemColour(Elem.Fire) };
                Vector3 hands = At(caster.X + Math.Sin(caster.Facing) * 0.45, caster.Y + Math.Cos(caster.Facing) * 0.45, caster.Hero ? 1.3f : (float)(caster.M?.Height ?? 1.7) * 0.7f);
                Vfx.Burst(_dyn, hands, c * 1.8f, c, 18, 0.2f, 0.8f, 0.25f, 0.35f, new Vector3(0, 0.8f, 0), 180, null, true, null, 0.25f);
                break;
            }
            case "hit":
            {
                // Sparks where a blow lands: white for steel, the element's colour for the rest, more for a critical.
                Actor struck = Game.Find(e.A);
                float hh = struck?.M != null ? (float)struck.M.Height * 0.6f : 1.1f;
                Color c = e.E == Elem.Phys ? new Color(1f, 0.9f, 0.7f) : ElemColour(e.E);
                Vfx.Burst(_dyn, at + new Vector3(0, hh, 0), c * 1.6f, c * 0.6f, e.Crit ? 22 : 9, 1.5f, e.Crit ? 5 : 3.5f, e.Crit ? 0.14f : 0.1f, 0.3f, new Vector3(0, -6, 0), 70, Vector3.Up, true, Vfx.Shard);
                break;
            }
            case "shield":
                Vfx.Burst(_dyn, At(Game.Me.X, Game.Me.Y, 1f), new Color(0.6f, 0.8f, 1f) * 1.8f, new Color(0.3f, 0.5f, 1f), 50, 1, 2.5f, 0.2f, 0.8f, new Vector3(0, 0.5f, 0), 180, null, true, Vfx.Shard, 0.9f);
                break;
            case "die":
                // A wisp of ash leaves the body.
                Vfx.Burst(_dyn, at + new Vector3(0, 0.8f, 0), new Color(0.6f, 0.55f, 0.5f, 0.6f), new Color(0.2f, 0.18f, 0.16f), 14, 0.3f, 1, 0.6f, 1.2f, new Vector3(0, 0.6f, 0), 60, null, false, null, 0.4f, 1.6f);
                break;
            case "blast":
            {
                Color c = ElemColour(e.E);
                var ball = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1, Height = 2 }, MaterialOverride = Glow(c, 3), Position = at + new Vector3(0, 0.4f, 0) };
                var flash = new OmniLight3D { LightColor = c, LightEnergy = 4, OmniRange = Math.Max(4, e.V * 3) };
                ball.AddChild(flash);
                float r = Math.Max(0.6f, e.V);
                AddFx(ball, 0.4f, (fx, t) => { fx.Node.Scale = Vector3.One * (0.2f + r * t); Fade(fx.Node, 1 - t); flash.LightEnergy = 4 * (1 - t); });
                // Fire and sparks thrown out, smoke rising after, and for a big one a scorch left on the floor.
                Vfx.Burst(_dyn, at + new Vector3(0, 0.4f, 0), new Color(1f, 0.9f, 0.6f) * 2f, c, 20 + 12 * e.V, 1.5f, 2.5f + 2 * r, 0.5f + 0.15f * r, 0.5f, new Vector3(0, 1.5f, 0), 180, null, true, null, 0.2f, 0.4f);
                Vfx.Burst(_dyn, at + new Vector3(0, 0.3f, 0), c * 1.8f, c, 12 + 8 * e.V, 3, 6 + 2 * r, 0.1f, 0.6f, new Vector3(0, -9, 0), 60, Vector3.Up, true, Vfx.Shard);
                if (e.V >= 2)
                {
                    Vfx.Burst(_dyn, at + new Vector3(0, 0.6f, 0), new Color(0.3f, 0.27f, 0.25f, 0.55f), new Color(0.15f, 0.13f, 0.12f), 16, 0.4f, 1.2f, 1.0f + 0.2f * r, 1.6f, new Vector3(0, 0.8f, 0), 50, null, false, null, r * 0.4f, 1.8f);
                    MeshInstance3D scorch = Vfx.Mark(new Color(0.05f, 0.03f, 0.02f, 0.8f), r * 0.9f);
                    scorch.Position = at + new Vector3(0, 0.015f, 0);
                    AddFx(scorch, 6, (fx, t) => Fade(fx.Node, 0.8f * (1 - t * t)));
                }
                if (e.V >= 3) _shake = Math.Max(_shake, 0.7f);
                break;
            }
            case "bolt":
            {
                var node = new Node3D();
                Vector3 a = At(e.X, e.Y, 1.2f), b = At(e.X2, e.Y2, 1.1f);
                var rng = new Random(e.GetHashCode());
                Vector3 prev = a;
                for (int k = 1; k <= 6; k++)
                {
                    Vector3 p = a.Lerp(b, k / 6f) + (k < 6 ? new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.6f : Vector3.Zero);
                    float len = prev.DistanceTo(p);
                    var seg = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.09f, 0.09f, len) }, MaterialOverride = Glow(new Color(0.85f, 0.9f, 1f), 5) };
                    // A fork off the bolt now and then, and a halo along it.
                    if (k < 6 && rng.NextDouble() < 0.5)
                    {
                        var fork = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.04f, 0.6f) }, MaterialOverride = Glow(ElemColour(Elem.Light), 4) };
                        node.AddChild(fork);
                        Vector3 tip = p + new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * 0.6f - 0.3f, (float)rng.NextDouble() - 0.5f).Normalized() * 0.6f;
                        fork.Position = (p + tip) / 2;
                        fork.LookAtFromPosition(fork.Position, tip, Vector3.Up);
                    }
                    node.AddChild(seg);
                    seg.Position = (prev + p) / 2;
                    if (len > 0.01f) seg.LookAtFromPosition(seg.Position, p, Vector3.Up);
                    prev = p;
                }
                node.AddChild(new OmniLight3D { LightColor = ElemColour(Elem.Light), LightEnergy = 4, OmniRange = 6, Position = b });
                Vfx.Burst(_dyn, b, new Color(0.9f, 0.95f, 1f) * 2f, ElemColour(Elem.Light), 18, 2, 5, 0.12f, 0.35f, new Vector3(0, -5, 0), 180, null, true, Vfx.Shard);
                AddFx(node, 0.2f, (fx, t) => fx.Node.Visible = ((int)(t * 8)) % 2 == 0 || t < 0.3f);
                break;
            }
            case "step":
            {
                foreach (Vector3 p in new[] { at, At(e.X2, e.Y2) })
                {
                    Vfx.Burst(_dyn, p + new Vector3(0, 0.9f, 0), new Color(0.35f, 0.2f, 0.5f, 0.7f), new Color(0.1f, 0.05f, 0.15f), 26, 0.4f, 1.6f, 0.7f, 0.9f, new Vector3(0, 0.6f, 0), 180, null, false, null, 0.5f, 1.8f);
                    var puff = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.7f, Height = 1.4f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.15f, 0.35f, 0.6f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha }, Position = p + new Vector3(0, 0.9f, 0) };
                    AddFx(puff, 0.6f, (fx, t) => { fx.Node.Scale = Vector3.One * (0.6f + t); Fade(fx.Node, 0.6f * (1 - t)); });
                }
                break;
            }
            case "rain":
            {
                var rng = new Random((int)(Game.Time * 1000));
                for (int k = 0; k < 6; k++)
                {
                    float ang = (float)rng.NextDouble() * Mathf.Tau, d = (float)rng.NextDouble() * e.V;
                    Vector3 p = at + new Vector3(Mathf.Sin(ang) * d, 0, Mathf.Cos(ang) * d);
                    var arrow = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.75f, 0.05f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.42f, 0.28f) } };
                    float delay = (float)rng.NextDouble() * 0.15f;
                    AddFx(arrow, 0.35f, (fx, t) => fx.Node.Position = p + new Vector3(0, Mathf.Max(0.3f, 8 * (1 - t) - delay * 20), 0));
                    if (k % 2 == 0) Vfx.Burst(_dyn, p + new Vector3(0, 0.1f, 0), new Color(0.5f, 0.45f, 0.4f, 0.6f), new Color(0.3f, 0.28f, 0.25f), 6, 0.5f, 1.5f, 0.35f, 0.5f, new Vector3(0, -2, 0), 60, null, false);
                }
                break;
            }
            case "level":
            {
                var col = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.9f, BottomRadius = 0.9f, Height = 5 }, MaterialOverride = Glow(new Color(1f, 0.8f, 0.35f, 0.5f), 2), Position = At(Game.Me.X, Game.Me.Y, 2.5f) };
                AddFx(col, 1.2f, (fx, t) => { fx.Node.Position = At(Game.Me.X, Game.Me.Y, 2.5f); fx.Node.Scale = new Vector3(1 - t * 0.5f, 1, 1 - t * 0.5f); Fade(fx.Node, 0.5f * (1 - t)); });
                Vfx.Burst(_dyn, At(Game.Me.X, Game.Me.Y, 0.3f), new Color(1f, 0.9f, 0.5f) * 2f, new Color(1f, 0.6f, 0.2f), 60, 1.5f, 3.5f, 0.15f, 1.4f, new Vector3(0, 0.5f, 0), 20, Vector3.Up, true, Vfx.Shard, 0.8f);
                break;
            }
            case "waystone" or "portal":
                Ring(e.Kind == "portal" ? at : At(Game.Me.X, Game.Me.Y), 3, new Color(0.4f, 0.6f, 1f), 0.6f, 0.2f);
                break;
            case "chest":
                if (_props.TryGetValue(e.A, out Node3D chest)) OpenLid(chest, true);
                break;
            case "shrine":
                if (_props.TryGetValue(e.A, out Node3D shrine))
                {
                    QuenchShrine(shrine);
                    Vfx.Burst(_dyn, shrine.Position + new Vector3(0, 1.3f, 0), new Color(1f, 0.8f, 0.4f) * 2f, new Color(1f, 0.4f, 0.1f), 50, 1, 3, 0.3f, 1.0f, new Vector3(0, 2, 0), 30, Vector3.Up, true, null, 0.3f);
                    Vfx.Burst(_dyn, At(Game.Me.X, Game.Me.Y, 0.2f), new Color(1f, 0.85f, 0.5f) * 1.8f, new Color(1f, 0.6f, 0.2f), 40, 1.5f, 3, 0.15f, 1.2f, new Vector3(0, 0.5f, 0), 15, Vector3.Up, true, Vfx.Shard, 0.6f);
                    Flash(shrine.Position + new Vector3(0, 1.5f, 0), new Color(1f, 0.6f, 0.25f), 5, 9, 0.8f);
                }
                break;
            case "break":
                if (_props.TryGetValue(e.A, out Node3D thing) && Game.Map.Props.FirstOrDefault(x => x.Id == e.A) is Prop bp) Shatter(bp, thing, true);
                break;
            case "drop":
                _falling[e.A] = _clock;
                break;
            case "boss":
                _shake = 1;
                break;
        }
        if (e.Kind is "hit" or "hurt" or "miss" or "block" or "dodge") Number(e);
    }

    /// <summary>A light that flashes up and dies away.</summary>
    private void Flash(Vector3 at, Color c, float energy, float range, float life)
    {
        var l = new OmniLight3D { LightColor = c, LightEnergy = energy, OmniRange = range, Position = at };
        AddFx(l, life, (fx, t) => ((OmniLight3D)fx.Node).LightEnergy = energy * (1 - t));
    }

    /// <summary>Cracks running out across the floor from a point, glowing a moment, then fading.</summary>
    private void Cracks(Vector3 at, float radius)
    {
        var rng = new Random((int)(_clock * 1000));
        var node = new Node3D { Position = at + new Vector3(0, 0.02f, 0) };
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.04f, 0.03f, 0.02f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = new Color(1f, 0.4f, 0.1f), EmissionEnergyMultiplier = 1.5f };
        for (int k = 0; k < 9; k++)
        {
            float ang = (k + (float)rng.NextDouble() * 0.6f) / 9 * Mathf.Tau;
            Vector3 from = Vector3.Zero;
            float len = radius * (0.5f + 0.5f * (float)rng.NextDouble());
            for (int s = 0; s < 4; s++)
            {
                ang += ((float)rng.NextDouble() - 0.5f) * 0.7f;
                Vector3 to = from + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * (len / 4);
                var seg = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.09f - s * 0.018f, 0.01f, from.DistanceTo(to)) }, MaterialOverride = mat };
                node.AddChild(seg);
                seg.Position = (from + to) / 2;
                seg.LookAtFromPosition(seg.Position, to, Vector3.Up);
                from = to;
            }
        }
        AddFx(node, 5, (fx, t) => { mat.EmissionEnergyMultiplier = Math.Max(0, 1.5f - t * 6); mat.AlbedoColor = new Color(0.04f, 0.03f, 0.02f, 1 - t * t); });
    }

    /// <summary>A number rising from a struck figure (damage in white, a critical in gold, the hero's wounds in red).</summary>
    private void Number(Ev e)
    {
        string text = e.Kind switch { "miss" => "miss", "block" => "block", "dodge" => "dodge", _ => e.V.ToString() };
        Color c = e.Kind switch
        {
            "hurt" => new Color(1f, 0.35f, 0.3f),
            "miss" or "block" or "dodge" => new Color(0.7f, 0.7f, 0.72f),
            _ => e.Crit ? new Color(1f, 0.85f, 0.3f) : e.E == Elem.Phys ? new Color(0.95f, 0.93f, 0.9f) : ElemColour(e.E),
        };
        Actor who = Game.Find(e.A);
        float h = who == null ? 1.6f : who.Hero ? 2.0f : (float)(who.M?.Height ?? 1.7) + 0.2f;
        float jx = (float)(new Random(e.GetHashCode()).NextDouble() - 0.5) * 0.6f;
        Vector3 start = At(e.X, e.Y, h) + new Vector3(jx, 0, 0);
        var l = new Label3D
        {
            Text = text, FontSize = e.Crit ? 52 : 40, PixelSize = 0.006f, NoDepthTest = true, OutlineSize = 10, Modulate = c,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = start,
        };
        AddFx(l, 0.9f, (fx, t) => { fx.Node.Position = start + new Vector3(0, t * 1.1f, 0); ((Label3D)fx.Node).Modulate = c with { A = 1 - t * t }; });
    }

    // ---- picking ----------------------------------------------------------------------------------------------------

    /// <summary>The floor point under a viewport position, in sim coordinates.</summary>
    public (double x, double y)? Ground(Vector2 p)
    {
        if (Cam == null) return null;
        Vector3 o = Cam.ProjectRayOrigin(p), d = Cam.ProjectRayNormal(p);
        if (d.Y > -1e-4f) return null;
        float t = -o.Y / d.Y;
        Vector3 hit = o + d * t;
        return (hit.X, hit.Z);
    }

    /// <summary>Where a sim point at a height is on the viewport.</summary>
    public Vector2 ScreenOf(double x, double y, float h = 0) => Cam.UnprojectPosition(At(x, y, h));

    public bool OnScreen(double x, double y, float h = 0) => !Cam.IsPositionBehind(At(x, y, h));

    /// <summary>The living monster or village folk under a viewport position (its body's box on screen).</summary>
    public Actor ActorAt(Vector2 p)
    {
        Actor best = null;
        float bestD = float.MaxValue;
        foreach (Actor a in Game.Here.Actors)
        {
            if (!a.Alive || Game.Dist(a, Game.Me) > 26) continue;
            float h = a.Npc ? 1.75f : (float)(a.M?.Height ?? 1.7);
            Vector2 foot = ScreenOf(a.X, a.Y), head = ScreenOf(a.X, a.Y, h);
            float half = Mathf.Max(16, (foot.Y - head.Y) * 0.32f);
            Vector2 mid = (foot + head) / 2;
            if (p.X < mid.X - half || p.X > mid.X + half || p.Y < head.Y - 6 || p.Y > foot.Y + 8) continue;
            float d = p.DistanceTo(mid);
            if (d < bestD) { bestD = d; best = a; }
        }
        return best;
    }

    /// <summary>The drop whose name or icon is under a viewport position.</summary>
    public Drop DropAt(Vector2 p)
    {
        foreach (Drop d in Game.Here.Drops.OrderBy(d => Game.Dist(d.X, d.Y, Game.Me.X, Game.Me.Y)))
        {
            if (Game.Dist(d.X, d.Y, Game.Me.X, Game.Me.Y) > 25) continue;
            float ly = _drops.TryGetValue(d.Id, out Node3D dn) && dn.GetNodeOrNull<Label3D>("Name") is Label3D lab && lab.Visible ? lab.Position.Y : -1;
            Vector2 label = ScreenOf(d.X, d.Y, Math.Max(0.6f, ly)), icon = ScreenOf(d.X, d.Y, 0.05f);
            if (ly < 0) label = new Vector2(-9999, -9999);
            float w = Math.Max(40, (d.Item.Base == "gold" ? 9 : d.Item.Title.Length) * 5.2f);
            if (Math.Abs(p.X - label.X) < w && Math.Abs(p.Y - label.Y) < 11) return d;
            if (p.DistanceTo(icon) < 22) return d;
        }
        return null;
    }

    /// <summary>The usable prop (chest, waystone, stairs, stash, well) at a floor point; -2 is the scroll's way back.</summary>
    public int PropAt(double x, double y)
    {
        if (Game.Depth == 0 && Game.PortalDepth > 0 && Game.Dist(x, y, Game.PortalSpot.x, Game.PortalSpot.y) < 1.1) return -2;
        foreach (Prop p in Game.Map.Props)
        {
            if (!p.Usable || p.OneUse && Game.Here.Opened.Contains(p.Id)) continue;
            if (x >= p.X - 0.4 && y >= p.Y - 0.4 && x < p.X + p.W + 0.4 && y < p.Y + p.H + 0.4) return p.Id;
        }
        return 0;
    }

    /// <summary>An ember rim added over a usable thing's own look; its strength is per mesh (instance uniform).</summary>
    private const string GlowShader = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back;
instance uniform float glow = 0.0;
uniform vec3 tint : source_color = vec3(1.0, 0.55, 0.2);
void fragment() {
    float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0);
    ALBEDO = tint * (rim * 0.9 + 0.06) * glow;
}";

    private const string WallShader = @"
shader_type spatial;
render_mode cull_back;
uniform vec3 hero_pos;
uniform vec3 cam_pos;
uniform float cut = 1.0;
uniform float radius = 2.8;
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    ALBEDO = COLOR.rgb;
    ROUGHNESS = 0.95;
    if (cut > 0.5 && wpos.y > 0.3) {
        vec3 d = hero_pos - cam_pos;
        float len = length(d);
        vec3 dir = d / len;
        vec3 rel = wpos - cam_pos;
        float t = dot(rel, dir);
        if (t < len - 0.5) {
            float off = length(rel - dir * t);
            if (off < radius) {
                float k = smoothstep(radius * 0.5, radius, off);
                float n = fract(sin(dot(floor(FRAGCOORD.xy), vec2(12.9898, 78.233))) * 43758.5453);
                if (n > k) discard;
            }
        }
    }
}";
}
