# godot-starter

A template for new Godot 4.7 C# games (`template/`), `bin/new-game.sh NAME` to make one, `bin/selftest.sh` to check
it end to end. See README.md.

## Commands

    bin/new-game.sh NAME [DIR]     a new game (renamed, godot-agent and sfxgen installed, first commit)
    bin/selftest.sh [DIR]          make a game in a scratch folder, build, self-check, walkthrough, drive it with the
                                   agent -- run after any change to template/ or to ../godot-agent or ../sfxgen

## Conventions

- `template/` is a working game named Starter: every name the new game takes is spelled Starter / STARTER /
  starter there (and nowhere else), and the repository's own name, godot-starter, is protected from the rename.
- The template has no addons of its own: new-game.sh installs them from the sibling repos, so they are never stale.
- Keep the template small and general: a feature belongs here only when every game wants it.
- The template's CLAUDE.md is the one each game starts with; keep it true.
