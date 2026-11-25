#!/usr/bin/env python3
"""EMBERDEEP -- the heroes' bodies and the pieces they wear, asked of assetgen's Blender agent.

    python3 scripts/figures.py           ask for what is missing (bodies first; a body's pieces once it is made)
    python3 scripts/figures.py --list    say what is made, under way, or missing; change nothing
    python3 scripts/figures.py --done    exit 0 only when every body and piece is made (for a waiting loop)

Two base bodies on the humanoid skeleton template, a man and a woman, in plain underclothes (role=base), and for
each the outfit pieces fitted to it (kind=outfit): the armour that can be worn (torso, head, hands, feet, by item
base) and each class's own pieces (cloaks, the rogue's hood, the sorcerer's robe). Every piece is skinned to the
template, so the game moves it onto the body's skeleton by bone name. Asking returns at once; a run takes minutes.
Made assets land in ../assetgen/library/emberdeep: `python3 scripts/assets.py` then copies them into assets/figures
(names wear-<m|f>-<piece>, body-man, body-woman).

The server: ASSETGEN_URL (default http://192.168.86.29:8765), project emberdeep.
"""
import json
import os
import sys
import urllib.request

URL = os.environ.get("ASSETGEN_URL", "http://192.168.86.29:8765").rstrip("/")
PROJECT = "emberdeep"

BODIES = {
    "body-man": "a human man about 1.8 m tall, lean and strong, a weathered face with a strong jaw, plain undyed linen "
                "underclothes: a sleeveless undershirt and knee-length breeches, bare arms, barefoot. A base body for armour "
                "and clothes to be fitted over",
    "body-woman": "a human woman about 1.72 m tall, lean and strong, a weathered face with high cheekbones, plain undyed "
                  "linen underclothes: a sleeveless undershirt and knee-length breeches, bare arms, barefoot. A base body "
                  "for armour and clothes to be fitted over",
}

# piece -> (slot, set, what it is). The game maps item bases and classes to these names (World3D.Dress).
PIECES = {
    "quilted-armour": ("torso", "quilted", "a padded quilted cloth gambeson in faded crimson-brown, reaching the hips, long sleeves"),
    "leather-armour": ("torso", "leather", "a dark brown boiled-leather cuirass over a short-sleeved tunic, riveted shoulder pieces"),
    "chain-hauberk": ("torso", "chain", "a grey chainmail hauberk to mid-thigh with short sleeves, a dark crimson tabard over it, a broad belt"),
    "plate-armour": ("torso", "plate", "a blackened steel plate breastplate and backplate with layered pauldrons over a mail shirt, a crimson sash"),
    "leather-cap": ("head", "leather", "a simple brown leather cap fitted to the skull" + "{FACE}"),
    "iron-helm": ("head", "iron", "an iron half-helm with a nose guard, dented" + "{FACE}"),
    "great-helm": ("head", "plate", "a closed steel great helm with eye slits and breathing holes, covering the whole head"),
    "leather-gloves": ("arms", "leather", "worn brown leather gloves with laced leather bracers on the forearms"),
    "chain-gloves": ("arms", "chain", "chainmail gloves and mail sleeves on the forearms"),
    "gauntlets": ("arms", "plate", "articulated steel plate gauntlets and vambraces on the forearms"),
    "leather-boots": ("feet", "leather", "worn brown leather boots to mid-calf"),
    "heavy-boots": ("feet", "iron", "heavy dark leather boots shod with iron plates at the toe and shin"),
    "greaves": ("feet", "plate", "steel sabatons and armoured boot cuffs reaching just above the ankle"),
    "trousers": ("legs", "cloth", "dark charcoal wool trousers, tucked in at the ankle"),
    "wanderer-cloak": ("back", "wanderer", "a tattered charcoal travelling cloak hanging from the shoulders to the calves"),
    "rogue-hood": ("head", "rogue", "a deep charcoal hood with a scarf drawn over the lower face, the eyes in shadow" + "{FACE}"),
    "hair": ("head", "hair", "{HAIR}" + "{FACE}"),
    "rogue-cape": ("back", "rogue", "a short dark charcoal cape to the waist with crimson trim, a quiver of arrows strapped on the back"),
    "sorcerer-robe": ("legs", "sorcerer", "the long skirts of a deep crimson and charcoal robe falling to the ankles, ember-orange runes embroidered at the hem, a rope belt with charms"),
    "sorcerer-mantle": ("back", "sorcerer", "a high-collared charcoal mantle over the shoulders with a hood down on the back, ember-orange embroidered runes"),
}
STYLE_NOTE = "stylised low-poly, fitted closely to the body, nothing on the hands but what is described"
# A head piece hides the whole head region, face included: an open one must bring the body's own face with it.
FACE = ("; this piece replaces the whole head region, so first copy the base body's body_head mesh (skull, face and neck, "
        "with its skin weights) into the piece unchanged, then build the rest on and around it, leaving the face as it is")
