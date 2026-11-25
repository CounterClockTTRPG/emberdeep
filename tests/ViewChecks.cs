using System.Linq;
using Godot;

namespace Emberdeep;

/// <summary>The art: every body loads with its clips, every prop and icon is there.</summary>
public partial class SelfCheck
{
    [Section]
    private void Figures()
    {
        Kit.Trouble.Clear();
        var bodies = Data.Monsters.Select(m => (m.Body, m.Height)).Concat(new[] { ("body-hero", 1.8), ("body-rogue", 1.8), ("body-sorcerer", 1.8), ("body-elder", 1.7), ("body-healer", 1.7), ("body-smith", 1.85) });
        foreach (var (body, h) in bodies)
        {
            var fig = new Figure3D(body, (float)h);
            AddChild(fig);
            Check(fig.Loaded, $"{body} loads");
            if (fig.Loaded)
            {
                string[] need = fig.Quad ? new[] { "idle", "walk", "run", "bite", "death" } : new[] { "idle", "walk", "run", "slash", "hit", "death", "point", "punch", "overhead" };
                foreach (string clip in need) Check(fig.Has(clip), $"{body} has the {clip} clip");
                // A clip must move the body: sample it part-way and compare with the rest pose.
                Skeleton3D skel = Kit.Find<Skeleton3D>(fig);
                foreach (string clip in need)
                {
                    fig.Play(clip);
                    var player = Kit.Find<AnimationPlayer>(fig);
                    player.Seek(player.CurrentAnimationLength * 0.4, true);
                    int moved = 0;
                    for (int b = 0; b < skel.GetBoneCount(); b++)
                        if (!skel.GetBonePoseRotation(b).IsEqualApprox(skel.GetBoneRest(b).Basis.GetRotationQuaternion())) moved++;
                    Check(moved > 3, $"{body}: the {clip} clip moves its bones ({moved})");
                }
            }
            fig.Free();
        }
        Check(Kit.Trouble.Count == 0, "clips: " + string.Join("; ", Kit.Trouble));
        foreach (string prop in new[] { "pillar", "brazier", "tomb", "barrel", "chest", "waystone", "house", "well", "tree" })
            Check(Kit.Has(prop, "models"), $"the {prop} model");
        foreach (string held in Data.Bases.Where(b => b.Held != null).Select(b => b.Held).Distinct())
            Check(Kit.Has("held-" + held), $"the held {held}");
        // Heroes on the base bodies (once assetgen has made them): what is worn shows, the skin under it is hidden.
        foreach (bool female in new[] { false, true })
        {
            string body = female ? "body-woman" : "body-man";
            if (!Kit.Has(body)) continue;
            Hero h = Hero.New(Cls.Wanderer, female);
            Check(Data.HeroBody(h) == body, $"a {(female ? "woman" : "man")} hero stands on {body}");
            var fig = new Figure3D(body, 1.8f);
            AddChild(fig);
            World3D.Dress(fig, h);
            var outfit = World3D.Outfit(h);
            Check(outfit.Any(p => p.slot == "torso") || !Kit.Has($"wear-{(female ? "f" : "m")}-quilted-armour"), "the quilted armour shows");
            if (outfit.Any(p => p.slot == "torso"))
                Check(fig.FindChildren("body_torso", "MeshInstance3D", true, false).Cast<MeshInstance3D>().All(m => !m.Visible), "and the skin under it is hidden");
            fig.Free();
        }
        // On the floor: weapons and shields as their models lying flat, the rest as icons with the background cut away.
        foreach (Base b in Data.Bases)
        {
            Node3D body = World3D.DropBody(new Item(b.Id));
            AddChild(body);
            Aabb box = Kit.Bounds(body);
            if (b.Held != null) Check(box.Size.Y < 0.5f && box.Size.Y > 0.005f && box.Position.Y > -0.02f, $"a dropped {b.Id} lies flat on the floor ({box.Size.Y:0.00} m high)");
            else
            {
                Texture2D t = Kit.Cutout(b.Icon);
                Image img = t?.GetImage();
                int edge = 0, clear = 0;
                if (img != null)
                    for (int k = 0; k < img.GetWidth(); k += 2)
                        foreach (Vector2I p in new[] { new Vector2I(k, 0), new Vector2I(k, img.GetHeight() - 1), new Vector2I(0, k), new Vector2I(img.GetWidth() - 1, k) })
                        { edge++; if (img.GetPixelv(p).A < 0.05f) clear++; }
                Check(edge > 0 && clear > edge * 0.6, $"the {b.Id} icon's background is cut away ({clear} of {edge} edge samples)");
                int kept = 0;
                if (img != null) for (int y = 0; y < img.GetHeight(); y += 4) for (int x = 0; x < img.GetWidth(); x += 4) if (img.GetPixel(x, y).A > 0.5f) kept++;
                Check(kept > 60, $"and the {b.Id} itself is kept ({kept} of 1024 samples)");
            }
            body.Free();
        }
    }
}
