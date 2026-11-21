using Godot;
using SfxGen;

namespace Starter;

/// <summary>
/// Every sound and the music, made by sfxgen at start-up from seeds and words (no audio files). Calls are safe with no
/// audio device (headless) and before the bank exists.
/// </summary>
public static class Sounds
{
    private static SoundBank _bank;
    private static AudioStreamPlayer _music;

    /// <summary>The game's sounds: a name, and how sfxgen makes it. Tune them by changing the seed or the words.</summary>
    public static readonly (string name, SfxDef def)[] All =
    {
        ("click", Effects.Roll("click", 2)),
        ("step", Effects.Describe("soft step on stone")),
        ("bump", Effects.Roll("deny", 1)),
        ("gem", Effects.Roll("coin", 3)),
        ("win", Effects.Roll("fanfare", 2)),
    };

    public static void Start(Node root)
    {
        _bank = new SoundBank { Name = "Sounds" };
        root.AddChild(_bank);
        foreach (var (name, def) in All) _bank.Add(name, def);
        if (SoundBank.Silent) return;
        _music = new AudioStreamPlayer { Name = "Music", Stream = SoundBank.MusicStream(new MusicSpec { Mood = "calm", Bars = 16, Seed = "starter" }) };
        root.AddChild(_music);
        ApplyMusic();
    }

    public static void Play(string name, float volume = 0.7f, float pitchJitter = 0, int minGapMs = 40) => _bank?.Play(name, volume, 1, pitchJitter, minGapMs);

    /// <summary>Music on or off, at the profile's volume.</summary>
    public static void ApplyMusic()
    {
        if (_music == null) return;
        _music.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, Profile.Current.MusicVolume));
        if (Profile.Current.MusicOn && !_music.Playing) _music.Play();
        if (!Profile.Current.MusicOn) _music.Stop();
    }
}
