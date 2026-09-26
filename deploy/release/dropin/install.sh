#!/usr/bin/env bash
# CS2 SoccerMod drop-in installer (Linux). Run it once after copying the
# release into your server folder, and again after every CS2 update or host
# panel update (they restore gameinfo.gi and remove the Metamod line).
#
#   bash install.sh                 # server folder = the folder of this script
#   bash install.sh /path/to/cs2    # or name it (the folder that contains game/)
#
# It only adds Metamod's search path to game/csgo/gameinfo.gi (backup first,
# nothing else is changed) and checks that the mod files are in place.
set -Eeuo pipefail

root=${1:-$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)}
gi=$root/game/csgo/gameinfo.gi
if [[ ! -f $gi ]]; then
    echo "gameinfo.gi not found at $gi" >&2
    echo "Copy the release into your CS2 server folder (the one that contains 'game') or pass that folder: bash install.sh /path/to/server" >&2
    exit 1
fi

missing=0
for f in \
    game/csgo/addons/metamod/bin/linuxsteamrt64/metamod.2.cs2.so \
    game/csgo/addons/counterstrikesharp/bin/linuxsteamrt64/counterstrikesharp.so \
    game/csgo/addons/multiaddonmanager/bin/multiaddonmanager.so \
    game/csgo/addons/soccermod_native/bin/linuxsteamrt64/soccermod_native.so \
    game/csgo/addons/counterstrikesharp/plugins/SoccerModNativeHull/SoccerModNativeHull.dll \
    game/csgo/cfg/multiaddonmanager/multiaddonmanager.cfg; do
    [[ -f $root/$f ]] || { echo "missing: $f" >&2; missing=1; }
done
((missing == 0)) || { echo "Copy the WHOLE release (the game folder included) into $root and run this again." >&2; exit 1; }

line_re='^[[:space:]]*Game[[:space:]]+csgo/addons/metamod[[:space:]]*(//.*)?'$'\r''?$'
if grep -qE "$line_re" "$gi"; then
    echo "gameinfo.gi: Metamod line already present."
else
    [[ -f $gi.soccermod.bak ]] || cp -p "$gi" "$gi.soccermod.bak"
    # Metamod's documented place: right after Game_LowViolence, else before
    # "Game csgo". Same indentation and line ending (CRLF kept) as that line.
    tmp=$(mktemp "$gi.XXXXXX")
    if ! awk '
        BEGIN { done = 0 }
        {
            line = $0; cr = ""
            if (sub(/\r$/, "", line)) cr = "\r"
            if (!done && line ~ /^[ \t]*Game_LowViolence([ \t]|$)/) {
                print $0
                match(line, /^[ \t]*/)
                printf "%sGame\tcsgo/addons/metamod%s\n", substr(line, 1, RLENGTH), cr
                done = 1
                next
            }
            print $0
        }
        END { if (!done) exit 3 }' "$gi" > "$tmp"; then
        awk '
            BEGIN { done = 0 }
            {
                line = $0; cr = ""
                if (sub(/\r$/, "", line)) cr = "\r"
                if (!done && line ~ /^[ \t]*Game[ \t]+csgo[ \t]*(\/\/.*)?$/) {
                    match(line, /^[ \t]*/)
                    printf "%sGame\tcsgo/addons/metamod%s\n", substr(line, 1, RLENGTH), cr
                    done = 1
                }
                print $0
            }
            END { if (!done) exit 3 }' "$gi" > "$tmp" || { rm -f "$tmp"; echo "gameinfo.gi layout not recognised; add 'Game csgo/addons/metamod' under SearchPaths by hand." >&2; exit 1; }
    fi
    chmod --reference="$gi" "$tmp" 2>/dev/null || true
    mv "$tmp" "$gi"
    echo "gameinfo.gi: Metamod line added (backup: gameinfo.gi.soccermod.bak)."
fi

echo
echo "CS2 SoccerMod is installed. Start the server with any map - SoccerMod"
echo "switches to the stadium by itself. Players need the Workshop item"
echo "3797479770 (the server tells them). Run this script again after every CS2 update."
