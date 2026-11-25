using System;
using System.Collections.Generic;
using System.Linq;

namespace SfxGen;

public enum Wave { Sine, Square, Saw, Triangle, Noise }

/// <summary>
/// Everything one sound effect is made of (in the spirit of sfxr): a tone with a pitch sweep and vibrato, a share of
/// noise, an envelope, a filter that can sweep, a few repeats, an echo and a room. Plain data: it serialises to JSON,
/// and the same def always renders the same samples.
/// </summary>
public sealed class SfxDef
{
    public string Name = "sound";
    public Wave Wave = Wave.Square;
    public float Duty = 0.5f;
    /// <summary>Start pitch (Hz) and where it slides to by the end of the sustain.</summary>
    public float Freq = 440, FreqEnd = 440;
    /// <summary>An overtone above the base (ratio) and its level, for bells, metal and glass.</summary>
    public float Partial = 0, PartialLevel = 0;
    public float VibratoHz = 0, VibratoDepth = 0;
    /// <summary>How much noise against tone, 0..1, and how bright the noise is (Hz low-pass).</summary>
    public float Noise = 0, NoiseColor = 8000;
    public float Attack = 0.005f, Sustain = 0.08f, Decay = 0.15f;
    /// <summary>Low-pass cutoff (Hz) at the start and the end; band-pass centre and Q when BandQ is above 0.</summary>
    public float Cutoff = 20000, CutoffEnd = 20000, BandCentre = 1000, BandQ = 0;
    public float HighPass = 20;
    /// <summary>The whole sound again, Repeats more times, every RepeatGap seconds, each a bit lower and quieter.</summary>
    public int Repeats = 0;
    public float RepeatGap = 0.1f, RepeatPitch = 1;
    public float EchoDelay = 0, EchoFeedback = 0;
    public float Room = 0, Drive = 1, Crush = 0, Volume = 0.9f;

    public float Length => Attack + Sustain + Decay + Repeats * RepeatGap + (EchoFeedback > 0 ? EchoDelay * 4 : 0) + (Room > 0 ? 0.6f * Room : 0);

    public SfxDef Copy() => (SfxDef)MemberwiseClone();
}

