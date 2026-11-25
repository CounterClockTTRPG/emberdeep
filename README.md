# Emberdeep

An original isometric action RPG in Godot 4.7 (.NET / C#), in the classic dungeon-crawler tradition with its own
names, lore and look. Below the village of Emberwick the old graves go down, and down, to a fire that should have
stayed asleep.

- **Three heroes:** the Wanderer (sword and shield), the Rogue (bow), the Sorcerer (fire, frost, lightning). Each
  has seven skills, two of them passive, learned with a point a level, up to rank 10.
- **Ten floors** generated from the seed: the Barrow, the Catacombs, the Ember Halls, and the Warden's Throne. Packs
  of monsters with champions, named quest foes, chests, a waystone on every floor.
- **Loot:** 28 item bases (weapons, armour, shields, rings, amulets), plain, magic and rare, with 30 affixes; draughts,
  waystone scrolls, gold; the smith Oskar buys and sells, the healer Maren mends you, the stash keeps your things.
- **Emberwick:** Elder Corwin's three quests (Mother Wort, Ember-Priest Kael, Vorgath), the waystone, the way down.
- **Rigged, animated 3D models** for every hero, monster and village folk, made with assetgen on two shared skeletons;
  the clips are retargeted to each body at run time.

```sh
./play.sh                          # play (Godot and dotnet paths in .env)
./play.sh --check                  # the self-check
./play.sh --check --only Balance   # a bot plays each class to Vorgath and reports
./play.sh --shots                  # the screenshot walkthrough into shots/
./play.sh --agent                  # remote control: scripts/agent.sh "new rogue; state"
python3 scripts/assets.py          # bring new art in from ../assetgen
bash scripts/export-android.sh     # an Android APK (build/android/Emberdeep.apk), installed if a phone is connected
```

Controls: left click to walk, fight, pick up, use and talk (hold to keep walking); right click for the chosen skill
(hold to repeat); Shift + left click to attack in place; Q W E R T choose the skill; 1-4 the belt; I C S the
inventory, character and skills; Tab the map; the wheel zooms; Esc closes panels. On a phone: tap to walk, fight,
pick up and use; tap the right-hand skill icon to make taps use that skill (tap it again to go back); the back button
closes panels.

See CLAUDE.md for the layout. The backlog is in Tasker, project EMB.

MIT licence.
