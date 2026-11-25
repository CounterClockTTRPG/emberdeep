using System;
using Godot;

namespace Emberdeep;

/// <summary>
/// The particles behind the spells and blows: soft glowing sprites (added light, so they bloom with the scene's
/// glow) with a colour ramp that fades out, made with CPUParticles3D (fine on the mobile renderer). One-shot bursts
/// free themselves; trails follow a missile and are left to fade when it is gone.
/// </summary>
public static class Vfx
{
    private static Texture2D _soft, _shard;

    /// <summary>A round soft dot, bright in the middle.</summary>
    public static Texture2D Soft => _soft ??= Radial(false);

    /// <summary>A small sharp-edged sliver (ice, sparks).</summary>
    public static Texture2D Shard => _shard ??= Radial(true);

    private static Texture2D Radial(bool hard)
    {
        var g = new Gradient();
        g.SetColor(0, new Color(1, 1, 1, 1));
        g.SetColor(1, new Color(1, 1, 1, 0));
        if (hard) g.AddPoint(0.45f, new Color(1, 1, 1, 0.9f));
        return new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f), Width = 64, Height = 64 };
    }

    /// <summary>A sprite material: added light by default (fire, sparks, magic), or plain see-through (smoke, dust).</summary>
    public static StandardMaterial3D Sprite(Texture2D tex, bool add = true) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoTexture = tex, VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = add ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
    };

    private static Gradient Ramp(Color a, Color b, float fadeAt = 0.6f)
    {
        var g = new Gradient();
        g.SetColor(0, a);
        g.SetColor(1, new Color(b, 0));
        g.AddPoint(fadeAt, b);
        return g;
    }

    private static Curve Shrink(float end = 0.2f)
    {
        var c = new Curve();
        c.AddPoint(new Vector2(0, 1));
        c.AddPoint(new Vector2(1, end));
        return c;
    }

    /// <summary>
    /// A one-shot burst at a place: <paramref name="n"/> sprites from <paramref name="from"/> to <paramref name="to"/>
    /// in colour, flying out at a speed, pulled by gravity, over a life. It frees itself.
    /// </summary>
    public static CpuParticles3D Burst(Node parent, Vector3 at, Color from, Color to, int n, float speedMin, float speedMax, float size,
        float life, Vector3 gravity, float spread = 180, Vector3? dir = null, bool add = true, Texture2D tex = null, float radius = 0.1f, float grow = 0.2f)
    {
        var p = new CpuParticles3D
        {
            Amount = Math.Max(1, n), Lifetime = life, OneShot = true, Explosiveness = 0.92f, Emitting = true, LocalCoords = false,
            Mesh = new QuadMesh { Size = new Vector2(size, size), Material = Sprite(tex ?? Soft, add) },
            Direction = dir ?? Vector3.Up, Spread = spread, InitialVelocityMin = speedMin, InitialVelocityMax = speedMax, Gravity = gravity,
            ColorRamp = Ramp(from, to), ScaleAmountCurve = Shrink(grow), ScaleAmountMin = 0.6f, ScaleAmountMax = 1.3f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = radius, DampingMin = 1, DampingMax = 3,
            AngleMin = 0, AngleMax = 360,
        };
        parent.AddChild(p);
        p.GlobalPosition = at;
        p.Finished += p.QueueFree;
        return p;
    }

    /// <summary>A ring burst along the ground: sprites flying outward in every direction (a nova, a shockwave's dust).</summary>
    public static CpuParticles3D Ring(Node parent, Vector3 at, Color from, Color to, int n, float speed, float size, float life, bool add = true, Texture2D tex = null)
    {
        CpuParticles3D p = Burst(parent, at, from, to, n, speed * 0.85f, speed * 1.15f, size, life, Vector3.Zero, 90, Vector3.Up, add, tex);
        p.Flatness = 1;           // all in the ground's plane
        p.Direction = Vector3.Right;
        p.Spread = 180;
        p.DampingMin = p.DampingMax = speed * 0.6f;
        return p;
    }

    /// <summary>A trail that streams from a moving node (fire behind a bolt, frost behind a shard).</summary>
    public static CpuParticles3D Trail(Color from, Color to, float size, int amount = 40, float life = 0.45f, bool add = true)
    {
        return new CpuParticles3D
        {
            Amount = amount, Lifetime = life, LocalCoords = false, Emitting = true,
            Mesh = new QuadMesh { Size = new Vector2(size, size), Material = Sprite(Soft, add) },
            Direction = Vector3.Up, Spread = 180, InitialVelocityMin = 0.1f, InitialVelocityMax = 0.5f, Gravity = new Vector3(0, 0.6f, 0),
            ColorRamp = Ramp(from, to, 0.4f), ScaleAmountCurve = Shrink(0.1f), ScaleAmountMin = 0.7f, ScaleAmountMax = 1.2f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.08f,
        };
    }

    /// <summary>Flames rising off a patch of ground for as long as they emit (burning ground).</summary>
    public static CpuParticles3D Flames(float radius, int amount = 60)
    {
        return new CpuParticles3D
        {
            Amount = amount, Lifetime = 0.8f, LocalCoords = false, Emitting = true,
            Mesh = new QuadMesh { Size = new Vector2(0.35f, 0.35f), Material = Sprite(Soft) },
            Direction = Vector3.Up, Spread = 12, InitialVelocityMin = 0.8f, InitialVelocityMax = 1.8f, Gravity = new Vector3(0, 0.5f, 0),
            ColorRamp = Ramp(new Color(1f, 0.85f, 0.4f) * 1.6f, new Color(0.9f, 0.25f, 0.05f), 0.5f), ScaleAmountCurve = Shrink(0.2f),
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(radius * 0.8f, 0.05f, radius * 0.8f),
        };
    }

    /// <summary>A dark mark on the floor that fades over some seconds (a scorch, a crack), returned for the caller to time.</summary>
    public static MeshInstance3D Mark(Color c, float radius)
    {
        var m = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoTexture = Soft, AlbedoColor = c,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        return new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(radius * 2, radius * 2) }, MaterialOverride = m };
    }
}
