using System.Collections.Generic;
using Godot;
using SfxGen;

namespace Emberdeep;

/// <summary>
/// Every sound and the music, made by sfxgen from seeds and words (no audio files). Calls are safe with no audio device
/// (headless) and before the bank exists. Music changes with the place: the village, the deep, the Warden's hall.
/// </summary>
public static class Sounds
{
    private static SoundBank _bank;
    private static AudioStreamPlayer _music;
    private static readonly Dictionary<string, AudioStream> _songs = new();
    private static string _mood;

    /// <summary>The game's sounds: a name, and how sfxgen makes it. Tune them by changing the seed or the words.</summary>
    public static readonly (string name, SfxDef def)[] All =
    {
        ("click", Effects.Roll("click", 2)),
        ("swing", Effects.Describe("quick whoosh", 1)),
        ("heavy", Effects.Describe("heavy deep whoosh", 2)),
        ("bow", Effects.Describe("short light whoosh", 3)),
        ("hit", Effects.Describe("punch flesh", 1)),
        ("hitmetal", Effects.Describe("metal sword on armour", 2)),
        ("crit", Effects.Describe("heavy punch flesh loud", 4)),
        ("block", Effects.Describe("metal shield clang", 5)),
        ("miss", Effects.Describe("soft small whoosh", 6)),
        ("fire", Effects.Roll("magic", 11)),
        ("frost", Effects.Describe("ice glass crystal", 7)),
        ("light", Effects.Roll("laser", 12)),
        ("blast", Effects.Roll("explosion", 3)),
        ("bigblast", Effects.Describe("heavy distant explosion", 1)),
        ("nova", Effects.Describe("ice crystal long echo", 2)),
        ("shield", Effects.Roll("powerup", 5)),
        ("warcry", Effects.Roll("roar", 2)),
        ("quake", Effects.Describe("huge stone impact echo", 3)),
        ("die", Effects.Describe("flesh heavy soft", 9)),
        ("bones", Effects.Describe("wood small rattle", 4)),
        ("hurt", Effects.Roll("hit", 4)),
        ("death", Effects.Describe("deep long bell distant", 1)),
        ("gold", Effects.Roll("coin", 3)),
        ("pickup", Effects.Roll("blip", 2)),
        ("magicdrop", Effects.Describe("small bell", 3)),
        ("raredrop", Effects.Describe("bell long", 6)),
        ("potion", Effects.Roll("water", 2)),
        ("level", Effects.Roll("fanfare", 2)),
        ("waystone", Effects.Describe("magic echo long", 4)),
        ("chest", Effects.Describe("heavy wooden door", 2)),
        ("stairs", Effects.Describe("stone step echo", 1)),
        ("quest", Effects.Roll("bell", 5)),
        ("nomana", Effects.Roll("deny", 1)),
        ("boss", Effects.Describe("huge roar echo", 7)),
        ("step", Effects.Describe("soft step on stone")),
        ("step2", Effects.Describe("soft step on stone", 2)),
        ("growl", Effects.Describe("small roar", 3)),
        ("groan", Effects.Describe("deep soft roar long", 4)),
        ("shriek", Effects.Describe("high roar echo", 5)),
        ("hiss", Effects.Describe("small quick whoosh", 7)),
        ("drip", Effects.Describe("small water drip echo", 1)),
        ("drip2", Effects.Describe("small water drip echo", 2)),
        ("crackle", Effects.Describe("small fire crackle", 1)),
        ("rumble", Effects.Describe("huge distant stone impact", 4)),
        ("wind", Effects.Describe("soft long distant whoosh", 3)),
        ("bark", Effects.Describe("small distant roar", 9)),
        ("win", Effects.Roll("fanfare", 5)),
    };

    public static void Start(Node root)
    {
        _bank = new SoundBank { Name = "Sounds" };
        root.AddChild(_bank);
        foreach (var (name, def) in All) _bank.Add(name, def);
        if (SoundBank.Silent) return;
        _music = new AudioStreamPlayer { Name = "Music" };
        root.AddChild(_music);
        Mood("sad");
    }

    public static void Play(string name, float volume = 0.7f, float pitchJitter = 0, int minGapMs = 40) => _bank?.Play(name, volume, 1, pitchJitter, minGapMs);

    /// <summary>The music for a mood (sad in the village, deep below, battle at the bottom), made the first time it is asked for.</summary>
    public static void Mood(string mood)
    {
        if (_music == null || mood == _mood) return;
        _mood = mood;
        if (!_songs.TryGetValue(mood, out AudioStream s))
            _songs[mood] = s = SoundBank.MusicStream(new MusicSpec { Mood = mood, Bars = 16, Seed = "emberdeep-" + mood });
        _music.Stream = s;
        _music.Play();
        ApplyMusic();
    }

    /// <summary>A pack's voice when it wakes, by kind.</summary>
    public static string Voice(string mon) => mon switch
    {
        "gnawer" or "wolf" or "hound" or "boar" => "growl",
        "sentry" or "archer" or "knight" => "bones",
        "shade" or "hag" or "lich" => "shriek",
        "imp" or "cultist" => "hiss",
        _ => "groan",
    };

    /// <summary>The sounds of a place now and then: (sound, shortest and longest gap in seconds, volume).</summary>
    public static (string sound, float min, float max, float volume)[] Ambience(string theme) => theme switch
    {
        "village" => new[] { ("wind", 5f, 12f, 0.35f), ("bark", 20f, 50f, 0.25f) },
        "barrow" or "catacomb" => new[] { ("drip", 2f, 6f, 0.3f), ("drip2", 3f, 9f, 0.25f), ("groan", 25f, 60f, 0.15f) },
        "ember" => new[] { ("crackle", 1.5f, 4f, 0.3f), ("rumble", 15f, 35f, 0.3f) },
        _ => new[] { ("crackle", 1.5f, 4f, 0.35f), ("rumble", 8f, 20f, 0.4f), ("drip", 4f, 9f, 0.2f) },
    };

    /// <summary>
    /// Write every sound and every music mood to WAV files in a folder (./play.sh --sounds), to listen to outside the
    /// game. Needs no audio device.
    /// </summary>
    public static void WriteAll(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        foreach (var (name, def) in All)
        {
            SoundBank.Stream(Effects.Render(def)).SaveToWav(System.IO.Path.Combine(dir, $"sfx-{name}.wav"));
            GD.Print($"  {dir}/sfx-{name}.wav");
        }
        foreach (string mood in new[] { "sad", "deep", "tense", "battle" })
        {
            SoundBank.MusicStream(new MusicSpec { Mood = mood, Bars = 16, Seed = "emberdeep-" + mood }).SaveToWav(System.IO.Path.Combine(dir, $"music-{mood}.wav"));
            GD.Print($"  {dir}/music-{mood}.wav");
        }
    }

    /// <summary>The music for a floor.</summary>
    public static string MoodFor(int depth) => depth == 0 ? "sad" : depth >= Data.Bottom ? "battle" : depth >= 7 ? "tense" : "deep";

    /// <summary>Music on or off, at the profile's volume.</summary>
    public static void ApplyMusic()
    {
        if (_music == null) return;
        _music.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, Profile.Current.MusicVolume));
        if (Profile.Current.MusicOn && !_music.Playing) _music.Play();
        if (!Profile.Current.MusicOn) _music.Stop();
    }
}
