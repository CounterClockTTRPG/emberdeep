using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Emberdeep;

/// <summary>
/// The models, read at run time from assets/ (glTF bytes, no import step): assets/figures holds the rigged bodies
/// (body-*), the things they hold (held-*) and the animation clips, humanoid (clip-*) and quadruped (qclip-*), all on
/// assetgen's two skeleton templates so any clip plays on any body of its kind; assets/models holds the props.
/// Each folder has a manifest.txt, since an exported build can read a file but not list a folder.
/// </summary>
public static class Kit
{
    private static string DirOf(string folder) => FileAccess.FileExists($"res://assets/{folder}/manifest.txt")
        ? $"res://assets/{folder}" : OS.GetExecutablePath().GetBaseDir() + $"/assets/{folder}";

    private static readonly Dictionary<string, Node3D> _protos = new();
    private static readonly Dictionary<string, AnimationLibrary> _clips = new();
    private static readonly Dictionary<string, Texture2D> _icons = new();

    /// <summary>Why clips were left out (the self-check reports it).</summary>
    public static readonly List<string> Trouble = new();

    /// <summary>A fresh copy of a model (folder "figures" or "models"), or null when there is none.</summary>
    public static Node3D Part(string name, string folder = "figures")
    {
        string key = folder + "/" + name;
        if (!_protos.TryGetValue(key, out Node3D proto))
        {
            proto = null;
            string path = $"{DirOf(folder)}/{name}.glb";
            if (FileAccess.FileExists(path))
            {
                var doc = new GltfDocument();
                var state = new GltfState();
                if (doc.AppendFromBuffer(FileAccess.GetFileAsBytes(path), "", state) == Error.Ok) proto = doc.GenerateScene(state) as Node3D;
                if (proto != null) DropColliders(proto);
            }
            _protos[key] = proto;
        }
        return proto?.Duplicate() as Node3D;
    }

    public static bool Has(string name, string folder = "figures") => FileAccess.FileExists($"{DirOf(folder)}/{name}.glb");

    /// <summary>An icon from assets/icons (item and skill pictures), or null.</summary>
    public static Texture2D Icon(string name)
    {
        if (name == null) return null;
        if (_icons.TryGetValue(name, out Texture2D t)) return t;
        string path = $"{DirOf("icons")}/{name}.png";
        t = null;
        if (FileAccess.FileExists(path))
        {
            var img = new Image();
            if (img.LoadPngFromBuffer(FileAccess.GetFileAsBytes(path)) == Error.Ok) t = ImageTexture.CreateFromImage(img);
        }
        return _icons[name] = t;
    }

    private static readonly Dictionary<string, Texture2D> _cutouts = new();

