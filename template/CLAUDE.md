# Starter

A Godot 4.7 (.NET / C#) game, made from godot-starter. Windows Godot and `dotnet.exe` driven from WSL (or Linux
ones); paths in `.env` (see `.env.example`).

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
    bash scripts/export.sh [DIR]   Windows build to DIR (default C:/temp/Starter), then the self-check against it.

Settings live in `user://settings.json`, games in `user://slot1.sav` .. `slot3.sav`. `--check` and `--shots` use
scratch files and never touch them. Never kill Godot by image name (`taskkill /IM`): other games may be running
-- use the agent's `quit`.

## Layout

- `src/Sim` -- the rules as plain data, no Godot types (`Game`: the whole game; a save is it as gzipped JSON).
- `src/Core` -- `Root` (screens, command-line flags), `Profile` (settings, slots), `Saves` (gzipped JSON, atomic),
  `Sounds` (every sound and the music, from sfxgen seeds and words).
- `src/UI` -- `UITheme` (palette, fonts, theme, builders; `Anchor` a control *after* adding it), the screens.
- `src/Dev` -- `AgentHooks` (the game's side of godot-agent: `state`, cells, commands), `ShotRunner` (the walkthrough).
- `tests` -- the self-check: methods marked `[Section]` in `partial class SelfCheck`, with `Check` / `Eq`.
- `addons/godot_agent`, `addons/sfxgen` -- building blocks; update with `../godot-agent/bin/install.sh .` and
  `../sfxgen/bin/install.sh .`, do not edit here.
- `assets/` -- read as bytes at run time (`assets/.gdignore` keeps Godot's importer out); fonts in `assets/fonts`.

## Conventions

- Rules go in `src/Sim` and get a self-check section; screens only draw and forward input.
- Advance the game only while `AgentServer.Running` (always true without an agent).
- Every new screen gets a step in `ShotRunner`, and anything an agent playtester needs gets an agent command.
- Randomness through `Rng` from the game's seed, so a game replays the same.
