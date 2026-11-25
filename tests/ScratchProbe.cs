using System.Linq;
using Godot;
namespace Emberdeep;
public partial class SelfCheck
{
    [Section(OnlyWhenAsked = true)]
    private void Probe()
    {
        var names = Data.Bases.Where(b => b.Held == null).Select(b => b.Icon).ToList();
        var sheet = Image.CreateEmpty(128 * 6, 128 * ((names.Count + 5) / 6), false, Image.Format.Rgba8);
        sheet.Fill(new Color(0.35f, 0.5f, 0.35f));
        for (int i = 0; i < names.Count; i++)
        {
            Image img = Kit.Cutout(names[i]).GetImage();
            img.Decompress(); img.ClearMipmaps();
            sheet.BlendRect(img, new Rect2I(0, 0, 128, 128), new Vector2I(i % 6 * 128, i / 6 * 128));
        }
        sheet.SavePng("/tmp/claude-1000/-home-raf-claude-emberdeep/50751e74-fcb2-47b1-ba6b-9910ef16b686/scratchpad/cutouts.png");
        GD.Print("PROBE " + string.Join(",", names));
    }
}
