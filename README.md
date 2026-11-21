# godot-starter

A template for a new Godot 4.7 C# game with the plumbing every game needs already done and tested, so the
first day goes on the game itself. Use it with an agent or by hand.

```sh
bin/new-game.sh Gemrun            # makes ../gemrun: renamed, building blocks installed, first commit made
cd ../gemrun
./play.sh --check                 # the self-check: passes from the first minute
./play.sh                         # play the sample
```

## What a new game gets

- **Run scripts that work from WSL** with Windows Godot and Windows `dotnet.exe` (UNC paths, CRLF stripped), or
  with Linux ones. Paths go in `.env`. Commands:
  - `./play.sh` plays the game.
  - `--build` builds in seconds through dotnet.
  - `--check [--only X]` runs the headless self-check.
  - `--shots` runs the screenshot walkthrough.
  - `--agent` starts remote control.
  - `--editor` opens the Godot editor.
  - `scripts/export.sh` makes a Windows build and runs the self-check against it.
- **Structure:**
  - `src/Sim` holds the rules as plain data with no Godot types, so they are quick to test and easy to save.
  - `src/Core` holds `Root` (the screen flow and command-line flags), `Profile` (settings and three save slots,
    with scratch files for tests and the agent's own slot), `Saves` (gzipped JSON, written atomically) and
    `Sounds`.
  - `src/UI` holds `UITheme` (a named palette, fonts from `assets/fonts`, a Theme and widget builders), plus a
    title screen and a game screen.
- **A self-check** (`tests/`): methods marked `[Section]` run in order, using `Check` and `Eq`. `--only X` runs
  some of them, a section that throws counts as failed, and the exit code is 1 on any failure.
- **[godot-agent](https://github.com/CounterClockTTRPG/godot-agent)** is wired in with `state` (the board in
  ASCII), map cells for `at`/`drag`, and game commands. Game time passes only while a command runs.
- **[sfxgen](https://github.com/CounterClockTTRPG/sfxgen)** makes every sound and the music at start-up from
  seeds and words. There are no audio files.
- **A screenshot walkthrough** with a fixed seed, ready for
  [shotkit](https://github.com/CounterClockTTRPG/shotkit) baselines (`shotkit check shots docs/baseline`).
- **A CLAUDE.md** with the commands, the layout and the conventions, for the agent that works on the game.
- **The sample game**, "gather the gems": a grid, walls, gems appearing over time, ten to win. It is small enough
  to read in a minute and replace in an hour.

## How the parts are tested

`bin/selftest.sh` checks the whole kit from start to finish:
1. Makes a game from the template in a scratch folder.
2. Builds it and runs its self-check.
3. Runs the walkthrough and checks the three screenshots exist.
4. Plays it through godot-agent: `ui`, a new game, `state`, keys, a wait (time passes only then), `at`, a
   screenshot, pressing a button by its label, `tree` and `perf`.

That tests godot-agent and sfxgen inside a real game as well. Run it after changing the template or either
building block.

## new-game.sh

`bin/new-game.sh NAME [DIR]`. The NAME must be a C# identifier starting with a capital: it becomes the namespace,
the assembly, the project name and the title. The script:
- copies `template/`;
- replaces `Starter`, `STARTER` and `starter` with the new name;
- installs godot-agent and sfxgen from sibling checkouts (`../godot-agent`, `../sfxgen`), or clones them from
  GitHub;
- runs `git init` and makes a first commit.

To update the building blocks in a game later, run `../godot-agent/bin/install.sh .` or
`../sfxgen/bin/install.sh .`.

## Licence

MIT. The sample game and template code are yours to change without credit.
