using System;
using System.Collections.Generic;
using System.Linq;

namespace SfxGen;

/// <summary>What to compose. Unset fields come from the mood.</summary>
public sealed class MusicSpec
{
    public string Mood = "calm";
    public string Seed = "sfxgen";
    public int Bars = 8;
    /// <summary>Beats a minute; 0: the mood's.</summary>
    public int Tempo = 0;
    /// <summary>The key's root as a MIDI note (57 = A3); -1: rolled.</summary>
    public int Root = -1;
    /// <summary>Instruments to leave out or force in (drone, pad, pluck, bell, bass, lead, drums); null: the mood's.</summary>
    public string[] Instruments;
}

/// <summary>A mood: its scale, pace, progressions and band.</summary>
public sealed class Mood
{
    public string Name, About;
    public int[] Scale;
    public int TempoLo, TempoHi, Beats = 4;
    public int[][] Progressions;
    public string[] Band;
    public float Room = 0.3f, Busy = 0.5f;
}

/// <summary>Composes loopable music, as it would be played: a stereo buffer whose end runs straight into its start.</summary>
public static class Music
{
    private static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 }, Minor = { 0, 2, 3, 5, 7, 8, 10 }, Dorian = { 0, 2, 3, 5, 7, 9, 10 },
        Phrygian = { 0, 1, 3, 5, 7, 8, 10 }, Mixolydian = { 0, 2, 4, 5, 7, 9, 10 }, HarmonicMinor = { 0, 2, 3, 5, 7, 8, 11 };

    public static readonly Mood[] Moods =
    {
        new() { Name = "calm", About = "gentle: pads, a plucked lute, a flute, a few bells", Scale = Major, TempoLo = 72, TempoHi = 90,
            Progressions = new[] { new[] { 0, 4, 5, 3 }, new[] { 0, 3, 4, 0 }, new[] { 0, 5, 3, 4 } }, Band = new[] { "pad", "pluck", "lead", "bell", "bass" }, Busy = 0.35f },
        new() { Name = "tense", About = "something is wrong: a drone, a pulsing bass, low toms", Scale = Phrygian, TempoLo = 92, TempoHi = 108,
            Progressions = new[] { new[] { 0, 0, 1, 0 }, new[] { 0, 6, 5, 6 } }, Band = new[] { "drone", "bass", "pad", "drums", "bell" }, Busy = 0.4f },
        new() { Name = "battle", About = "fighting: drums, driving bass, a hard lead", Scale = HarmonicMinor, TempoLo = 132, TempoHi = 156,
            Progressions = new[] { new[] { 0, 5, 3, 4 }, new[] { 0, 3, 5, 4 }, new[] { 0, 6, 5, 4 } }, Band = new[] { "drums", "bass", "pad", "lead" }, Busy = 0.75f },
        new() { Name = "mystery", About = "the deep and the unknown: drone, sparse bells, a wide room", Scale = Dorian, TempoLo = 58, TempoHi = 72,
            Progressions = new[] { new[] { 0, 1, 0, 6 }, new[] { 0, 3, 1, 0 } }, Band = new[] { "drone", "bell", "pad", "pluck" }, Room = 0.6f, Busy = 0.25f },
        new() { Name = "triumph", About = "victory: drums, a brass-like lead, a big pad", Scale = Major, TempoLo = 108, TempoHi = 124,
            Progressions = new[] { new[] { 0, 3, 4, 0 }, new[] { 0, 4, 3, 4 } }, Band = new[] { "drums", "bass", "pad", "lead", "bell" }, Busy = 0.6f },
        new() { Name = "sad", About = "loss: a slow pad, a lone pluck, a flute", Scale = Minor, TempoLo = 60, TempoHi = 72,
            Progressions = new[] { new[] { 0, 5, 3, 4 }, new[] { 0, 3, 6, 4 } }, Band = new[] { "pad", "pluck", "lead" }, Room = 0.45f, Busy = 0.3f },
        new() { Name = "tavern", About = "a hall full of ale: a lute in three, a hand drum, a whistle", Scale = Mixolydian, TempoLo = 120, TempoHi = 140, Beats = 3,
            Progressions = new[] { new[] { 0, 6, 0, 4 }, new[] { 0, 3, 6, 0 } }, Band = new[] { "pluck", "bass", "drums", "lead" }, Busy = 0.7f },
        new() { Name = "deep", About = "far underground: a low drone, a slow heartbeat, distant bells", Scale = Phrygian, TempoLo = 50, TempoHi = 60,
            Progressions = new[] { new[] { 0, 0, 1, 0 } }, Band = new[] { "drone", "bell", "drums" }, Room = 0.7f, Busy = 0.15f },
    };

    public static Mood MoodOf(string name) => Moods.FirstOrDefault(m => m.Name == name) ?? throw new ArgumentException($"no mood '{name}' (try: {string.Join(", ", Moods.Select(m => m.Name))})");

    private static float Hz(int midi) => 440f * MathF.Pow(2, (midi - 69) / 12f);

    /// <summary>The piece, left and right, exactly Bars long: its tails are folded back onto its start so it loops.</summary>
    public static (float[] left, float[] right) Compose(MusicSpec spec)
    {
        Mood m = MoodOf(spec.Mood);
        var r = new Rng($"{spec.Seed}/{spec.Mood}");
        int tempo = spec.Tempo > 0 ? spec.Tempo : r.Int(m.TempoLo, m.TempoHi + 1);
        int root = spec.Root >= 0 ? spec.Root : r.Int(50, 58);
        float beat = 60f / tempo;
        int beats = m.Beats, bars = Math.Max(1, spec.Bars);
        int length = (int)(bars * beats * beat * Audio.Rate);
        int tail = (int)(3f * Audio.Rate);
        var L = new float[length + tail];
        var R = new float[length + tail];
        var band = (spec.Instruments ?? m.Band).ToHashSet();
        int[] prog = r.Pick(m.Progressions);

        // Chord tones of a scale degree: root, third, fifth (in the scale), as semitones above the key.
        int Deg(int degree) => m.Scale[((degree % 7) + 7) % 7] + 12 * (int)Math.Floor(degree / 7.0);
        int[] Chord(int degree) => new[] { Deg(degree), Deg(degree + 2), Deg(degree + 4) };

        void Add(float[] note, double at, float gain, float pan)
        {
            int start = (int)(at * Audio.Rate);
            float gl = gain * MathF.Sqrt(1 - pan) * 0.9f, gr = gain * MathF.Sqrt(pan) * 0.9f;
            for (int i = 0; i < note.Length && start + i < L.Length; i++) { L[start + i] += note[i] * gl; R[start + i] += note[i] * gr; }
        }

        int melody = 7; // a scale degree above the key, for the lead's walk
        for (int bar = 0; bar < bars; bar++)
        {
            int degree = prog[bar % prog.Length];
            int[] chord = Chord(degree);
            double t0 = bar * beats * beat;
            float barLen = beats * beat;
            if (band.Contains("drone") && bar % 2 == 0)
            {
                Add(Drone(Hz(root - 12), barLen * 2 + 1.5f), t0, 0.32f, 0.5f);
                Add(Drone(Hz(root - 5), barLen * 2 + 1.5f), t0, 0.18f, 0.5f);
            }
            if (band.Contains("pad"))
                for (int k = 0; k < 3; k++) Add(Pad(Hz(root + chord[k]), barLen + 0.8f, r), t0, 0.12f, 0.3f + 0.2f * k);
            if (band.Contains("bass"))
            {
                bool pulse = m.Busy > 0.5f || m.Name == "tense";
                int steps = pulse ? beats * 2 : beats;
                for (int s = 0; s < steps; s++)
                {
                    if (!pulse && s % 2 == 1 && r.Chance(0.5)) continue;
                    int note = root - 12 + chord[s % 4 == 2 && r.Chance(0.4) ? 2 : 0];
                    Add(Bass(Hz(note), beat * (pulse ? 0.45f : 0.9f)), t0 + s * beat * beats / steps, 0.36f, 0.5f);
                }
            }
            if (band.Contains("pluck"))
            {
                int per = m.Busy > 0.5f ? beats * 2 : beats;
                for (int s = 0; s < per; s++)
                {
                    if (r.Chance(0.3 - m.Busy * 0.2)) continue;
                    int note = root + chord[(s + bar) % 3] + (s % per == per - 1 ? 12 : 0);
                    Add(Pluck(Hz(note), 1.4f, r), t0 + s * barLen / per, 0.24f, 0.35f + 0.3f * (float)r.Float());
                }
            }
            if (band.Contains("bell") && (bar % 2 == 1 || r.Chance(m.Busy)))
                Add(Bell(Hz(root + 12 + chord[r.Int(0, 3)]), 2.6f), t0 + beat * r.Int(0, beats), 0.13f, (float)r.Float());
            if (band.Contains("lead") && bar % 4 != 3)
            {
                // A walk on the scale that lands on chord tones on the strong beats.
                int notes = m.Busy > 0.6f ? beats * 2 : beats;
                double t = t0;
                for (int s = 0; s < notes; s++)
                {
                    float len = barLen / notes * (r.Chance(0.25) && s < notes - 1 ? 2 : 1);
                    if (s % (notes / beats) == 0 && r.Chance(0.6)) melody = chord.Select(c => Array.IndexOf(m.Scale, c % 12)).Where(i => i >= 0).Select(i => i + 7).OrderBy(i => Math.Abs(i - melody)).First();
                    else melody = Math.Clamp(melody + r.Pick(new[] { -2, -1, -1, 1, 1, 2 }), 3, 13);
                    if (!r.Chance(0.15)) Add(Lead(Hz(root + Deg(melody)), len * 0.95f, m.Name is "battle" or "triumph"), t, 0.16f, 0.58f);
                    t += len;
                    if (t >= t0 + barLen - 1e-3) break;
                }
            }
            if (band.Contains("drums"))
            {
                for (int s = 0; s < beats * 2; s++)
                {
                    double at = t0 + s * beat / 2;
                    bool onBeat = s % 2 == 0;
                    if (m.Name == "deep") { if (s == 0 || s == 1) Add(Kick(0.9f), at, 0.5f, 0.5f); continue; }
                    if (m.Name == "tense") { if (s == 0 || (s == beats * 2 - 2 && r.Chance(0.6))) Add(Tom(90 + r.Int(0, 30)), at, 0.4f, 0.4f); continue; }
                    if (onBeat && (s / 2 % 2 == 0 || m.Busy > 0.7f)) Add(Kick(0.6f), at, 0.55f, 0.5f);
                    if (onBeat && s / 2 % 2 == 1 && m.Name != "tavern") Add(Snare(r), at, 0.32f, 0.55f);
                    if (m.Name == "tavern" && onBeat && s > 0) Add(Tom(180), at, 0.28f, 0.6f);
                    if (m.Busy > 0.55f && r.Chance(0.85)) Add(Hat(r), at, onBeat ? 0.09f : 0.06f, 0.65f);
                }
                if (bar % 4 == 3 && m.Busy > 0.55f) for (int k = 0; k < 4; k++) Add(Tom(160 - k * 25), t0 + (beats - 1) * beat + k * beat / 4, 0.3f, 0.3f + k * 0.12f);
            }
        }
        Dsp.Reverb(L, 1.1f, m.Room);
        Dsp.Reverb(R, 1.25f, m.Room);
        // Fold what rings on past the end back onto the start: played in a loop, it never jumps.
        var outL = new float[length];
        var outR = new float[length];
        for (int i = 0; i < L.Length; i++) { outL[i % length] += L[i]; outR[i % length] += R[i]; }
        // Every mood at the same peak, so a quiet one is not lost next to a loud one.
        float peak = Math.Max(outL.Max(Math.Abs), outR.Max(Math.Abs));
        if (peak > 1e-6f) { float k = 0.9f / peak; for (int i = 0; i < length; i++) { outL[i] *= k; outR[i] *= k; } }
        return (outL, outR);
    }

    // ---- the band -------------------------------------------------------------------------

    private static float[] Buffer(float seconds) => new float[Math.Max(1, (int)(seconds * Audio.Rate))];

    /// <summary>A plucked string (Karplus-Strong): a burst of noise in a short delay line, losing its brightness.</summary>
    public static float[] Pluck(float hz, float seconds, Rng r)
    {
        var o = Buffer(seconds);
        int period = Math.Max(2, (int)(Audio.Rate / hz));
        var line = new float[period];
        for (int i = 0; i < period; i++) line[i] = r.Bipolar() * 0.8f;
        for (int i = 0; i < o.Length; i++)
        {
            int k = i % period;
            float v = line[k];
            line[k] = (v + line[(k + 1) % period]) * 0.497f;
            o[i] = v;
        }
        Dsp.Declick(o, 0.002f);
        return o;
    }

    public static float[] Pad(float hz, float seconds, Rng r)
    {
        var o = Buffer(seconds);
        double p1 = r.Float(), p2 = r.Float(), p3 = r.Float();
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)Audio.Rate;
            p1 += hz / Audio.Rate; p2 += hz * 1.006 / Audio.Rate; p3 += hz * 0.994 / Audio.Rate;
            o[i] = (Dsp.Saw(p1) + Dsp.Saw(p2) + Dsp.Saw(p3)) / 3 * Dsp.Adsr(t, 0.5f, 0.3f, 0.8f, 0.7f, seconds - 0.7f);
        }
        Dsp.LowPass(o, _ => hz * 3.5f);
        return o;
    }

    public static float[] Bell(float hz, float seconds)
    {
        var o = Buffer(seconds);
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)Audio.Rate;
            o[i] = (MathF.Sin(MathF.Tau * hz * t) + 0.5f * MathF.Sin(MathF.Tau * hz * 2.76f * t) * MathF.Exp(-t * 3) + 0.25f * MathF.Sin(MathF.Tau * hz * 5.4f * t) * MathF.Exp(-t * 6)) * MathF.Exp(-t * 1.6f);
        }
        Dsp.Declick(o, 0.001f);
        return o;
    }

    public static float[] Bass(float hz, float seconds)
    {
        var o = Buffer(seconds + 0.08f);
        double p = 0;
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)Audio.Rate;
            p += hz / Audio.Rate;
            o[i] = (Dsp.Triangle(p) * 0.8f + Dsp.Saw(p) * 0.2f) * Dsp.Adsr(t, 0.008f, 0.1f, 0.7f, 0.08f, seconds);
        }
        Dsp.LowPass(o, _ => hz * 6);
        return o;
    }

    public static float[] Drone(float hz, float seconds)
    {
        var o = Buffer(seconds);
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)Audio.Rate;
            float swell = Dsp.Adsr(t, seconds * 0.35f, 0.01f, 1, seconds * 0.35f, seconds * 0.65f);
            o[i] = (MathF.Sin(MathF.Tau * hz * t) + 0.3f * MathF.Sin(MathF.Tau * hz * 2.002f * t) + 0.15f * MathF.Sin(MathF.Tau * hz * 3 * t)) * swell * (0.85f + 0.15f * MathF.Sin(t * 0.7f));
        }
        return o;
    }

    /// <summary>A breathy whistle, or with `bright` a brassy saw.</summary>
    public static float[] Lead(float hz, float seconds, bool bright)
    {
        var o = Buffer(seconds + 0.1f);
        var r = new Rng((ulong)(hz * 1000));
        double p = 0;
        float breath = 0;
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)Audio.Rate;
            p += hz * (1 + 0.006f * MathF.Sin(MathF.Tau * 5.5f * t) * Math.Min(1, t * 3)) / Audio.Rate;
            breath += 0.05f * (r.Bipolar() - breath);
            float tone = bright ? Dsp.Saw(p) * 0.6f + Dsp.Square(p, 0.3f) * 0.2f : Dsp.Sine(p) + 0.15f * Dsp.Sine(p * 2) + breath * 0.6f;
            o[i] = tone * Dsp.Adsr(t, bright ? 0.02f : 0.04f, 0.08f, 0.8f, 0.1f, seconds);
        }
        if (bright) Dsp.LowPass(o, _ => hz * 5);
        return o;
    }

    public static float[] Kick(float weight)
    {
        var o = Buffer(0.35f);
        double p = 0;
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)Audio.Rate;
            p += (45 + 110 * MathF.Exp(-t * 30)) * weight / Audio.Rate;
            o[i] = Dsp.Sine(p) * MathF.Exp(-t * 9);
        }
        return o;
    }

    public static float[] Snare(Rng r)
    {
        var o = Buffer(0.22f);
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)Audio.Rate;
            o[i] = (r.Bipolar() * 0.7f + MathF.Sin(MathF.Tau * 190 * t) * 0.4f) * MathF.Exp(-t * 22);
        }
        Dsp.HighPass(o, 300);
        return o;
    }

    public static float[] Hat(Rng r)
    {
        var o = Buffer(0.06f);
        for (int i = 0; i < o.Length; i++) o[i] = r.Bipolar() * MathF.Exp(-i / (float)Audio.Rate * 70);
        Dsp.HighPass(o, 6000);
        return o;
    }

    public static float[] Tom(float hz)
    {
        var o = Buffer(0.4f);
        double p = 0;
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)Audio.Rate;
            p += hz * (1 + 0.5f * MathF.Exp(-t * 25)) / Audio.Rate;
            o[i] = Dsp.Sine(p) * MathF.Exp(-t * 8);
        }
        return o;
    }
}
