using System;
using System.IO;

namespace SfxGen;

/// <summary>A small deterministic random source (xorshift), so a seed always gives the same sound.</summary>
public sealed class Rng
{
    private ulong _s;
    public Rng(ulong seed) { _s = seed * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL; if (_s == 0) _s = 1; Next(); }
    public Rng(string seed) : this(Hash(seed)) { }

    public static ulong Hash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s ?? "") { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    public ulong Next() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; return _s; }
    /// <summary>0 (inclusive) to 1 (exclusive).</summary>
    public double Float() => (Next() >> 11) * (1.0 / (1UL << 53));
    public float Range(float lo, float hi) => lo + (float)Float() * (hi - lo);
    public int Int(int lo, int hiExclusive) => lo + (int)(Float() * (hiExclusive - lo));
    public bool Chance(double p) => Float() < p;
    public T Pick<T>(T[] items) => items[Int(0, items.Length)];
    /// <summary>-1..1.</summary>
    public float Bipolar() => (float)Float() * 2 - 1;
}

/// <summary>Oscillators, noise, filters and effects on float buffers at <see cref="Audio.Rate"/>.</summary>
public static class Dsp
{
    public static float Sine(double phase) => (float)Math.Sin(phase * Math.Tau);
    public static float Square(double phase, float duty = 0.5f) => (phase - Math.Floor(phase)) < duty ? 1 : -1;
    public static float Saw(double phase) => (float)(2 * (phase - Math.Floor(phase)) - 1);
    public static float Triangle(double phase) { double p = phase - Math.Floor(phase); return (float)(p < 0.5 ? 4 * p - 1 : 3 - 4 * p); }

    /// <summary>Attack-decay-sustain-release level at time t (seconds) for a note held `hold` seconds.</summary>
    public static float Adsr(float t, float attack, float decay, float sustain, float release, float hold)
    {
        if (t < 0) return 0;
        float level;
        if (t < attack) level = t / Math.Max(attack, 1e-5f);
        else if (t < attack + decay) level = 1 - (1 - sustain) * (t - attack) / Math.Max(decay, 1e-5f);
        else level = sustain;
        if (t > hold) level *= Math.Max(0, 1 - (t - hold) / Math.Max(release, 1e-5f));
        return level;
    }

    /// <summary>One-pole low-pass in place; cutoff in Hz (may change over the buffer through `cutoffAt`).</summary>
    public static void LowPass(float[] x, Func<int, float> cutoffAt)
    {
        float y = 0;
        for (int i = 0; i < x.Length; i++)
        {
            float a = 1 - MathF.Exp(-MathF.Tau * Math.Clamp(cutoffAt(i), 10, Audio.Rate / 2f) / Audio.Rate);
            y += a * (x[i] - y);
            x[i] = y;
        }
    }

    public static void HighPass(float[] x, float cutoff)
    {
        float a = 1 - MathF.Exp(-MathF.Tau * cutoff / Audio.Rate), low = 0;
        for (int i = 0; i < x.Length; i++) { low += a * (x[i] - low); x[i] -= low; }
    }

    /// <summary>A resonant band-pass (state-variable filter) in place.</summary>
    public static void BandPass(float[] x, float centre, float q)
    {
        float f = 2 * MathF.Sin(MathF.PI * Math.Clamp(centre, 20, Audio.Rate / 6f) / Audio.Rate), damp = 1 / Math.Max(q, 0.5f), low = 0, band = 0;
        for (int i = 0; i < x.Length; i++)
        {
            float high = x[i] - low - damp * band;
            band += f * high;
            low += f * band;
            x[i] = band;
        }
    }

    /// <summary>Feedback echo in place.</summary>
    public static void Echo(float[] x, float delaySeconds, float feedback, float mix)
    {
        int d = Math.Max(1, (int)(delaySeconds * Audio.Rate));
        for (int i = d; i < x.Length; i++) x[i] += x[i - d] * feedback * mix;
    }

