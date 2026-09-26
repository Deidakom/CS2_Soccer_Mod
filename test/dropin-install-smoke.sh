#!/usr/bin/env bash
# Smoke test of the Linux drop-in ZIP: unpack it next to a fake CS2 server,
# run install.sh twice and check the gameinfo.gi line (added once, CRLF
# kept, backup made) and the checksums.
#
#   bash test/dropin-install-smoke.sh artifacts/releases/CS2-SoccerMod-1.5.0-linux.zip
set -Eeuo pipefail
zip=${1:?usage: $0 <linux drop-in zip>}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

python3 -c 'import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])' "$zip" "$work/unzipped"
release=$(find "$work/unzipped" -mindepth 1 -maxdepth 1 -type d)
(cd "$release" && sha256sum --quiet --check SHA256SUMS)

server=$work/server
mkdir -p "$server/game/csgo"
# A trimmed CS2 gameinfo.gi with CRLF line endings, like the real one.
printf '"GameInfo"\r\n{\r\n\tFileSystem\r\n\t{\r\n\t\tSearchPaths\r\n\t\t{\r\n\t\t\tGame_LowViolence\tcsgo_lv // Perfect World content override\r\n\t\t\tGame\tcsgo\r\n\t\t\tGame\tcsgo_imported\r\n\t\t\tGame\tcsgo_core\r\n\t\t}\r\n\t}\r\n}\r\n' > "$server/game/csgo/gameinfo.gi"
cp -r "$release/." "$server/"
# install.sh unpacks with its mode; a Windows-built ZIP would not keep it.
[[ -x $server/install.sh ]] || { echo "install.sh lost its executable bit" >&2; exit 1; }

"$server/install.sh" >/dev/null
"$server/install.sh" >/dev/null
gi=$server/game/csgo/gameinfo.gi
[[ $(grep -c 'csgo/addons/metamod' "$gi") == 1 ]] || { echo "Metamod line not added exactly once" >&2; exit 1; }
grep -q $'^\t\t\tGame\tcsgo/addons/metamod\r$' "$gi" || { echo "Metamod line has the wrong indent or line ending" >&2; exit 1; }
[[ $(grep -n 'csgo/addons/metamod' "$gi" | cut -d: -f1) == 8 ]] || { echo "Metamod line is not right after Game_LowViolence" >&2; exit 1; }
[[ -f $gi.soccermod.bak ]] || { echo "no gameinfo.gi backup" >&2; exit 1; }
echo "drop-in smoke test passed: $(basename "$zip")"
