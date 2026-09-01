#!/usr/bin/env bash
set -euo pipefail

release_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$release_root"

sha256sum --check SHA256SUMS

required=(
  "game/csgo/addons/counterstrikesharp/plugins/SoccerModNativeHull/SoccerModNativeHull.dll"
  "game/csgo/addons/soccermod_native/bin/linuxsteamrt64/soccermod_native.so"
  "game/csgo/addons/metamod/soccermod_native.vdf"
  "game/csgo/models/soccermod/ball_large_1850.vmdl_c"
  "game/csgo/maps/scripts/soccermod_classic_menu.vjs_c"
  "game/csgo/panorama/layout/custom_game/soccermod_classic_menu.vxml_c"
  "game/csgo/panorama/styles/custom_game/soccermod_classic_menu.vcss_c"
  "game/csgo/panorama/images/overheadmaps/soccer_cssl_stadium_v8_radar_psd.vtex_c"
  "game/csgo/panorama/images/map_icons/screenshots/1080p/soccer_cssl_stadium_v8_png.vtex_c"
  "game/csgo/resource/overviews/soccer_cssl_stadium_v8.txt"
)

for path in "${required[@]}"; do
  if [[ ! -s "$path" ]]; then
    printf 'Missing release file: %s\n' "$path" >&2
    exit 1
  fi
done

printf 'CS2 SoccerMod release package verified (%d runtime files).\n' "${#required[@]}"
