// sfxgen for Godot 4 (C#): copy this file and src/SfxGen/*.cs into the game (e.g. addons/sfxgen/).
using System;
using System.Collections.Generic;
using Godot;

namespace SfxGen;

/// <summary>
/// Sounds made at start-up from sfxgen defs (no audio files), played from a small pool of players; and music
/// composed into a looping stream. Safe headless: nothing is built or played without an audio device.
/// <code>
/// var bank = new SoundBank();
/// AddChild(bank);
/// bank.Add("click", Effects.Roll("click", 3));
/// bank.Add("pick", Effects.Describe("pickaxe on granite"));
/// bank.Play("pick", volume: 0.5f, pitchJitter: 0.1f, minGapMs: 80);
/// musicPlayer.Stream = SoundBank.MusicStream(new MusicSpec { Mood = "tavern", Bars = 16 });
/// </code>
/// </summary>
public partial class SoundBank : Node
{
    public static SoundBank Instance { get; private set; }
    /// <summary>How many sounds may play at once.</summary>
    public int Voices = 8;
    /// <summary>The audio bus the players use.</summary>
    public string Bus = "Master";

    private readonly Dictionary<string, AudioStreamWav> _sounds = new();
    private readonly Dictionary<string, ulong> _last = new();
    private readonly List<AudioStreamPlayer> _pool = new();
    private readonly RandomNumberGenerator _rng = new();

    public static bool Silent => DisplayServer.GetName() == "headless";

    public override void _Ready()
    {
        Instance = this;
        if (Silent) return;
        for (int k = 0; k < Voices; k++) { var p = new AudioStreamPlayer { Bus = Bus }; AddChild(p); _pool.Add(p); }
    }

    /// <summary>Renders a def under a name (a few milliseconds; done once).</summary>
    public void Add(string name, SfxDef def)
    {
        if (Silent) return;
        _sounds[name] = Stream(Effects.Render(def));
    }

    public bool Has(string name) => _sounds.ContainsKey(name);

    /// <summary>Plays a sound; skipped when it played less than minGapMs ago or every voice is busy.</summary>
    public void Play(string name, float volume = 1, float pitch = 1, float pitchJitter = 0, int minGapMs = 0)
    {
        if (!_sounds.TryGetValue(name, out AudioStreamWav s)) return;
        ulong now = Time.GetTicksMsec();
        if (minGapMs > 0 && _last.TryGetValue(name, out ulong last) && now - last < (ulong)minGapMs) return;
        _last[name] = now;
        foreach (AudioStreamPlayer p in _pool)
        {
            if (p.Playing) continue;
            p.Stream = s;
            p.VolumeDb = Mathf.LinearToDb(Math.Max(0.001f, volume));
            p.PitchScale = Math.Max(0.05f, pitch + (pitchJitter > 0 ? _rng.RandfRange(-pitchJitter, pitchJitter) : 0));
            p.Play();
            return;
        }
    }

    /// <summary>Samples as a 16-bit stream.</summary>
    public static AudioStreamWav Stream(float[] left, float[] right = null) => new()
    {
        Data = Audio.Pcm16(left, right), Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Audio.Rate, Stereo = right != null,
    };

    /// <summary>A piece of music as a stream that loops end to start (sfxgen composes it to loop seamlessly).</summary>
    public static AudioStreamWav MusicStream(MusicSpec spec)
    {
        var (l, r) = Music.Compose(spec);
        AudioStreamWav s = Stream(l, r);
        s.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        s.LoopBegin = 0;
        s.LoopEnd = l.Length;
        return s;
    }
}
