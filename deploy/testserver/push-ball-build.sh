#!/usr/bin/env bash
# Pushes the current ball model, plugin build, compiled classic-menu HUD, and
# stadium radar resources to the test server. It preserves the previous live
# files and the configured menu mode, restarts once, and reports what actually
# came up. One SSH connection, so one password prompt.
#
#   bash deploy/testserver/push-ball-build.sh
#
# Override the host with SOCCERMOD_HOST=user@host if needed.
#
# The payload is base64-embedded in the remote script rather than piped as a
# separate tar stream: `ssh host 'bash -s' <<EOF` already claims stdin for the
# script, so a piped archive would be silently discarded.
set -euo pipefail

HOST="${SOCCERMOD_HOST:-root@212.87.212.58}"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CS2_GAME="${CS2_GAME:-E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game}"

MODEL="$CS2_GAME/csgo_addons/soccermod_phase1/models/soccermod/ball_large_1850.vmdl_c"
DLL="$REPO/src/server-plugin/SoccerModMvp/bin/Release/net10.0/SoccerModNativeHull.dll"
CLASSIC_ADDON="$CS2_GAME/csgo_addons/soccermod_classic_ui"
CLASSIC_SCRIPT="$CLASSIC_ADDON/scripts/vscripts/soccermod_classic_menu.vjs_c"
CLASSIC_LAYOUT="$CLASSIC_ADDON/panorama/layout/custom_game/soccermod_classic_menu.vxml_c"
CLASSIC_STYLE="$CLASSIC_ADDON/panorama/styles/custom_game/soccermod_classic_menu.vcss_c"
RADAR_ADDON="$CS2_GAME/csgo_addons/soccermod_stadium_radar"
RADAR_TEXTURE="$RADAR_ADDON/panorama/images/overheadmaps/soccer_cssl_stadium_v8_radar_psd.vtex_c"
RADAR_LOADING="$RADAR_ADDON/panorama/images/map_icons/screenshots/1080p/soccer_cssl_stadium_v8_png.vtex_c"
RADAR_OVERVIEW="$RADAR_ADDON/resource/overviews/soccer_cssl_stadium_v8.txt"

for f in "$MODEL" "$DLL" "$CLASSIC_SCRIPT" "$CLASSIC_LAYOUT" "$CLASSIC_STYLE" \
    "$RADAR_TEXTURE" "$RADAR_LOADING" "$RADAR_OVERVIEW"; do
    if [ ! -f "$f" ]; then
        echo "missing: $f" >&2
        exit 1
    fi
done

echo "model : $MODEL ($(wc -c < "$MODEL") bytes)"
echo "plugin: $DLL ($(wc -c < "$DLL") bytes)"
echo "classic script: $CLASSIC_SCRIPT ($(wc -c < "$CLASSIC_SCRIPT") bytes)"
echo "classic layout: $CLASSIC_LAYOUT ($(wc -c < "$CLASSIC_LAYOUT") bytes)"
echo "classic style : $CLASSIC_STYLE ($(wc -c < "$CLASSIC_STYLE") bytes)"
echo "radar texture : $RADAR_TEXTURE ($(wc -c < "$RADAR_TEXTURE") bytes)"
echo "loading image : $RADAR_LOADING ($(wc -c < "$RADAR_LOADING") bytes)"
echo "overview data : $RADAR_OVERVIEW ($(wc -c < "$RADAR_OVERVIEW") bytes)"
echo "host  : $HOST"
echo "Enter the root password when prompted; nothing echoes while you type."
echo

