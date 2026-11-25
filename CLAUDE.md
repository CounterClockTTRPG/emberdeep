# Emberdeep

An original isometric action RPG in Godot 4.7 (.NET / C#), in the classic dungeon-crawler tradition with its own names,
lore and look: the village of Emberwick above, ten floors of the Emberdeep below (the Barrow, the Catacombs, the Ember
Halls), Vorgath the Ashen Warden at the bottom. Three classes (Wanderer, Rogue, Sorcerer) with seven skills each,
rolled loot (plain, magic, rare), waystones, quests from the elder. Made from godot-starter. Godot and dotnet paths in
`.env` (see `.env.example`); a Linux dotnet off PATH gets `DOTNET_ROOT` from `scripts/common.sh`.

## Commands

    ./play.sh                   play
    ./play.sh --build           compile only (dotnet, seconds)
    ./play.sh --check           headless self-check -- run after every change; --only X for the sections named like X
    ./play.sh --shots           screenshot walkthrough to shots/*.png -- look at them (fixed seed: the same build,
                                the same pictures; `shotkit check shots docs/baseline` shows what changed)
    ./play.sh --agent           remote control for an agent playtester; drive it with
                                scripts/agent.sh "state; key Right; shot board" ('help' lists the commands, game
                                commands included; 'quit' closes it). Game time passes only while a command runs.
                                Its own save slot (user://agent.sav).
    ./play.sh --reel DIR [--write-movie F.avi --fixed-fps 30 ...]
                                filmed by Reel (~/claude/reel): `--agent` with the agent folder DIR and the game
                                holding still between commands (no frame is drawn until one arrives, so Movie Maker
                                films no idle time; `--reel-live` turns that off). `time` replies the game time,
                                `wait MS` counts game time.
    ./play.sh --check --only Balance   the balance report: a bot plays each class from floor 1 to Vorgath (~10 s);
                                run it after changing any number in Data.cs, Hero.cs or Game.cs
    python3 scripts/assets.py   bring the art in from ../assetgen/library (project emberdeep, plus shared clips);
                                re-run when assetgen finishes more (--list shows what it would copy)
    bash scripts/export.sh [DIR]   Windows build to DIR (default C:/temp/Emberdeep), then the self-check against it.
    bash scripts/export-android.sh debug APK to build/android/Emberdeep.apk (arm64; installs with adb if a phone is
                                connected). The Android SDK, JDK and .NET 9 SDK are needed (net9.0 for Android only, see
                                Emberdeep.csproj); assets/.gdignore steps aside during the export and the art is packed
                                as-is ("keep" .import files, gitignored).
    python3 scripts/figures.py  the heroes' base bodies and outfit pieces, asked of the assetgen server (--list)

`--shots` and `--agent` run under `xvfb-run` when there is no display (software Vulkan: slow but fine).
On a touchscreen there is no right button: tapping the right-hand skill icon turns "skill taps" on (each tap uses that
skill) and off; Android's back button does what Esc does.

Settings live in `user://settings.json`, games in `user://slot1.sav` .. `slot3.sav`. `--check` and `--shots` use
scratch files and never touch them. Never kill Godot by image name (`taskkill /IM`): other games may be running
-- use the agent's `quit`.

## Layout

- `src/Sim` -- the rules as plain data, no Godot types; a save is `Game` as gzipped JSON.
  - `Data.cs` -- every table and number: floors, classes, skills (`SkillNumbers` says what each rank does), monsters,
    quest foes, item bases (one per icon), affixes, the XP curve, monster levels by depth.
  - `Items.cs` -- `Item` (base, rarity, rolled mods), `Loot` (rolling items, drops, the smith's stock).
  - `Hero.cs` -- `Hero` (attributes, equipment, pack, belt, stash, skills, quests) and `Stats` derived from it all.
  - `Floor.cs` -- a floor's layout from the seed and depth (never saved): rooms and corridors, props, spawns, the
    village, the throne; `Path` (A*), `Sight` (exact grid walk), `Distances`.
  - `Game.cs` -- actors, orders (move, attack, skill, pick, use, talk), monster AI, actions with a wind-up, every
    skill's effect, missiles and zones, damage, death, levels, floors and their state, the village's trade.
    Fixed ticks of 1/30 s; randomness from `R()` (seed + roll count), so games replay. `Events` tell the screen what
    happened (not saved).
- `src/View` -- `Kit` (models, clips and icons read from assets/ at run time; clips retargeted per body by bone name),
  `Figure3D` (a rigged body, held things in its sockets, clips), `World3D` (floors and walls built per chunk, the
  wall cut-away shader, props, lights, figures, drops, missiles, effects, the camera, picking).
- `src/UI` -- `GameScreen` (the 3D view in a SubViewport, the HUD drawn over it, mouse and keys into orders, events into
  sounds and words), `Panels.cs` (inventory, character, skills, shop, stash, talk, waystone, notice; `ItemSlot` with
  coloured tooltips), `TitleScreen` (slots, class choice), `UITheme`.
- `src/Core` -- `Root`, `Profile`, `Saves`, `Sounds` (sfxgen sounds; music by mood per floor).
- `src/Dev` -- `AgentHooks` (state with an ASCII map, and commands: new, go, attack, skill, pick, use, talk, trade,
  buy, sell, belt, equip, raise, learn, travel, descend, give, xp...), `ShotRunner` (the walkthrough).
- `tests` -- `GameChecks` (the rules), `ViewChecks` (every body loads and its clips move it), `BalanceCheck` (the bot).
- `assets/figures` (bodies, held things, clips), `assets/models` (props), `assets/icons` (128 px) -- from assetgen by
  `scripts/assets.py`, each with a manifest; `assets/fonts`.
- `addons/godot_agent`, `addons/sfxgen` -- building blocks; update with `../godot-agent/bin/install.sh .` and
  `../sfxgen/bin/install.sh .`, do not edit here.

## Conventions

- Rules go in `src/Sim` and get a self-check section; screens only draw and forward input.
- Advance the game only while `AgentServer.Running` (always true without an agent).
- Every new screen gets a step in `ShotRunner`, and anything an agent playtester needs gets an agent command.
- Randomness through `Game.R()` (or an `Rng` from the seed), so a game replays the same.
- Names, lore and look are Emberdeep's own: the genre's mechanics are fair game, another game's names and art are not.
- New art comes from assetgen (project `emberdeep`), on its humanoid or quadruped skeleton template, so the shared
  clips play on it; a body without a model yet shows as a capsule and still plays.

## Backlog: Tasker

This project's backlog lives in Tasker (http://localhost:7778), project key **EMB** (Emberdeep), not in markdown files.
Read `curl -s http://localhost:7778/api/guide` once per session before using it. At the start of a session check
`curl -s "http://localhost:7778/api/projects/EMB/items?sent_back=true"` for work the owner sent back. File new work as
items, move what you work on to `in_progress`, and to `in_review` with a comment when done; the owner accepts it.
Never delete, cancel instead.

