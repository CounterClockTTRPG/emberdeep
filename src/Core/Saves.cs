using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Emberdeep;

/// <summary>
/// Saves: any plain object as gzipped JSON (public fields included), written through a temporary file and a rename so a
/// crash mid-write never leaves half a save. Keep the saved types free of Godot objects.
/// </summary>
public static class Saves
{
    public static readonly JsonSerializerOptions Options = new() { IncludeFields = true, Converters = { new JsonStringEnumConverter() } };

    public static byte[] Write<T>(T value)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest)) gz.Write(json);
        return ms.ToArray();
    }

    public static T Read<T>(byte[] data)
    {
        using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var outp = new MemoryStream();
        gz.CopyTo(outp);
        return JsonSerializer.Deserialize<T>(outp.ToArray(), Options);
    }

    /// <summary>Writes to a user:// (or absolute) path; false when it could not.</summary>
    public static bool ToFile<T>(T value, string path)
    {
        string tmp = path + ".tmp";
        using (var f = Godot.FileAccess.Open(tmp, Godot.FileAccess.ModeFlags.Write))
        {
            if (f == null) { GD.PrintErr("cannot write " + tmp); return false; }
            f.StoreBuffer(Write(value));
        }
        return DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(tmp), ProjectSettings.GlobalizePath(path)) == Error.Ok;
    }

    /// <summary>Reads a save, or default when it is missing or unreadable (which is logged).</summary>
    public static T FromFile<T>(string path)
    {
        if (!Godot.FileAccess.FileExists(path)) return default;
        try { return Read<T>(Godot.FileAccess.GetFileAsBytes(path)); }
        catch (System.Exception e) { GD.PrintErr($"{path} unreadable: {e.Message}"); return default; }
    }
}
