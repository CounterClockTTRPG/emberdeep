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
    }
}