    /// <summary>
    /// An icon with its dark background taken away (a flood from the edges through dark pixels, so dark parts inside
    /// the object stay), for an item lying on the floor. Null when there is no icon.
    /// </summary>
    public static Texture2D Cutout(string name)
    {
        if (name == null) return null;
        if (_cutouts.TryGetValue(name, out Texture2D t)) return t;
        string path = $"{DirOf("icons")}/{name}.png";
        var img = new Image();
        if (!FileAccess.FileExists(path) || img.LoadPngFromBuffer(FileAccess.GetFileAsBytes(path)) != Error.Ok) return _cutouts[name] = null;
        img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight();
        var lum = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) { Color c = img.GetPixel(x, y); lum[y * w + x] = c.R * 0.3f + c.G * 0.55f + c.B * 0.15f; }
        // 1. The background: dark pixels reached from the edges.
        var gone = new bool[w * h];
        var queue = new Queue<int>();
        for (int x = 0; x < w; x++) { queue.Enqueue(x); queue.Enqueue((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { queue.Enqueue(y * w); queue.Enqueue(y * w + w - 1); }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w;
            if (gone[i] || lum[i] > 0.12f) continue;
            gone[i] = true;
            if (x > 0) queue.Enqueue(i - 1);
            if (x < w - 1) queue.Enqueue(i + 1);
            if (y > 0) queue.Enqueue(i - w);
            if (y < h - 1) queue.Enqueue(i + w);
        }
        // 2. Specks: what is left in small islands (glints, sparks, bits of a vignette) goes too.
        var island = new int[w * h];
        var sizes = new List<int> { 0 };
        for (int i = 0; i < w * h; i++)
        {
            if (gone[i] || island[i] != 0) continue;
            int id = sizes.Count, n = 0;
            sizes.Add(0);
            queue.Enqueue(i);
            island[i] = id;
            while (queue.Count > 0)
            {
                int j = queue.Dequeue(), x = j % w, y = j / w;
                n++;
                foreach (int k in new[] { x > 0 ? j - 1 : -1, x < w - 1 ? j + 1 : -1, y > 0 ? j - w : -1, y < h - 1 ? j + w : -1 })
                    if (k >= 0 && !gone[k] && island[k] == 0) { island[k] = id; queue.Enqueue(k); }
            }
            sizes[id] = n;
        }
        int biggest = sizes.Max();
        for (int i = 0; i < w * h; i++) if (!gone[i] && sizes[island[i]] < Math.Max(40, biggest / 25)) gone[i] = true;
        // 3. Alpha: none for the background, a soft edge where the object meets it, darker edge pixels fading more.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                Color c = img.GetPixel(x, y);
                if (gone[i]) { img.SetPixel(x, y, new Color(c.R, c.G, c.B, 0)); continue; }
                bool edge = x > 0 && gone[i - 1] || x < w - 1 && gone[i + 1] || y > 0 && gone[i - w] || y < h - 1 && gone[i + w];
                if (edge) img.SetPixel(x, y, new Color(c.R, c.G, c.B, Mathf.Clamp((lum[i] - 0.08f) * 5, 0.25f, 0.8f)));
            }
        img.GenerateMipmaps();
        return _cutouts[name] = ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// A model lying on the floor as an item does: turned so its smallest side is up (a sword flat, a shield on its
    /// back), stood on the ground, centred. Null when there is no such model.
    /// </summary>
    public static Node3D Lying(string name, string folder, float scale = 1)
    {
        Node3D m = Part(name, folder);
        if (m == null) return null;
        var holder = new Node3D { Name = name };
        var turn = new Node3D();
        holder.AddChild(turn);
        turn.AddChild(m);
        Basis best = Basis.Identity;
        float low = float.MaxValue;
        foreach (Basis b in new[] { Basis.Identity, new Basis(Vector3.Right, Mathf.Pi / 2), new Basis(Vector3.Back, Mathf.Pi / 2) })
        {
            turn.Basis = b;
            float hgt = Bounds(holder).Size.Y;
            if (hgt < low - 1e-4f) { low = hgt; best = b; }
        }
        turn.Basis = best.Scaled(Vector3.One * scale);
        Aabb box = Bounds(holder);
        Vector3 c = box.GetCenter();
        turn.Position = new Vector3(-c.X, -box.Position.Y + 0.01f, -c.Z);
        return holder;
    }

    /// <summary>The clip names in a manifest, for a prefix (clip- or qclip-).</summary>
    private static List<string> ClipNames(string prefix)
    {
        string mf = $"{DirOf("figures")}/manifest.txt";
        if (!FileAccess.FileExists(mf)) { Trouble.Add("no figures manifest"); return new List<string>(); }
        return FileAccess.GetFileAsString(mf).Split('\n').Select(l => l.Trim().Split(' ')[0])
            .Where(n => n.StartsWith(prefix)).Select(n => n[prefix.Length..]).ToList();
    }

    public static readonly HashSet<string> Looping = new() { "idle", "walk", "run", "ready", "search", "hammer", "pray", "aim" };

    /// <summary>Every clip fitted to one body (they differ in height and rest pose), aimed at its skeleton under "Body".</summary>
    public static AnimationLibrary Clips(string body, Skeleton3D ours, string skelPath, bool quad)
    {
        if (_clips.TryGetValue(body, out AnimationLibrary have)) return have;
        var lib = _clips[body] = new AnimationLibrary();
        string prefix = quad ? "qclip-" : "clip-";
        foreach (string name in ClipNames(prefix))
        {
            Node3D scene = Part(prefix + name);
            var player = scene == null ? null : Find<AnimationPlayer>(scene);
            string src = player?.GetAnimationList().FirstOrDefault();
            if (src == null) { Trouble.Add($"{prefix}{name}: no animation"); scene?.Free(); continue; }
            var anim = (Animation)player.GetAnimation(src).Duplicate(true);
            Retarget(anim, Find<Skeleton3D>(scene), ours, skelPath);
            anim.LoopMode = Looping.Contains(name) ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
            lib.AddAnimation(name, anim);
            scene.Free();
        }
        return lib;
    }

    /// <summary>
    /// Point every bone track at our skeleton and drop the rest. Rotations carry over (the template's rest pose is
    /// shared); positions are the clip's movement from its rest, scaled by how much taller or shorter our body is.
    /// </summary>
    private static void Retarget(Animation anim, Skeleton3D theirs, Skeleton3D ours, string skelPath)
    {
        float ratio = 1;
        if (theirs != null && ours != null)
        {
            int th = theirs.FindBone("hips"), oh = ours.FindBone("hips");
            if (th >= 0 && oh >= 0)
            {
                float a = theirs.GetBoneGlobalRest(th).Origin.Y, b = ours.GetBoneGlobalRest(oh).Origin.Y;
                if (a > 0.01f && b > 0.01f) ratio = b / a;
            }
        }
        for (int t = anim.GetTrackCount() - 1; t >= 0; t--)
        {
            string bone = anim.TrackGetPath(t).GetConcatenatedSubNames();
            Animation.TrackType type = anim.TrackGetType(t);
            if (string.IsNullOrEmpty(bone) || type == Animation.TrackType.Scale3D || ours == null || ours.FindBone(bone) < 0) { anim.RemoveTrack(t); continue; }
            if (type == Animation.TrackType.Position3D && theirs != null && theirs.FindBone(bone) >= 0)
            {
                Vector3 theirRest = theirs.GetBoneRest(theirs.FindBone(bone)).Origin, ourRest = ours.GetBoneRest(ours.FindBone(bone)).Origin;
                for (int k = 0; k < anim.TrackGetKeyCount(t); k++)
                    anim.TrackSetKeyValue(t, k, ourRest + ((Vector3)anim.TrackGetKeyValue(t, k) - theirRest) * ratio);
            }
            anim.TrackSetPath(t, new NodePath($"Body/{skelPath}:{bone}"));
        }
    }

    public static T Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (Node c in n.GetChildren()) if (Find<T>(c) is T f) return f;
        return null;
    }

