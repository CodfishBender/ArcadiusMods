# Arcadius mods

Source for the Arcadius mod loader and mods. The game itself (Unity 6000.4, Mono, Windows build run under
Proton on Linux) is a separate folder, by default `../Arcadius`; never edit mod source there.

- `README.md`: the loader API, how to inspect game assets, the player character's skeleton, model mods
  without Unity. Read it first.
- `ModLoader/`: the loader. `Mods/<name>/`: each mod (csproj, `mod.json`, data folders).
- `tools/dump_prefab.py`: prints a prefab's hierarchy from the game's asset files.
- `legacy/OfflineMod/`: the old Cecil-patching approach, superseded by the `Offline` mod.
- Decompiled game code: `~/.cache/rea-work/arcadius/src/acs`.

## Publishing to the game

`./install.sh [--game PATH] [MOD...]` builds the loader and the named mods (default: all) and publishes
them into `<game>/Mods`, keeping config.json files and enabled flags. It copies only DLLs and the `Text`,
`Textures`, `Audio`, `Bundles` and `Model` folders. The game path comes from `--game` (remembered in
`game.path`), `$ARCADIUS_GAME`, `game.path`, then `../Arcadius`. `./uninstall.sh` removes the loader.

In the game folder, `Mods/` is install output plus the user's configs; `Mods/modlog.txt` is the log.

## Testing

- You can't run, see or screenshot the game, so the user tests every change in game. Check what you can
  beforehand: the build, data files (e.g. mesh winding and UVs with a script), and game data with
  `tools/dump_prefab.py`.
- Installing while the game runs works, but code changes need a game restart; F9 only reloads configs and
  assets. install.sh says so when the game is running; tell the user which one they need.
- Read `<game>/Mods/modlog.txt` when something doesn't work in game.

## Tools on this machine

`dotnet`, `blender` (use `-b --python script.py -- args`) and `python3`. No Unity editor, assimp or
trimesh. UnityPy isn't installed system-wide: use a venv (see README) and run scripts with `python -I`.
