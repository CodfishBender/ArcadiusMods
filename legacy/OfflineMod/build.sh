#!/usr/bin/env bash
# Rebuilds the mod from Arcadius_Data/Managed/Assembly-CSharp.dll.orig and installs it.
# Usage: build.sh [offline|capture]   (capture = online game that logs Cloud Code traffic)
set -euo pipefail
echo "Superseded by ModKit/install.sh (the offline mod is now a ModKit mod). Patching Assembly-CSharp again would apply every change twice." >&2
exit 1
mode="${1:-offline}"
here="$(cd "$(dirname "$0")" && pwd)"
managed="$here/../Arcadius_Data/Managed"
orig="$managed/Assembly-CSharp.dll.orig"
[ -f "$orig" ] || cp -p "$managed/Assembly-CSharp.dll" "$orig"
dotnet build -c Release "$here/ArcadiusOffline" -p:GameManaged="$managed" -v q -nologo
dotnet build -c Release "$here/Patcher" -v q -nologo
mod="$here/ArcadiusOffline/bin/Release/netstandard2.1/ArcadiusOffline.dll"
dotnet "$here/Patcher/bin/Release/net10.0/Patcher.dll" "$orig" "$mod" "$here/Assembly-CSharp.patched.dll" "$managed" "$mode"
cp "$mod" "$managed/ArcadiusOffline.dll"
cp "$here/Assembly-CSharp.patched.dll" "$managed/Assembly-CSharp.dll"
echo "Installed $mode build."
