#!/usr/bin/env bash
# Pushes the current ball model and plugin build to the test server, restarts
# the service and reports what actually came up.  One SSH connection, so one
# password prompt.
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

for f in "$MODEL" "$DLL"; do
    if [ ! -f "$f" ]; then
        echo "missing: $f" >&2
        exit 1
    fi
done

echo "model : $MODEL ($(wc -c < "$MODEL") bytes)"
echo "plugin: $DLL ($(wc -c < "$DLL") bytes)"
echo "host  : $HOST"
echo "Enter the root password when prompted; nothing echoes while you type."
echo

{
    cat <<'HEADER'
set -euo pipefail
ROOT=/home/gameserver/cs2/game/csgo
MODELS="$ROOT/models/soccermod"
PLUGIN="$ROOT/addons/counterstrikesharp/plugins/SoccerModNativeHull"
STAMP=$(date +%Y%m%d-%H%M%S)
BACKUP=/home/gameserver/cs2/backups/ball-$STAMP
mkdir -p "$BACKUP"

# Keep whatever is live so a rollback is one copy, not a rebuild.
cp -a "$MODELS" "$BACKUP/models-soccermod" 2>/dev/null || true
cp -a "$PLUGIN" "$BACKUP/plugin-SoccerModNativeHull" 2>/dev/null || true
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

    cat <<'FOOTER'
echo "staged:"
ls -l "$STAGE"

install -D -m 0644 "$STAGE/ball_large_1850.vmdl_c" "$MODELS/ball_large_1850.vmdl_c"
install -D -m 0644 "$STAGE/SoccerModNativeHull.dll" "$PLUGIN/SoccerModNativeHull.dll"
chown -R gameserver:gameserver "$MODELS" "$PLUGIN"

echo "installed:"
ls -l "$MODELS/ball_large_1850.vmdl_c" "$PLUGIN/SoccerModNativeHull.dll"

systemctl restart cs2-soccermod-test.service
sleep 25
echo "service: $(systemctl is-active cs2-soccermod-test.service)"
echo "--- plugin lines ---"
journalctl -u cs2-soccermod-test.service --since '2 min ago' --no-pager \
    | grep -iE "SoccerMod Ball|Native XSL Hull|alpha1|plugin|clean_ball_activated|Exception|error" \
    | tail -30
FOOTER
} | ssh "$HOST" 'bash -s'

echo
echo "done. Next: run the reference trials over RCON, then collect them with"
echo "  ssh $HOST \"journalctl -u cs2-soccermod-test.service --since '10 min ago' | grep SM2CSSREF\" > css-vs-cs2.log"