{
    cat <<'HEADER'
set -euo pipefail
ROOT=/home/gameserver/cs2/game/csgo
MODELS="$ROOT/models/soccermod"
PLUGIN="$ROOT/addons/counterstrikesharp/plugins/SoccerModNativeHull"
CLASSIC_SCRIPT="$ROOT/scripts/vscripts/soccermod_classic_menu.vjs_c"
CLASSIC_LAYOUT="$ROOT/panorama/layout/custom_game/soccermod_classic_menu.vxml_c"
CLASSIC_STYLE="$ROOT/panorama/styles/custom_game/soccermod_classic_menu.vcss_c"
RADAR_TEXTURE="$ROOT/panorama/images/overheadmaps/soccer_cssl_stadium_v8_radar_psd.vtex_c"
RADAR_LOADING="$ROOT/panorama/images/map_icons/screenshots/1080p/soccer_cssl_stadium_v8_png.vtex_c"
RADAR_OVERVIEW="$ROOT/resource/overviews/soccer_cssl_stadium_v8.txt"
STAMP=$(date +%Y%m%d-%H%M%S)
BACKUP=/home/gameserver/cs2/backups/ball-$STAMP
mkdir -p "$BACKUP"

# Keep whatever is live so a rollback is one copy, not a rebuild.
cp -a "$MODELS" "$BACKUP/models-soccermod" 2>/dev/null || true
cp -a "$PLUGIN" "$BACKUP/plugin-SoccerModNativeHull" 2>/dev/null || true
for RESOURCE in "$CLASSIC_SCRIPT" "$CLASSIC_LAYOUT" "$CLASSIC_STYLE" \
    "$RADAR_TEXTURE" "$RADAR_LOADING" "$RADAR_OVERVIEW"; do
    if [ -f "$RESOURCE" ]; then
        mkdir -p "$BACKUP/$(dirname "${RESOURCE#$ROOT/}")"
        cp -a "$RESOURCE" "$BACKUP/${RESOURCE#$ROOT/}"
    fi
done
echo "backup: $BACKUP"

STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
HEADER

    printf "base64 -d > \"\$STAGE/ball_large_1850.vmdl_c\" <<'B64_MODEL'\n"
    base64 "$MODEL"
    printf "B64_MODEL\n"

    printf "base64 -d > \"\$STAGE/SoccerModNativeHull.dll\" <<'B64_DLL'\n"
    base64 "$DLL"
    printf "B64_DLL\n"

    printf "base64 -d > \"\$STAGE/soccermod_classic_menu.vjs_c\" <<'B64_CLASSIC_SCRIPT'\n"
    base64 "$CLASSIC_SCRIPT"
    printf "B64_CLASSIC_SCRIPT\n"

    printf "base64 -d > \"\$STAGE/soccermod_classic_menu.vxml_c\" <<'B64_CLASSIC_LAYOUT'\n"
    base64 "$CLASSIC_LAYOUT"
    printf "B64_CLASSIC_LAYOUT\n"

    printf "base64 -d > \"\$STAGE/soccermod_classic_menu.vcss_c\" <<'B64_CLASSIC_STYLE'\n"
    base64 "$CLASSIC_STYLE"
    printf "B64_CLASSIC_STYLE\n"

    printf "base64 -d > \"\$STAGE/soccer_cssl_stadium_v8_radar_psd.vtex_c\" <<'B64_RADAR_TEXTURE'\n"
    base64 "$RADAR_TEXTURE"
    printf "B64_RADAR_TEXTURE\n"

    printf "base64 -d > \"\$STAGE/soccer_cssl_stadium_v8_png.vtex_c\" <<'B64_RADAR_LOADING'\n"
    base64 "$RADAR_LOADING"
    printf "B64_RADAR_LOADING\n"

    printf "base64 -d > \"\$STAGE/soccer_cssl_stadium_v8.txt\" <<'B64_RADAR_OVERVIEW'\n"
    base64 "$RADAR_OVERVIEW"
    printf "B64_RADAR_OVERVIEW\n"

    cat <<'FOOTER'
echo "staged:"
ls -l "$STAGE"

install -D -m 0644 "$STAGE/ball_large_1850.vmdl_c" "$MODELS/ball_large_1850.vmdl_c"
install -D -m 0644 "$STAGE/SoccerModNativeHull.dll" "$PLUGIN/SoccerModNativeHull.dll"
install -D -m 0644 "$STAGE/soccermod_classic_menu.vjs_c" "$CLASSIC_SCRIPT"
install -D -m 0644 "$STAGE/soccermod_classic_menu.vxml_c" "$CLASSIC_LAYOUT"
install -D -m 0644 "$STAGE/soccermod_classic_menu.vcss_c" "$CLASSIC_STYLE"
install -D -m 0644 "$STAGE/soccer_cssl_stadium_v8_radar_psd.vtex_c" "$RADAR_TEXTURE"
install -D -m 0644 "$STAGE/soccer_cssl_stadium_v8_png.vtex_c" "$RADAR_LOADING"
install -D -m 0644 "$STAGE/soccer_cssl_stadium_v8.txt" "$RADAR_OVERVIEW"

chown -R gameserver:gameserver "$MODELS" "$PLUGIN" \
    "$(dirname "$CLASSIC_SCRIPT")" \
    "$(dirname "$CLASSIC_LAYOUT")" \
    "$(dirname "$CLASSIC_STYLE")"
chown gameserver:gameserver "$RADAR_TEXTURE" "$RADAR_LOADING" "$RADAR_OVERVIEW"

echo "installed:"
ls -l "$MODELS/ball_large_1850.vmdl_c" "$PLUGIN/SoccerModNativeHull.dll" \
    "$CLASSIC_SCRIPT" "$CLASSIC_LAYOUT" "$CLASSIC_STYLE" \
    "$RADAR_TEXTURE" "$RADAR_LOADING" "$RADAR_OVERVIEW"

systemctl restart cs2-soccermod-test.service
sleep 25
echo "service: $(systemctl is-active cs2-soccermod-test.service)"
echo "--- plugin lines ---"
journalctl -u cs2-soccermod-test.service --since '2 min ago' --no-pager \
    | grep -iE "SoccerMod Ball|Native XSL Hull|alpha1|plugin|clean_ball_activated|classic_menu|radar|overview|Exception|error" \
    | tail -50
FOOTER
} | ssh "$HOST" 'bash -s'

echo
echo "done. Next: run the reference trials over RCON, then collect them with"
echo "  ssh $HOST \"journalctl -u cs2-soccermod-test.service --since '10 min ago' | grep SM2CSSREF\" > css-vs-cs2.log"
