#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 1 ]]; then
  printf 'usage: %s <release.zip>\n' "$0" >&2
  exit 2
fi

archive="$(realpath "$1")"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

unzip -q "$archive" -d "$work/release"
release_root="$(find "$work/release" -mindepth 1 -maxdepth 1 -type d -print -quit)"
fake_game="$work/server/game/csgo"
plugin_dir="$fake_game/addons/counterstrikesharp/plugins/SoccerModNativeHull"

mkdir -p "$fake_game/addons/counterstrikesharp" "$fake_game/addons/metamod" "$plugin_dir"
touch "$fake_game/gameinfo.gi"
printf 'old-dll\n' > "$plugin_dir/SoccerModNativeHull.dll"
printf '{"Version":1,"Admins":[]}\n' > "$plugin_dir/soccermod_admins.json"

CS2_GAME_ROOT="$fake_game" bash "$release_root/install.sh"

cmp \
  "$release_root/game/csgo/addons/counterstrikesharp/plugins/SoccerModNativeHull/SoccerModNativeHull.dll" \
  "$plugin_dir/SoccerModNativeHull.dll"
grep -q '"Admins":\[\]' "$plugin_dir/soccermod_admins.json"
test -s "$fake_game/models/soccermod/ball_large_1850.vmdl_c"
test -s "$fake_game/panorama/images/overheadmaps/soccer_cssl_stadium_v8_radar_psd.vtex_c"
test -s "$fake_game/resource/overviews/soccer_cssl_stadium_v8.txt"
test "$(find "$fake_game/addons/soccermod-backups" -name SoccerModNativeHull.dll -type f | wc -l)" -eq 1

printf 'Release installer smoke test passed.\n'
