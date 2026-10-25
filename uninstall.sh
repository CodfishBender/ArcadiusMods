#!/usr/bin/env bash
# Removes the mod loader from the game, restoring its original files. The Mods folder (configs,
# dumps, logs) and saves are left alone.
# Usage: uninstall.sh [--game PATH]
set -euo pipefail
source "$(dirname "$0")/game.sh"
for file in ScriptingAssemblies.json RuntimeInitializeOnLoads.json; do
	[ -f "$data/$file.orig" ] && mv "$data/$file.orig" "$data/$file"
done
rm -f "$managed/ArcadiusModLoader.dll" "$managed/0Harmony.dll"
echo "Mod loader removed from $game; the game is back to its original files."