    public static Node FindNamed(Node n, string name)
    {
        if (n.Name == name) return n;
        foreach (Node c in n.GetChildren()) if (FindNamed(c, name) is Node f) return f;
        return null;
    }

    private static void DropColliders(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            string name = c.Name.ToString();
            if (name.EndsWith("-colonly") || name.EndsWith("-col")) { n.RemoveChild(c); c.Free(); }
            else DropColliders(c);
        }
    }

    /// <summary>The box around every mesh of a model, in the model's own frame.</summary>
    public static Aabb Bounds(Node3D n)
    {
        Aabb? box = null;
        foreach (MeshInstance3D mi in n.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            if (mi.Mesh == null) continue;
            Transform3D t = Transform3D.Identity;
            for (Node p = mi; p != null && p != n; p = p.GetParent()) if (p is Node3D p3) t = p3.Transform * t;
            Aabb a = t * mi.Mesh.GetAabb();
            box = box == null ? a : box.Value.Merge(a);
        }
        return box ?? new Aabb(Vector3.Zero, Vector3.One);
    }

    /// <summary>A prop fitted to a size (its largest side), stood on the ground and centred.</summary>
    public static Node3D Prop(string model, float size)
    {
        Node3D m = Part(model, "models");
        if (m == null) return null;
        var holder = new Node3D { Name = model };
        holder.AddChild(m);
        Aabb box = Bounds(m);
        float big = Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z));
        float s = big > 0.01f ? size / big : 1;
        m.Scale = Vector3.One * s;
        Vector3 c = box.GetCenter();
        m.Position = new Vector3(-c.X * s, -box.Position.Y * s, -c.Z * s);
        return holder;
    }
}

