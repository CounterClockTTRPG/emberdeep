using System;
using System.Collections.Generic;

namespace Starter;

/// <summary>
/// The whole game as plain data, with no Godot types: what a save holds, what the self-check tests, what the screens
/// draw. The sample: walk a grid and gather the gems that appear; ten wins. Replace it with your own rules, and keep
/// them here -- free of Godot, so they are quick to test and easy to save.
/// </summary>
public sealed class Game
{
    public const int W = 16, H = 10, Goal = 10, MaxGems = 8;
    /// <summary>Seconds between new gems.</summary>
    public const double SpawnEvery = 3;

    public ulong Seed;
    public int X, Y, Score, Steps, Spawned;
    public double Time;
    /// <summary>Gem cells, as y * W + x.</summary>
    public List<int> Gems = new();
    /// <summary>Walls, as y * W + x.</summary>
    public HashSet<int> Walls = new();

    public bool Won => Score >= Goal;

    public static Game New(ulong seed)
    {
        var g = new Game { Seed = seed, X = W / 2, Y = H / 2 };
        var r = new Rng(seed);
        for (int k = 0; k < 14; k++)
        {
            int c = r.Int(0, W * H);
            if (c != g.Y * W + g.X) g.Walls.Add(c);
        }
        for (int k = 0; k < 3; k++) g.Spawn();
        return g;
    }

    public static bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
    public bool Open(int x, int y) => In(x, y) && !Walls.Contains(y * W + x);

    /// <summary>One step; returns what happened, for the screen to show and sound: "blocked", "moved", "gem", "won".</summary>
    public string Move(int dx, int dy)
    {
        if (Won) return "blocked";
        int nx = X + dx, ny = Y + dy;
        if (!Open(nx, ny)) return "blocked";
        X = nx; Y = ny; Steps++;
        if (!Gems.Remove(Y * W + X)) return "moved";
        Score++;
        return Won ? "won" : "gem";
    }

    /// <summary>Time passes: a new gem every few seconds, at a place drawn from the seed (so a game replays the same).</summary>
    public void Step(double dt)
    {
        if (Won) return;
        double before = Time;
        Time += dt;
        if (Math.Floor(Time / SpawnEvery) > Math.Floor(before / SpawnEvery)) Spawn();
    }

    private void Spawn()
    {
        if (Gems.Count >= MaxGems) return;
        var r = new Rng(Seed + (ulong)Spawned * 7919);
        Spawned++;
        for (int tries = 0; tries < 50; tries++)
        {
            int c = r.Int(0, W * H);
            if (!Walls.Contains(c) && !Gems.Contains(c) && c != Y * W + X) { Gems.Add(c); return; }
        }
    }
}

/// <summary>A small deterministic random source (xorshift): the same seed, the same game.</summary>
public sealed class Rng
{
    private ulong _s;
    public Rng(ulong seed) { _s = seed * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL; if (_s == 0) _s = 1; Next(); }
    public ulong Next() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; return _s; }
    public double Float() => (Next() >> 11) * (1.0 / (1UL << 53));
    public int Int(int lo, int hiExclusive) => lo + (int)(Float() * (hiExclusive - lo));
    public bool Chance(double p) => Float() < p;
}
