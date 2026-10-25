#!/usr/bin/env bash
# Builds the mod loader and the mods in this repo, and publishes them into the game.
#   Arcadius_Data/Managed: ArcadiusModLoader.dll + 0Harmony.dll (the game's own DLLs stay original)
#   Arcadius_Data: ScriptingAssemblies.json and RuntimeInitializeOnLoads.json register the loader
#   Mods/<name>: each mod; existing config.json files and enabled flags are kept
# Usage: install.sh [--game PATH] [MOD...]   (MOD = folder names under Mods/; default: all of them)
set -euo pipefail
source "$(dirname "$0")/game.sh"
here="$repo"
# dotnet needs a working directory that exists.
cd "$here"

mods=()
if [ ${#args[@]} -eq 0 ]; then
	for source in "$here"/Mods/*/; do
		mods+=("$(basename "$source")")
	done
else
	for name in "${args[@]}"; do
		[ -f "$here/Mods/$name/mod.json" ] || { echo "No mod $name in $here/Mods." >&2; exit 2; }
		mods+=("$name")
	done
fi

# Undo the old Cecil-patched build (legacy/OfflineMod): back to the game's original assembly.
if [ -f "$managed/Assembly-CSharp.dll.orig" ]; then
	cp -p "$managed/Assembly-CSharp.dll.orig" "$managed/Assembly-CSharp.dll"
	rm -f "$managed/Assembly-CSharp.dll.orig"
fi
rm -f "$managed/ArcadiusOffline.dll"

dotnet build "$here/ModLoader" -p:GameManaged="$managed" -v q -nologo
cp "$here/ModLoader/bin/Release/ArcadiusModLoader.dll" "$here/lib/0Harmony.dll" "$managed/"

for name in "${mods[@]}"; do
	for project in "$here/Mods/$name"/*.csproj; do
		[ -f "$project" ] && dotnet build "$project" -p:GameManaged="$managed" -v q -nologo
	done
done

python3 - "$data" <<'PY'
import json, os, shutil, sys
data = sys.argv[1]

def original(name):
    path = os.path.join(data, name)
    if not os.path.exists(path + ".orig"):
        shutil.copy2(path, path + ".orig")
    with open(path + ".orig") as f:
        return path, json.load(f)

# Register the loader as a user assembly (type 16), so Unity loads it and knows its MonoBehaviour.
path, scripting = original("ScriptingAssemblies.json")
scripting["names"].append("ArcadiusModLoader.dll")
scripting["types"].append(16)
with open(path, "w") as f:
    json.dump(scripting, f, separators=(",", ":"))

# Have Unity call the loader at startup (2 = AfterAssembliesLoaded, 1 = BeforeSceneLoad).
path, runtime = original("RuntimeInitializeOnLoads.json")
def entry(method, load_type):
    return {"assemblyName": "ArcadiusModLoader", "nameSpace": "ArcadiusModLoader", "className": "Bootstrap",
            "methodName": method, "loadTypes": load_type, "isUnityClass": False}
runtime["root"] = [entry("Init", 2), entry("CreateHost", 1)] + runtime["root"]
with open(path, "w") as f:
    json.dump(runtime, f, separators=(",", ":"))
PY

for name in "${mods[@]}"; do
	source="$here/Mods/$name"
	target="$game/Mods/$name"
	mkdir -p "$target"
	find "$source/bin/Release" -maxdepth 1 -name '*.dll' -exec cp {} "$target/" \; 2>/dev/null || true
	for folder in Text Textures Audio Bundles Model; do
		[ -d "$source/$folder" ] && cp -r "$source/$folder" "$target/"
	done
	# New manifest, but keep the user's enabled flag.
	python3 - "$source/mod.json" "$target/mod.json" <<'PY'
import json, os, sys
with open(sys.argv[1]) as f:
    manifest = json.load(f)
if os.path.exists(sys.argv[2]):
    with open(sys.argv[2]) as f:
        manifest["enabled"] = json.load(f).get("enabled", manifest.get("enabled", True))
with open(sys.argv[2], "w") as f:
    json.dump(manifest, f, indent=2)
    f.write("\n")
PY
done
rm -rf "$here"/ModLoader/{bin,obj} "$here"/Mods/*/{bin,obj}
echo "Installed the mod loader and ${#mods[@]} mod(s) (${mods[*]}) into $game/Mods."
if pgrep -f Arcadius.exe >/dev/null 2>&1; then
	echo "The game is running: restart it to load new code (F9 only reloads configs and assets)."
fi