/// <summary>
/// One figure: a rigged body, things held in its sockets, and an animation player with every clip of its skeleton.
/// <see cref="Hold"/> sets what it holds (rebuilt only when that changes) and <see cref="Play"/> what it does.
/// </summary>
public partial class Figure3D : Node3D
{
    private Node3D _body;
    private Skeleton3D _skel;
    private AnimationPlayer _player;
    private readonly List<Node> _held = new(), _worn = new();
    private string _holding = "", _clip = "", _wearing = "";
    public readonly string BodyName;
    private readonly float _height;
    public bool Quad { get; private set; }
    public bool Loaded => _body != null;

    public Figure3D() { }

    public Figure3D(string body, float height) { BodyName = body; _height = height; }

    public override void _Ready()
    {
        _body = Kit.Part(BodyName);
        if (_body == null) return;
        _body.Name = "Body";
        AddChild(_body);
        _skel = Kit.Find<Skeleton3D>(_body);
        Quad = _skel != null && _skel.FindBone("paw_fl") >= 0;
        string skelPath = _skel != null ? _body.GetPathTo(_skel).ToString() : "Skeleton3D";
        foreach (AnimationPlayer p in _body.FindChildren("*", "AnimationPlayer", true, false).Cast<AnimationPlayer>()) p.QueueFree();
        _player = new AnimationPlayer { Name = "Anim" };
        AddChild(_player);
        _player.RootNode = new NodePath("..");
        _player.AddAnimationLibrary("", Kit.Clips(BodyName, _skel, skelPath, Quad));
        Aabb box = Kit.Bounds(_body);
        float s = box.Size.Y > 0.1f ? _height / box.Size.Y : 1;
        _body.Scale = Vector3.One * s;
        _body.Position = new Vector3(0, -box.Position.Y * s, 0);
    }

    public bool Has(string clip) => _player != null && _player.HasAnimation(clip);

    /// <summary>The skin regions of a base body each outfit slot covers (assetgen's rig_lib.SLOTS).</summary>
    private static readonly Dictionary<string, string[]> Hides = new()
    {
        ["head"] = new[] { "head" }, ["torso"] = new[] { "torso", "upper_arms" }, ["arms"] = new[] { "forearms", "hands" },
        ["legs"] = new[] { "thighs", "shins" }, ["feet"] = new[] { "feet" }, ["waist"] = new string[0], ["back"] = new string[0],
    };

