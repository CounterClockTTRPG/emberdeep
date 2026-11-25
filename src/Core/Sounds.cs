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
