# Sourced by install.sh and uninstall.sh: finds the game folder and sets $game, $data and $managed.
# The game is, in order: --game PATH (remembered in game.path), $ARCADIUS_GAME, game.path, ../Arcadius.
# Leaves the remaining arguments in $args.
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
game=""
args=()
while [ $# -gt 0 ]; do
	case "$1" in
		--game) [ $# -ge 2 ] || { echo "--game needs a path" >&2; exit 2; }
			game="$2"; shift 2
			[ -d "$game" ] && printf '%s\n' "$(cd "$game" && pwd)" > "$repo/game.path" ;;
		--game=*) game="${1#--game=}"; shift
			[ -d "$game" ] && printf '%s\n' "$(cd "$game" && pwd)" > "$repo/game.path" ;;
		*) args+=("$1"); shift ;;
	esac
done
[ -z "$game" ] && game="${ARCADIUS_GAME:-}"
[ -z "$game" ] && [ -f "$repo/game.path" ] && game="$(head -n 1 "$repo/game.path")"
[ -z "$game" ] && game="$repo/../Arcadius"
if [ ! -f "$game/Arcadius_Data/Managed/Assembly-CSharp.dll" ]; then
	echo "No Arcadius install at $game (expected Arcadius_Data/Managed/Assembly-CSharp.dll)." >&2
	echo "Pass --game /path/to/Arcadius or set ARCADIUS_GAME." >&2
	exit 1
fi
game="$(cd "$game" && pwd)"
data="$game/Arcadius_Data"
managed="$data/Managed"
