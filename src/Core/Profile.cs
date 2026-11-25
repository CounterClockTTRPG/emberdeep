using System;
using System.Text.Json;
using Godot;

namespace Emberdeep;

/// <summary>
/// Settings (<c>user://settings.json</c>) and three save slots (<c>user://slot1.sav</c> .. <c>slot3.sav</c>). The
/// self-check and the walkthrough use scratch files and never touch them; the agent plays in its own slot.
/// </summary>
public sealed class Profile
{
    /// <summary>Running the self-check or the walkthrough: scratch settings and slots.</summary>
    public static bool Scratch;
    /// <summary>Running under an agent: its own slot.</summary>
    public static bool Agent;
    public static Profile Current = new();
    public static int Slot = 1;
    public const int Slots = 3;

    public bool Fullscreen;
    /// <summary>Numbers rising from blows; every drop's name shown (not only magic and better).</summary>
    public bool DamageNumbers = true, AllNames;
    public bool Sound = true, MusicOn = true;
    public float Volume = 0.7f, MusicVolume = 0.5f;
    public int LastSlot = 1;

    private static string SettingsPath => Scratch ? "user://scratch_settings.json" : "user://settings.json";
    public static string SlotPath(int slot) => Agent ? "user://agent.sav" : Scratch ? $"user://scratch{slot}.sav" : $"user://slot{slot}.sav";

    public static void Load()
    {
        try
        {
            if (FileAccess.FileExists(SettingsPath))
                Current = JsonSerializer.Deserialize<Profile>(FileAccess.GetFileAsString(SettingsPath), Saves.Options) ?? new Profile();
        }
        catch (Exception e) { GD.PrintErr("settings unreadable, using defaults: " + e.Message); Current = new Profile(); }
        Slot = Math.Clamp(Current.LastSlot, 1, Slots);
    }

    public void Save()
    {
        using var f = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(this, Saves.Options));
    }

    /// <summary>Puts the settings into effect: the window and the audio buses.</summary>
    public void Apply()
    {
        if (DisplayServer.GetName() == "headless") return;
        DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        AudioServer.SetBusVolumeDb(0, Sound ? Mathf.LinearToDb(Mathf.Max(0.001f, Volume)) : -80);
    }

    public static bool SaveGame(Game g, int slot = 0) => Saves.ToFile(g, SlotPath(slot <= 0 ? Slot : slot));
    public static Game LoadGame(int slot) => Saves.FromFile<Game>(SlotPath(slot));

    /// <summary>A slot in one line for the title screen, or null when it is empty.</summary>
    public static string Peek(int slot)
    {
        Game g = LoadGame(slot);
        return g == null ? null : $"{g.Hero.Name}, level {g.Hero.Level} {g.Hero.Class} -- {g.PlaceName}{(g.Won ? " -- the Warden is slain" : "")}";
    }

    public static void DeleteSlot(int slot)
    {
        string path = SlotPath(slot);
        if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }
}
