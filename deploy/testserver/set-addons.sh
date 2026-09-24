#!/usr/bin/env bash
# Sets the Workshop addons MultiAddonManager makes every player download:
#
#   sudo bash deploy/testserver/set-addons.sh 3807366566 3797479770   # menu + jerseys
#   sudo bash deploy/testserver/set-addons.sh 3797479770              # jerseys only
#   sudo bash deploy/testserver/set-addons.sh none                    # no extra addons
#   sudo bash deploy/testserver/set-addons.sh --show                  # current list, tested
#
# Every addon is first downloaded anonymously with SteamCMD, the way the server
# downloads it. If Steam refuses one (private, awaiting approval, broken), the
# list stays unchanged: MultiAddonManager would otherwise retry and reload the
# map about once a second, throwing every player out. After the change the
# server restarts; an addon that still fails to download there is taken out
# again and the server restarts once more. --no-restart only edits the list.
set -Eeuo pipefail

server_root=${CS2_SERVER_ROOT:-/home/gameserver/cs2}
server_user=${CS2_SERVER_USER:-gameserver}
service=${CS2_SERVICE:-cs2-soccermod-test.service}
port=${CS2_SERVER_PORT:-27017}
settle_seconds=${SOCCERMOD_ADDON_SETTLE_SECONDS:-45}
game_root=$server_root/game/csgo
cfg=$game_root/cfg/multiaddonmanager/multiaddonmanager.cfg
workshop_map=soccer_cssl_stadium_v8
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
serverctl=$repo_root/deploy/testserver/serverctl.py

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }
die() { log "ERROR: $*" >&2; exit 1; }
usage() { sed -n '2,/^set -Eeuo/p' "$0" | sed '$d; s/^# \{0,1\}//'; exit 2; }

restart=1 show=0 clear=0 ids=()
(($#)) || usage
for arg in "$@"; do
    case $arg in
        --no-restart) restart=0 ;;
        --show) show=1 ;;
        none) clear=1 ;;
        -h|--help) usage ;;
        *)
            [[ $arg =~ ^[0-9]+$ ]] || die "'$arg' is not a Workshop id (only digits, e.g. 3797479770)."
            [[ " ${ids[*]} " == *" $arg "* ]] || ids+=("$arg")
            ;;
    esac
done
((clear == 0 || ${#ids[@]} == 0)) || die "Use either 'none' or Workshop ids."
((show || clear || ${#ids[@]})) || usage

[[ $EUID == 0 ]] || die "Run as root."
[[ -f $cfg ]] || die "MultiAddonManager config not found at $cfg."
steamcmd=""
for candidate in "/home/$server_user/steamcmd/steamcmd.sh" "$(command -v steamcmd 2>/dev/null)" /usr/games/steamcmd; do
    if [[ -n $candidate && -x $candidate ]]; then steamcmd=$candidate; break; fi
done
[[ -n $steamcmd ]] || die "SteamCMD not found."
if [[ ! -f $game_root/addons/metamod/multiaddonmanager.vdf ]]; then
    log "Note: MultiAddonManager is not armed (no addons/metamod/multiaddonmanager.vdf); the list has no effect until it is."
fi

# 0 when an anonymous download of the item succeeds and MultiAddonManager could mount it.
can_download() {
    local id=$1 output dir
    output=$(timeout 900 runuser -u "$server_user" -- "$steamcmd" +login anonymous +workshop_download_item 730 "$id" +quit </dev/null 2>&1 || true)
    if ! grep -q "Success. Downloaded item $id" <<<"$output"; then
        log "  $id: Steam refused the download: $(grep -m 1 -iE 'error|fail|denied' <<<"$output" || echo 'no reason given')"
        log "  $id: private, still awaiting approval, or broken. Check the item on Steam and try again later."
        return 1
    fi
    dir=$(sed -n "s/.*Downloaded item $id to \"\([^\"]*\)\".*/\1/p" <<<"$output" | tail -n 1)
    if [[ -n $dir && ! -f $dir/${id}_dir.vpk && ! -f $dir/$id.vpk ]]; then
        log "  $id: downloaded, but it has no ${id}_dir.vpk, so MultiAddonManager cannot mount it."
        return 1
    fi
    log "  $id: ok"
}

current=$(python3 "$serverctl" mam-addons "$cfg")
if ((show)); then
    log "Current addons: $current"
    if [[ $current != none ]]; then
        IFS=, read -r -a listed <<<"$current"
        for id in "${listed[@]}"; do can_download "$id" || true; done
    fi
    exit 0
fi

if ((${#ids[@]})); then
    log "Test-downloading ${#ids[@]} addon(s) the way the server does:"
    refused=0
    for id in "${ids[@]}"; do can_download "$id" || refused=1; done
    ((refused == 0)) || die "Nothing was changed. Leave out the refused addon(s), or wait until Steam lets everyone download them."
fi

cp -a "$cfg" "$cfg.bak-$(date -u +%Y%m%dT%H%M%SZ)"
log "Addons: $current -> $(python3 "$serverctl" mam-addons "$cfg" "${ids[@]:-none}")"
((restart)) || { log "Restart the server to apply it: systemctl restart $service"; exit 0; }

# Restart, wait for the map, then read MultiAddonManager's own download results.
restart_and_check() {
    local started deadline info journal
    started=$(date -u '+%Y-%m-%d %H:%M:%S')
    log "Restarting $service"
    systemctl restart "$service"
    deadline=$((SECONDS + 300))
    until [[ ${info:-} == *"map=$workshop_map"* ]]; do
        ((SECONDS < deadline)) || { log "The server did not come up on $workshop_map within 5 minutes; see journalctl -u $service"; return 2; }
        sleep 5
        info=$(python3 "$serverctl" a2s 127.0.0.1 "$port" 2>/dev/null || true)
    done
    log "Server is up; watching MultiAddonManager for ${settle_seconds}s"
    sleep "$settle_seconds"
    journal=$(journalctl -u "$service" --since "$started UTC" --no-pager 2>/dev/null || true)
    failed=$(grep -oE 'Addon [0-9]+ download failed with reason "[^"]*"' <<<"$journal" | sort -u || true)
    reloads=$(grep -c "reloading map" <<<"$journal" || true)
    [[ -z $failed ]]
}

if restart_and_check; then
    log "Done: no addon download failed (map reloads since restart: $reloads). Players can join."
    exit 0
fi
[[ -n ${failed:-} ]] || exit 1
log "The server itself could not download:"
printf '    %s\n' "$failed"
keep=()
for id in "${ids[@]}"; do
    grep -q "Addon $id download failed" <<<"$failed" || keep+=("$id")
done
log "Taking them out again so players can join: $(python3 "$serverctl" mam-addons "$cfg" "${keep[@]:-none}")"
if restart_and_check; then
    log "Done with the remaining addons. Add the others back once Steam lets everyone download them."
    exit 0
fi
die "Downloads still fail after removing them; set the list to none: sudo bash $0 none"