HAIR = {"m": "short cropped dark hair and a short dark beard", "f": "dark hair pulled back tight into a low bun at the nape"}


def call(path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(URL + path, data=data, method="POST" if data else "GET", headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.loads(r.read())


def made():
    """name -> asset id, for every asset of the project that has a finished version."""
    out = {}
    for a in call(f"/api/projects/{PROJECT}/assets").get("assets", []):
        if a.get("current"):
            out[a["name"]] = a["id"]
    return out


def under_way():
    jobs = call("/api/jobs?status=queued,running")
    jobs = jobs.get("jobs", jobs) if isinstance(jobs, dict) else jobs
    return [j for j in jobs if j.get("project") == PROJECT]


def wanted():
    """Every name, with its request (a piece's request needs its body's id, so it is None until the body is made)."""
    have = made()
    out = {}
    for name, prompt in BODIES.items():
        out[name] = {"kind": "model3d", "model": "blender", "name": name,
                     "params": {"triangles": 4000, "rounds": 4, "role": "base"},
                     "prompt": f"{prompt}, standing in the skeleton template's rest pose"}
    for body, tag in (("body-man", "m"), ("body-woman", "f")):
        for piece, (slot, set_, what) in PIECES.items():
            name = f"wear-{tag}-{piece}"
            base = have.get(body)
            out[name] = None if base is None else {
                "kind": "outfit", "model": "blender-outfit", "name": name,
                "params": {"base": base, "slot": slot, "set": set_},
                "prompt": what.replace("{HAIR}", HAIR[tag]).replace("{FACE}", FACE) + f", {STYLE_NOTE}"}
    return out


def main(argv):
    mode = argv[0] if argv else ""
    have = made()
    jobs = under_way()
    labels = " | ".join(j.get("label") or "" for j in jobs)
    want = wanted()
    missing = []
    for name, req in want.items():
        if name in have:
            state = "made"
        elif req is not None and req["prompt"][:70] in labels:
            state = "under way"
        elif req is None:
            state = "waits for its body"
        else:
            state = "missing"
        if state != "made":
            missing.append(name)
        if mode == "--list":
            print(f"  {state:20} {name}")
            continue
        if state == "missing" and mode != "--done":
            r = call(f"/api/projects/{PROJECT}/generate", req)
            job = r.get("job") or {}
            print(f"  asked {name:30} job {job.get('id')} {r.get('error') or ''}")
            # The bodies first: every piece waits for one.
            if name.startswith("body-") and job.get("id"):
                try:
                    call(f"/api/jobs/{job['id']}/front", {})
                except Exception as e:  # moving it up is a nicety
                    print(f"    (could not move it to the front: {e})")
    if mode == "--done":
        print(f"{len(want) - len(missing)} of {len(want)} made")
        return 0 if not missing else 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