    /// <summary>A small room: four combs and two all-passes (Schroeder), mixed in.</summary>
    public static void Reverb(float[] x, float size, float mix)
    {
        if (mix <= 0) return;
        int[] combs = { 1116, 1188, 1277, 1356 }, alls = { 556, 441 };
        float scale = Audio.Rate / 44100f * Math.Clamp(size, 0.2f, 2f);
        var wet = new float[x.Length];
        foreach (int c in combs)
        {
            int d = Math.Max(1, (int)(c * scale));
            var buf = new float[d];
            float fb = 0.78f + 0.1f * Math.Clamp(size, 0, 1), lp = 0;
            for (int i = 0; i < x.Length; i++)
            {
                float o = buf[i % d];
                lp = o * 0.7f + lp * 0.3f;
                buf[i % d] = x[i] + lp * fb;
                wet[i] += o * 0.25f;
            }
        }
        foreach (int a in alls)
        {
            int d = Math.Max(1, (int)(a * scale));
            var buf = new float[d];
            for (int i = 0; i < wet.Length; i++)
            {
                float b = buf[i % d], v = wet[i];
                buf[i % d] = v + b * 0.5f;
                wet[i] = b - v * 0.5f;
            }
        }
        for (int i = 0; i < x.Length; i++) x[i] = x[i] * (1 - mix * 0.5f) + wet[i] * mix;
    }

    /// <summary>Soft clipping (drive above 1 adds grit).</summary>
    public static void Drive(float[] x, float drive)
    {
        if (drive <= 1.001f) return;
        float norm = MathF.Tanh(drive);
        for (int i = 0; i < x.Length; i++) x[i] = MathF.Tanh(x[i] * drive) / norm;
    }

    /// <summary>Reduce the sample rate and depth, for a retro crunch (0 = off).</summary>
    public static void Crush(float[] x, float amount)
    {
        if (amount <= 0) return;
        int hold = 1 + (int)(amount * 12);
        float levels = MathF.Max(4, 256 * (1 - amount));
        float v = 0;
        for (int i = 0; i < x.Length; i++)
        {
            if (i % hold == 0) v = MathF.Round(x[i] * levels) / levels;
            x[i] = v;
        }
    }

    /// <summary>Scales so the loudest sample is at `peak`; quiet buffers are left alone.</summary>
    public static void Normalize(float[] x, float peak = 0.9f)
    {
        float m = 0;
        foreach (float v in x) m = Math.Max(m, Math.Abs(v));
        if (m < 1e-6f) return;
        float k = peak / m;
        for (int i = 0; i < x.Length; i++) x[i] *= k;
    }

    /// <summary>Short fades at both ends, so nothing clicks.</summary>
    public static void Declick(float[] x, float seconds = 0.003f)
    {
        int n = Math.Min(x.Length / 2, (int)(seconds * Audio.Rate));
        for (int i = 0; i < n; i++) { float k = i / (float)n; x[i] *= k; x[x.Length - 1 - i] *= k; }
    }
}

/// <summary>Sample format and the WAV writer.</summary>
public static class Audio
{
    /// <summary>Samples per second everywhere in sfxgen.</summary>
    public const int Rate = 44100;

    /// <summary>16-bit PCM bytes, interleaved when there are two channels.</summary>
    public static byte[] Pcm16(float[] left, float[] right = null)
    {
        int ch = right == null ? 1 : 2, n = left.Length;
        var data = new byte[n * ch * 2];
        int o = 0;
        for (int i = 0; i < n; i++)
            for (int c = 0; c < ch; c++)
            {
                float v = c == 0 ? left[i] : right[i];
                short s = (short)(Math.Clamp(v, -1f, 1f) * 32000);
                data[o++] = (byte)s;
                data[o++] = (byte)(s >> 8);
            }
        return data;
    }

    /// <summary>A whole .wav file (16-bit PCM, mono or stereo).</summary>
    public static byte[] Wav(float[] left, float[] right = null)
    {
        byte[] pcm = Pcm16(left, right);
        short ch = (short)(right == null ? 1 : 2);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + pcm.Length); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write(ch); w.Write(Rate);
        w.Write(Rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(pcm.Length); w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }

    public static void Save(string path, float[] left, float[] right = null) => File.WriteAllBytes(path, Wav(left, right));
}