    /// <summary>
    /// What it wears: outfit pieces (assets/figures/&lt;name&gt;.glb, each skinned to the template), with the slot each
    /// fills. Each piece's meshes move onto this body's skeleton by bone name; the body's skin regions a slot covers
    /// (body_torso, body_head...) are hidden under it. Rebuilt only when the list changes.
    /// </summary>
    public void Wear(IReadOnlyList<(string piece, string slot)> pieces)
    {
        if (_skel == null) return;
        string key = string.Join(",", pieces.Select(p => p.piece));
        if (key == _wearing) return;
        _wearing = key;
        foreach (Node n in _worn) n.QueueFree();
        _worn.Clear();
        var hidden = new HashSet<string>(pieces.SelectMany(p => Hides.TryGetValue(p.slot, out string[] h) ? h : new string[0]));
        foreach (MeshInstance3D mi in _body.FindChildren("body_*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            string region = mi.Name.ToString()[5..];
            mi.Visible = !hidden.Any(h => region == h || region.StartsWith(h + "_"));
        }
        foreach (var (piece, _) in pieces) Put(piece);
    }

    private void Put(string piece)
    {
        Node3D part = Kit.Part(piece);
        if (part == null) return;
        Skeleton3D theirs = Kit.Find<Skeleton3D>(part);
        foreach (MeshInstance3D mi in part.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToList())
        {
            if (mi.Skin == null)
            {
                // A rigid part riding a bone (a helm's crest, a buckle): onto our bone, where it sat on theirs.
                BoneAttachment3D on = null;
                for (Node p = mi.GetParent(); p != null && on == null; p = p.GetParent()) on = p as BoneAttachment3D;
                if (on == null || _skel.FindBone(on.BoneName) < 0) continue;
                Transform3D rel = Transform3D.Identity;
                for (Node n = mi; n != on; n = n.GetParent()) if (n is Node3D n3) rel = n3.Transform * rel;
                var at = new BoneAttachment3D { BoneName = on.BoneName };
                _skel.AddChild(at);
                mi.GetParent().RemoveChild(mi);
                at.AddChild(mi);
                mi.Transform = rel;
                _worn.Add(at);
                continue;
            }
            if (theirs == null) continue;
            var skin = (Skin)mi.Skin.Duplicate();
            for (int b = 0; b < skin.GetBindCount(); b++)
            {
                string bone = skin.GetBindName(b);
                if (string.IsNullOrEmpty(bone)) bone = theirs.GetBoneName(skin.GetBindBone(b));
                int ours = _skel.FindBone(bone);
                if (ours >= 0) { skin.SetBindName(b, ""); skin.SetBindBone(b, ours); }
            }
            mi.GetParent().RemoveChild(mi);
            _skel.AddChild(mi);
            mi.Transform = Transform3D.Identity;
            mi.Skin = skin;
            mi.Skeleton = mi.GetPathTo(_skel);
            _worn.Add(mi);
        }
        part.Free();
    }

    /// <summary>What it holds: right hand, left hand (a bow), left forearm (a shield).</summary>
    public void Hold(string right, string left, string forearm)
    {
        if (_body == null) return;
        string key = $"{right}|{left}|{forearm}";
        if (key == _holding) return;
        _holding = key;
        foreach (Node n in _held) n.QueueFree();
        _held.Clear();
        Attach(right, "socket_hand_r");
        Attach(left, "socket_hand_l");
        Attach(forearm, "socket_forearm_l");
    }

    private void Attach(string what, string socket)
    {
        if (string.IsNullOrEmpty(what)) return;
        Node3D item = Kit.Part("held-" + what);
        Node at = Kit.FindNamed(_body, socket);
        if (item == null || at == null) { item?.Free(); return; }
        at.AddChild(item);
        _held.Add(item);
    }

    /// <summary>Play a clip (the first of the names it has) at a speed; <paramref name="again"/> restarts it even if playing.</summary>
    public void Play(string clip, float speed = 1, bool again = false, params string[] instead)
    {
        if (_player == null) return;
        if (!_player.HasAnimation(clip)) clip = instead.FirstOrDefault(c => _player.HasAnimation(c)) ?? (_player.HasAnimation("idle") ? "idle" : "");
        if (clip == "") return;
        _player.SpeedScale = speed;
        if (clip == _clip && !again && (_player.IsPlaying() || !Kit.Looping.Contains(clip))) return;
        _clip = clip;
        _player.Play(clip, 0.12);
        if (again) _player.Seek(0, true);
    }

    /// <summary>How long a clip runs at speed 1, in seconds (0 when there is none).</summary>
    public float Length(string clip) => _player != null && _player.HasAnimation(clip) ? (float)_player.GetAnimation(clip).Length : 0;

    public string Clip => _clip;

    /// <summary>Tint every surface (a champion's red, a shade's pallor, a hidden rogue's smoke).</summary>
    public void Tint(Color c, float alpha = 1)
    {
        if (_body == null) return;
        foreach (MeshInstance3D mi in _body.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            if (mi.Mesh == null) continue;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                if (c == Colors.White && alpha >= 1) { mi.SetSurfaceOverrideMaterial(s, null); continue; }
                if (mi.Mesh.SurfaceGetMaterial(s) is not BaseMaterial3D m) continue;
                var x = (BaseMaterial3D)m.Duplicate();
                Color col = m.AlbedoColor * c;
                col.A = alpha;
                x.AlbedoColor = col;
                if (alpha < 1) x.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                mi.SetSurfaceOverrideMaterial(s, x);
            }
        }
    }
}
