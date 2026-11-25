#!/usr/bin/env python3
"""EMBERDEEP -- bring the art in from assetgen's library (no server needed: it reads the files).

    python3 scripts/assets.py            copy every finished emberdeep asset and the shared clips
    python3 scripts/assets.py --list     say what would be copied, change nothing

Sources (ASSETGEN_LIBRARY, default ../assetgen/library):
  emberdeep/assets/body-*, held-*   -> assets/figures/<name>.glb     rigged bodies (humanoid or quadruped template),
                                                                     things held in their sockets
  emberdeep/assets/prop-*           -> assets/models/<name>.glb      props
  emberdeep/assets/drop-*, loot-*   -> assets/models/drop-<base>.glb an item lying on the floor (drop-pot-hp, drop-gold)
  emberdeep/assets/icon-*           -> assets/icons/<name>.png       item and skill icons, 128 px (with ffmpeg)
  blender-test/assets/<clip>        -> assets/figures/clip-*.glb     humanoid clips (on the shared template),
  hammerdeep/assets/<clip>                                           and four of hammerdeep's (chop, hammer, pray, smash)
                                       assets/figures/qclip-*.glb    quadruped clips
Each folder gets a manifest.txt (name, asset id): an exported build can read a file but not list a folder.
The latest version of each asset is taken; the files are committed, so the game never needs assetgen.
"""
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LIB = Path(os.environ.get("ASSETGEN_LIBRARY", ROOT.parent / "assetgen" / "library"))
PROJECT = "emberdeep"
ICON = 128

# The game's clip name -> the clip asset's name in the shared library (blender-test).
CLIPS = {
    "idle": "humanoid idle", "walk": "humanoid walk", "run": "humanoid run", "ready": "humanoid 1h ready",
    "slash": "humanoid 1h slash", "overhead": "humanoid 1h overhead", "punch": "humanoid punch", "kick": "humanoid kick",
    "hit": "humanoid hit react", "death": "humanoid death", "point": "humanoid point", "cheer": "humanoid cheer",
    "block": "humanoid block", "jump": "humanoid jump", "pickup": "humanoid pick up", "wave": "humanoid wave",
    "search": "humanoid search", "crouch": "humanoid crouch",
    # Made for a pistol: the right arm out, the left hand at the wrist, a kick on the shot -- a bow held out and loosed.
    "aim": "humanoid pistol aim", "shoot": "humanoid pistol shoot",
}
# Clips from hammerdeep's library (same humanoid template): a two-handed chop, a smith's hammering, kneeling prayer,
# a two-handed overhead blow (made for digging; a brute's or a golem's smash here).
HCLIPS = {"chop": "clip-chop", "hammer": "clip-hammer", "pray": "clip-pray", "smash": "dig"}
QCLIPS = {
    "idle": "quadruped idle", "walk": "quadruped walk", "run": "quadruped gallop", "bite": "quadruped bite",
    "pounce": "quadruped pounce", "death": "quadruped death", "howl": "quadruped howl",
}


def latest(asset_dir: Path, ext: str):
    files = sorted(asset_dir.glob(f"v*.{ext}"))
    return files[-1] if files else None


def assets_of(project: str):
    for d in sorted((LIB / project / "assets").iterdir()):
        meta = d / "asset.json"
        if not meta.exists():
            continue
        a = json.loads(meta.read_text())
        yield d, a


def main():
    dry = "--list" in sys.argv
    if not (LIB / PROJECT).is_dir():
        sys.exit(f"no assetgen library at {LIB / PROJECT} (set ASSETGEN_LIBRARY)")
    plan = {"figures": [], "models": [], "icons": []}  # (target name, asset id, source file)
    for d, a in assets_of(PROJECT):
        name = a["name"]
        if name.startswith(("body-", "held-", "beast-")):
            src = latest(d, "glb")
            plan["figures"].append(("body-" + name[6:] if name.startswith("beast-") else name, a["id"], src))
        elif name.startswith("prop-"):
            plan["models"].append((name[5:], a["id"], latest(d, "glb")))
        elif name.startswith(("drop-", "loot-")):
            # An item lying on the floor (drop-pot-hp, drop-gold...): the game shows it instead of the cut-out icon.
            plan["models"].append(("drop-" + name[5:], a["id"], latest(d, "glb")))
        elif name.startswith("icon-"):
            plan["icons"].append((name[5:], a["id"], latest(d, "png")))
    by_name = {a["name"]: (d, a) for d, a in assets_of("blender-test")}
    for d, a in assets_of("hammerdeep"):
        if a["name"] in HCLIPS.values() and latest(d, "glb") is not None:
            by_name[a["name"]] = (d, a)
    for prefix, table in (("clip-", CLIPS), ("qclip-", QCLIPS), ("clip-", HCLIPS)):
        for ours, theirs in table.items():
            if theirs not in by_name:
                print(f"  missing clip: {theirs}")
                continue
            d, a = by_name[theirs]
            plan["figures"].append((prefix + ours, a["id"], latest(d, "glb")))
    for folder, rows in plan.items():
        out = ROOT / "assets" / folder
        ext = "png" if folder == "icons" else "glb"
        done = [r for r in rows if r[2] is not None]
        for name, aid, src in rows:
            if src is None:
                print(f"  not finished yet: {aid}")
        print(f"{folder}: {len(done)}")
        if dry:
            for name, aid, src in done:
                print(f"  {name:28} {aid}  {src.name}")
            continue
        out.mkdir(parents=True, exist_ok=True)
        for name, aid, src in done:
            if folder == "icons" and shutil.which("ffmpeg"):
                # 512 px from the generator; the game shows them at most 128 px, at a sixth of the size on disk.
                subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(src), "-vf", f"scale={ICON}:{ICON}:flags=lanczos",
                                str(out / f"{name}.{ext}")], check=True)
            else:
                shutil.copyfile(src, out / f"{name}.{ext}")
        (out / "manifest.txt").write_text(
            f"# Emberdeep {folder}: name, assetgen asset id (scripts/assets.py copies the latest version of each).\n"
            + "".join(f"{name} {aid}\n" for name, aid, _ in sorted(done)))


if __name__ == "__main__":
    main()