/// <summary>Sound effects: render a def, roll one in a category from a seed, or build one from a few words.</summary>
public static class Effects
{
    /// <summary>Renders a def to mono samples at <see cref="Audio.Rate"/>, normalised to its volume.</summary>
    public static float[] Render(SfxDef d, ulong noiseSeed = 1)
    {
        int n = Math.Max(1, (int)(d.Length * Audio.Rate));
        var outp = new float[n];
        float hold = d.Attack + d.Sustain;
        float one = d.Attack + d.Sustain + d.Decay;
        for (int r = 0; r <= d.Repeats; r++)
        {
            int start = (int)(r * d.RepeatGap * Audio.Rate);
            float pitch = MathF.Pow(d.RepeatPitch, r), gain = MathF.Pow(0.8f, r);
            var rng = new Rng(noiseSeed + (ulong)r * 7919);
            int len = Math.Min(n - start, (int)(one * Audio.Rate) + 1);
            if (len <= 0) break;
            var tone = new float[len];
            var noise = new float[len];
            double phase = 0, phase2 = 0;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Audio.Rate;
                float k = Math.Clamp(t / Math.Max(hold, 1e-4f), 0, 1);
                float f = (d.Freq + (d.FreqEnd - d.Freq) * k) * pitch;
                if (d.VibratoDepth > 0) f *= 1 + d.VibratoDepth * MathF.Sin(MathF.Tau * d.VibratoHz * t);
                phase += f / Audio.Rate;
                phase2 += f * d.Partial / Audio.Rate;
                float s = d.Wave switch
                {
                    Wave.Sine => Dsp.Sine(phase),
                    Wave.Square => Dsp.Square(phase, d.Duty),
                    Wave.Saw => Dsp.Saw(phase),
                    Wave.Triangle => Dsp.Triangle(phase),
                    _ => rng.Bipolar(),
                };
                if (d.PartialLevel > 0) s += Dsp.Sine(phase2) * d.PartialLevel;
                tone[i] = s;
                noise[i] = rng.Bipolar();
            }
            if (d.Noise > 0) Dsp.LowPass(noise, _ => d.NoiseColor);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Audio.Rate;
                float env = Dsp.Adsr(t, d.Attack, 0.0001f, 1, d.Decay, hold);
                float v = tone[i] * (1 - d.Noise) + noise[i] * d.Noise * 1.6f;
                outp[start + i] += v * env * gain;
            }
        }
        if (d.Cutoff < 19999 || d.CutoffEnd < 19999)
            Dsp.LowPass(outp, i => d.Cutoff + (d.CutoffEnd - d.Cutoff) * Math.Clamp(i / (float)Audio.Rate / Math.Max(d.Attack + d.Sustain + d.Decay, 1e-3f), 0, 1));
        if (d.BandQ > 0) Dsp.BandPass(outp, d.BandCentre, d.BandQ);
        if (d.HighPass > 20) Dsp.HighPass(outp, d.HighPass);
        Dsp.Drive(outp, d.Drive);
        Dsp.Crush(outp, d.Crush);
        if (d.EchoFeedback > 0) Dsp.Echo(outp, d.EchoDelay, d.EchoFeedback, 1);
        Dsp.Reverb(outp, 0.7f, d.Room);
        Dsp.Normalize(outp, d.Volume);
        Dsp.Declick(outp);
        return outp;
    }

    /// <summary>The categories <see cref="Roll"/> knows, with what each is for.</summary>
    public static readonly (string name, string about)[] Categories =
    {
        ("coin", "a pickup: two quick rising blips"), ("laser", "a shot: a falling zap"), ("explosion", "a blast of noise and rumble"),
        ("powerup", "a rising arpeggio"), ("hit", "a hurt: a short noisy thump"), ("jump", "a quick rising sweep"),
        ("blip", "a menu select: a short beep"), ("click", "a UI click"), ("deny", "a refusal: a low double buzz"),
        ("impact", "something struck: stone, wood or metal"), ("whoosh", "air: a swing, a pass"), ("step", "a footstep"),
        ("bell", "a bell or chime"), ("door", "a door or gate: a heavy knock and creak"), ("magic", "a shimmering sparkle"),
        ("alarm", "an alarm: a repeating siren"), ("roar", "a beast: a low growl"), ("water", "a splash or rush of water"),
        ("fanfare", "a short triumph"),
    };

    /// <summary>A sound in a category, its details rolled from the seed (the same seed, the same sound).</summary>
    public static SfxDef Roll(string category, ulong seed)
    {
        var r = new Rng(seed);
        var d = new SfxDef { Name = $"{category}-{seed}" };
        switch (category)
        {
            case "coin":
                d.Wave = r.Pick(new[] { Wave.Square, Wave.Sine, Wave.Triangle }); d.Freq = d.FreqEnd = r.Range(700, 1300);
                d.Sustain = r.Range(0.03f, 0.07f); d.Decay = r.Range(0.1f, 0.25f); d.Repeats = 1; d.RepeatGap = d.Sustain + 0.01f; d.RepeatPitch = r.Range(1.25f, 1.5f);
                break;
            case "laser":
                d.Wave = r.Pick(new[] { Wave.Square, Wave.Saw }); d.Duty = r.Range(0.2f, 0.5f); d.Freq = r.Range(900, 2200); d.FreqEnd = d.Freq * r.Range(0.1f, 0.35f);
                d.Sustain = r.Range(0.05f, 0.15f); d.Decay = r.Range(0.05f, 0.15f); d.Cutoff = r.Range(3000, 9000); d.CutoffEnd = 1500;
                break;
            case "explosion":
                d.Wave = Wave.Noise; d.Noise = 1; d.NoiseColor = r.Range(1500, 5000); d.Freq = 60; d.Sustain = r.Range(0.1f, 0.3f); d.Decay = r.Range(0.5f, 1.2f);
                d.Cutoff = r.Range(3000, 6000); d.CutoffEnd = r.Range(200, 500); d.Drive = r.Range(1.5f, 3f); d.Room = r.Range(0.2f, 0.5f);
                break;
            case "powerup":
                d.Wave = r.Pick(new[] { Wave.Square, Wave.Triangle }); d.Freq = r.Range(300, 500); d.FreqEnd = d.Freq * r.Range(1.5f, 2.2f);
                d.Sustain = r.Range(0.1f, 0.2f); d.Decay = 0.15f; d.Repeats = r.Int(2, 4); d.RepeatGap = r.Range(0.07f, 0.11f); d.RepeatPitch = r.Range(1.12f, 1.26f);
                d.VibratoHz = 12; d.VibratoDepth = r.Range(0, 0.03f);
                break;
            case "hit":
                d.Wave = r.Pick(new[] { Wave.Square, Wave.Saw }); d.Noise = r.Range(0.4f, 0.8f); d.NoiseColor = r.Range(2000, 6000);
                d.Freq = r.Range(150, 400); d.FreqEnd = d.Freq * 0.4f; d.Sustain = 0.03f; d.Decay = r.Range(0.08f, 0.18f); d.Drive = 2;
                break;
            case "jump":
                d.Wave = r.Pick(new[] { Wave.Square, Wave.Sine }); d.Duty = r.Range(0.3f, 0.5f); d.Freq = r.Range(200, 350); d.FreqEnd = d.Freq * r.Range(1.8f, 3f);
                d.Sustain = r.Range(0.08f, 0.16f); d.Decay = 0.08f;
                break;
            case "blip":
                d.Wave = r.Pick(new[] { Wave.Square, Wave.Sine, Wave.Triangle }); d.Freq = d.FreqEnd = r.Range(500, 1400);
                d.Sustain = r.Range(0.02f, 0.05f); d.Decay = r.Range(0.03f, 0.08f);
                break;
            case "click":
                d.Wave = Wave.Square; d.Freq = d.FreqEnd = r.Range(1200, 2600); d.Noise = r.Range(0, 0.3f); d.Attack = 0.001f; d.Sustain = 0.005f; d.Decay = r.Range(0.015f, 0.04f); d.Volume = 0.6f;
                break;
            case "deny":
                d.Wave = Wave.Square; d.Freq = d.FreqEnd = r.Range(150, 240); d.Sustain = 0.07f; d.Decay = 0.05f; d.Repeats = 1; d.RepeatGap = 0.11f; d.RepeatPitch = r.Range(0.72f, 0.85f);
                d.Cutoff = d.CutoffEnd = 2500; d.Volume = 0.7f;
                break;
            case "impact": return Material(d, r.Pick(new[] { "stone", "wood", "metal" }), r, 1);
            case "whoosh":
                d.Wave = Wave.Noise; d.Noise = 1; d.NoiseColor = 9000; d.Attack = r.Range(0.05f, 0.15f); d.Sustain = r.Range(0.05f, 0.2f); d.Decay = r.Range(0.1f, 0.3f);
                d.BandQ = 2.5f; d.BandCentre = r.Range(600, 2500); d.Volume = 0.6f;
                break;
            case "step":
                d.Wave = Wave.Noise; d.Noise = 1; d.NoiseColor = r.Range(1000, 3000); d.Attack = 0.002f; d.Sustain = 0.01f; d.Decay = r.Range(0.05f, 0.1f);
                d.Freq = 90; d.Cutoff = d.CutoffEnd = r.Range(800, 2000); d.Volume = 0.6f;
                break;
            case "bell":
                d.Wave = Wave.Sine; d.Freq = d.FreqEnd = r.Range(300, 1200); d.Partial = r.Pick(new[] { 2.76f, 2.4f, 3.01f }); d.PartialLevel = r.Range(0.3f, 0.6f);
                d.Attack = 0.002f; d.Sustain = 0.02f; d.Decay = r.Range(1f, 2.5f); d.Room = r.Range(0.2f, 0.4f);
                break;
            case "door": return Door(d, r);
            case "magic":
                d.Wave = Wave.Sine; d.Freq = r.Range(800, 1500); d.FreqEnd = d.Freq * r.Range(1.5f, 2.5f); d.VibratoHz = r.Range(15, 30); d.VibratoDepth = 0.05f;
                d.Partial = 1.5f; d.PartialLevel = 0.4f; d.Sustain = r.Range(0.2f, 0.4f); d.Decay = 0.4f; d.Repeats = r.Int(2, 5); d.RepeatGap = 0.06f; d.RepeatPitch = 1.06f;
                d.Room = 0.5f; d.EchoDelay = 0.09f; d.EchoFeedback = 0.35f;
                break;
            case "alarm":
                d.Wave = r.Pick(new[] { Wave.Square, Wave.Saw }); d.Freq = r.Range(600, 900); d.FreqEnd = d.Freq * 1.4f; d.Sustain = 0.25f; d.Decay = 0.05f;
                d.Repeats = 3; d.RepeatGap = 0.32f; d.Cutoff = d.CutoffEnd = 4000; d.Volume = 0.7f;
                break;
            case "roar":
                d.Wave = Wave.Saw; d.Freq = r.Range(70, 120); d.FreqEnd = d.Freq * 0.7f; d.Noise = r.Range(0.3f, 0.5f); d.NoiseColor = 1500;
                d.VibratoHz = r.Range(18, 35); d.VibratoDepth = 0.08f; d.Attack = 0.15f; d.Sustain = r.Range(0.6f, 1.1f); d.Decay = 0.5f;
                d.Cutoff = d.CutoffEnd = 1800; d.Drive = 2.5f; d.Room = 0.3f;
                break;
            case "water":
                d.Wave = Wave.Noise; d.Noise = 1; d.NoiseColor = r.Range(3000, 7000); d.Attack = 0.01f; d.Sustain = r.Range(0.1f, 0.4f); d.Decay = r.Range(0.3f, 0.8f);
                d.BandQ = 1.2f; d.BandCentre = r.Range(800, 1800); d.VibratoHz = 9; d.Room = 0.3f; d.Volume = 0.7f;
                break;
            case "fanfare":
                d.Wave = r.Pick(new[] { Wave.Saw, Wave.Square }); d.Duty = 0.35f; d.Freq = d.FreqEnd = r.Range(330, 440); d.Sustain = 0.12f; d.Decay = 0.25f;
                d.Repeats = 3; d.RepeatGap = 0.15f; d.RepeatPitch = MathF.Pow(2, 4 / 12f); d.Cutoff = d.CutoffEnd = 3500; d.VibratoHz = 6; d.VibratoDepth = 0.01f; d.Room = 0.3f;
                break;
            default: throw new ArgumentException($"no category '{category}' (try: {string.Join(", ", Categories.Select(c => c.name))})");
        }
        return d;
    }

    /// <summary>Something struck: the material sets the ring (metal), the knock (wood) or the crack (stone).</summary>
    private static SfxDef Material(SfxDef d, string material, Rng r, float weight)
    {
        d.Attack = 0.001f;
        switch (material)
        {
            case "metal":
                d.Wave = Wave.Sine; d.Freq = d.FreqEnd = r.Range(900, 2400) / weight; d.Partial = r.Range(2.3f, 2.9f); d.PartialLevel = 0.6f;
                d.Noise = 0.25f; d.NoiseColor = 9000; d.Sustain = 0.01f; d.Decay = r.Range(0.3f, 0.8f) * weight; d.Room = 0.15f;
                break;
            case "wood":
                d.Wave = Wave.Sine; d.Freq = r.Range(140, 260) / weight; d.FreqEnd = d.Freq * 0.85f; d.Noise = 0.35f; d.NoiseColor = 2500;
                d.Sustain = 0.01f; d.Decay = r.Range(0.08f, 0.16f) * weight; d.Cutoff = d.CutoffEnd = 3000;
                break;
            case "glass":
                d.Wave = Wave.Sine; d.Freq = d.FreqEnd = r.Range(2500, 4500); d.Partial = 2.1f; d.PartialLevel = 0.5f; d.Noise = 0.4f; d.NoiseColor = 12000;
                d.Sustain = 0.005f; d.Decay = r.Range(0.2f, 0.4f); d.Repeats = r.Int(2, 5); d.RepeatGap = 0.03f; d.RepeatPitch = 1.1f;
                break;
            case "flesh":
                d.Wave = Wave.Sine; d.Freq = r.Range(80, 140); d.FreqEnd = d.Freq * 0.6f; d.Noise = 0.5f; d.NoiseColor = 1200;
                d.Sustain = 0.02f; d.Decay = r.Range(0.08f, 0.15f) * weight; d.Drive = 1.8f;
                break;
            default: // stone
                d.Wave = Wave.Sine; d.Freq = d.FreqEnd = r.Range(1800, 3400) / weight; d.Partial = 1.48f; d.PartialLevel = 0.5f;
                d.Noise = 0.55f; d.NoiseColor = r.Range(4000, 9000); d.Sustain = 0.005f; d.Decay = r.Range(0.1f, 0.22f) * weight;
                break;
        }
        return d;
    }

    private static SfxDef Door(SfxDef d, Rng r)
    {
        d.Wave = Wave.Sine; d.Freq = r.Range(90, 150); d.FreqEnd = d.Freq * 0.8f; d.Noise = 0.4f; d.NoiseColor = 2000; d.Attack = 0.002f;
        d.Sustain = 0.01f; d.Decay = r.Range(0.07f, 0.11f); d.Repeats = 1; d.RepeatGap = r.Range(0.22f, 0.3f); d.RepeatPitch = 0.95f; d.Room = 0.18f; d.Cutoff = d.CutoffEnd = 2500;
        return d;
    }

    private static readonly Dictionary<string, string> Words = new()
    {
        // what the sound is
        ["coin"] = "coin", ["pickup"] = "coin", ["gold"] = "coin", ["collect"] = "coin", ["laser"] = "laser", ["shoot"] = "laser", ["shot"] = "laser", ["zap"] = "laser",
        ["explosion"] = "explosion", ["explode"] = "explosion", ["blast"] = "explosion", ["boom"] = "explosion", ["powerup"] = "powerup", ["level"] = "powerup",
        ["hit"] = "hit", ["hurt"] = "hit", ["punch"] = "hit", ["damage"] = "hit", ["jump"] = "jump", ["hop"] = "jump", ["blip"] = "blip", ["select"] = "blip", ["menu"] = "blip",
        ["click"] = "click", ["button"] = "click", ["deny"] = "deny", ["error"] = "deny", ["refuse"] = "deny", ["no"] = "deny",
        ["impact"] = "impact", ["pickaxe"] = "impact", ["pick"] = "impact", ["hammer"] = "impact", ["anvil"] = "impact", ["axe"] = "impact", ["chop"] = "impact", ["strike"] = "impact", ["knock"] = "impact",
        ["whoosh"] = "whoosh", ["swing"] = "whoosh", ["swish"] = "whoosh", ["wind"] = "whoosh", ["arrow"] = "whoosh", ["step"] = "step", ["footstep"] = "step", ["walk"] = "step",
        ["bell"] = "bell", ["chime"] = "bell", ["gong"] = "bell", ["door"] = "door", ["gate"] = "door", ["magic"] = "magic", ["spell"] = "magic", ["sparkle"] = "magic", ["heal"] = "magic",
        ["alarm"] = "alarm", ["siren"] = "alarm", ["roar"] = "roar", ["growl"] = "roar", ["beast"] = "roar", ["monster"] = "roar", ["water"] = "water", ["splash"] = "water", ["flood"] = "water",
        ["fanfare"] = "fanfare", ["triumph"] = "fanfare", ["victory"] = "fanfare",
    };

    private static readonly Dictionary<string, string> MaterialWords = new()
    {
        ["stone"] = "stone", ["granite"] = "stone", ["rock"] = "stone", ["marble"] = "stone", ["brick"] = "stone",
        ["wood"] = "wood", ["wooden"] = "wood", ["oak"] = "wood", ["tree"] = "wood", ["log"] = "wood", ["chop"] = "wood", ["axe"] = "wood",
        ["metal"] = "metal", ["iron"] = "metal", ["steel"] = "metal", ["anvil"] = "metal", ["sword"] = "metal", ["bronze"] = "metal", ["armour"] = "metal", ["armor"] = "metal",
        ["glass"] = "glass", ["crystal"] = "glass", ["ice"] = "glass", ["flesh"] = "flesh", ["body"] = "flesh", ["punch"] = "flesh",
    };

    /// <summary>
    /// A sound from a few words: "pickaxe on granite", "heavy wooden door", "small quick coin", "distant explosion".
    /// The first word naming a kind of sound picks the category (default: impact); a material word sets what is struck;
    /// heavy/big/deep, small/light/high, long, short, distant, soft, loud, retro, echo shape it. The words seed the
    /// details, so the same words always give the same sound (pass a seed for variants).
    /// </summary>
    public static SfxDef Describe(string text, ulong variant = 0)
    {
        string[] w = text.ToLowerInvariant().Split(new[] { ' ', ',', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
        string cat = w.Select(x => Words.GetValueOrDefault(x)).FirstOrDefault(x => x != null) ?? "impact";
        string mat = w.Select(x => MaterialWords.GetValueOrDefault(x)).FirstOrDefault(x => x != null);
        ulong seed = Rng.Hash(text) + variant * 104729;
        var r = new Rng(seed);
        float weight = w.Any(x => x is "heavy" or "big" or "huge" or "deep" or "large" or "great") ? 1.6f : w.Any(x => x is "small" or "light" or "tiny" or "high") ? 0.65f : 1;
        SfxDef d = cat == "impact" || (mat != null && cat is "door" or "step")
            ? Material(new SfxDef(), mat ?? (w.Contains("pickaxe") || w.Contains("pick") ? "stone" : "wood"), r, weight)
            : Roll(cat, seed);
        if (cat == "door") { d.Repeats = 1; d.RepeatGap = 0.25f; d.Decay = Math.Min(d.Decay, 0.12f); d.Room = Math.Max(d.Room, 0.18f); }
        if (cat == "step") { d.Decay = Math.Min(d.Decay, 0.1f); d.Volume = 0.6f; }
        if (cat != "impact" && weight != 1) { d.Freq /= weight; d.FreqEnd /= weight; d.Decay *= weight; }
        if (w.Contains("long")) { d.Sustain *= 2; d.Decay *= 2; }
        if (w.Contains("short") || w.Contains("quick")) { d.Sustain *= 0.5f; d.Decay *= 0.5f; }
        if (w.Contains("distant") || w.Contains("far")) { d.Cutoff = Math.Min(d.Cutoff, 1800); d.CutoffEnd = Math.Min(d.CutoffEnd, 1500); d.Room = Math.Max(d.Room, 0.5f); d.Volume *= 0.6f; }
        if (w.Contains("soft") || w.Contains("quiet")) d.Volume *= 0.55f;
        if (w.Contains("loud")) d.Drive = Math.Max(d.Drive, 1.8f);
        if (w.Contains("retro") || w.Contains("8bit") || w.Contains("chiptune")) { d.Crush = 0.5f; d.Room = 0; }
        if (w.Contains("echo") || w.Contains("cave") || w.Contains("cavern") || w.Contains("hall")) { d.Room = Math.Max(d.Room, 0.6f); d.EchoDelay = 0.18f; d.EchoFeedback = 0.35f; }
        d.Name = string.Join('-', w.Take(4));
        return d;
    }
}
