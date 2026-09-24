#!/usr/bin/env bash
# Re-publishes a Workshop addon that the server already has cached as a new,
# public item owned by your Steam account, so MultiAddonManager can deliver it
# to every joining player. Run as root on the server, in your own terminal
# (SteamCMD asks for your password and Steam Guard code itself; never paste
# them anywhere else):
#
#   sudo bash deploy/testserver/republish-addon.sh login STEAM_LOGIN
#   sudo bash deploy/testserver/republish-addon.sh publish STEAM_LOGIN OLD_ID "Title"
#
# `publish` copies <old>_dir.vpk from the server's Workshop cache, creates the
# new item, then renames the file to <new>_dir.vpk and updates the item:
# MultiAddonManager mounts steamapps/workshop/content/730/<id>/<id>_dir.vpk,
# so the file name has to carry the new id. A failed run can be repeated; it
# resumes with the item it already created.
set -Eeuo pipefail

server_root=${CS2_SERVER_ROOT:-/home/gameserver/cs2}
server_user=${CS2_SERVER_USER:-gameserver}
work_root=${SOCCERMOD_REPUBLISH_ROOT:-/home/$server_user/workshop-republish}
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
serverctl=$repo_root/deploy/testserver/serverctl.py
preview=$repo_root/src/workshop-addon/soccermod_classic_ui/workshop_preview.jpg

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }
die() { log "ERROR: $*" >&2; exit 1; }
usage() { sed -n '2,/^set -Eeuo/p' "$0" | sed '$d; s/^# \{0,1\}//'; exit 2; }

[[ $EUID == 0 ]] || die "Run as root."
id "$server_user" >/dev/null 2>&1 || die "Missing server user $server_user."
as_server_user() { runuser -u "$server_user" -- "$@"; }
steamcmd=""
for candidate in "/home/$server_user/steamcmd/steamcmd.sh" "$(command -v steamcmd 2>/dev/null)" /usr/games/steamcmd; do
    if [[ -n $candidate && -x $candidate ]]; then steamcmd=$candidate; break; fi
done
[[ -n $steamcmd ]] || die "SteamCMD not found."

command=${1:-}
login=${2:-}
[[ -n $command && $login =~ ^[A-Za-z0-9_]+$ ]] || usage

if [[ $command == login ]]; then
    log "SteamCMD will ask for the password and Steam Guard code of '$login'."
    as_server_user "$steamcmd" +login "$login" +quit
    log "Done. If SteamCMD reported 'Logged in OK', continue with: publish $login <old id> \"<title>\""
    exit 0
fi
[[ $command == publish && $# -ge 4 && $3 =~ ^[0-9]+$ && -n $4 ]] || usage
old=$3 title=$4

# The copy players used to get: the server's own Workshop cache.
cache=""
for base in "$server_root" "/home/$server_user/Steam" "/home/$server_user/.steam/steam"; do
    if [[ -f $base/steamapps/workshop/content/730/$old/${old}_dir.vpk ]]; then
        cache=$base/steamapps/workshop/content/730/$old
        break
    fi
done
[[ -n $cache ]] || die "No cached ${old}_dir.vpk found under $server_root/steamapps or /home/$server_user/Steam/steamapps."

work=$work_root/$old
content=$work/content
vdf=$work/item.vdf
new_id_file=$work/new_id
install -d -o "$server_user" -g "$server_user" "$work_root" "$work" "$content"
install -o "$server_user" -g "$server_user" -m 644 "$preview" "$work/preview.jpg"

write_vdf() {  # publishedfileid changenote
    cat >"$vdf" <<VDF
"workshopitem"
{
	"appid"		"730"
	"publishedfileid"	"$1"
	"contentfolder"	"$content"
	"previewfile"	"$work/preview.jpg"
	"visibility"	"0"
	"title"		"$title"
	"description"	"Content addon for the CS2 SoccerMod server. The server delivers it automatically; no need to subscribe."
	"changenote"	"$2"
}
VDF
    chown "$server_user:$server_user" "$vdf"
}
build() {  # log file
    # Cached credentials only: without them SteamCMD fails instead of waiting for a password.
    as_server_user "$steamcmd" +login "$login" +workshop_build_item "$vdf" +quit </dev/null 2>&1 | tee "$1" || true
    grep -qi "success" "$1" || die "SteamCMD did not report success (log: $1). Run 'login $login' first if it asked for a password."
}
stage_as() {  # file name prefix
    find "$content" -mindepth 1 -delete
    local file
    for file in "$cache/${old}_"*.vpk; do
        install -o "$server_user" -g "$server_user" -m 644 "$file" "$content/$1_${file##*/${old}_}"
    done
}

if [[ -s $new_id_file ]]; then
    new=$(<"$new_id_file")
    log "Resuming with item $new created earlier for $old."
else
    log "Creating a new public item from $cache ($(du -sh "$cache" | cut -f1))."
    stage_as pending
    write_vdf 0 "Re-published copy of addon $old."
    build "$work/create.log"
    new=$(sed -n 's/.*"publishedfileid"[[:space:]]*"\([0-9]\{6,\}\)".*/\1/p' "$vdf")
    [[ -n $new ]] || new=$(grep -oiE 'publish(ed)?file ?id[^0-9]*[0-9]{6,}' "$work/create.log" | grep -oE '[0-9]{6,}' | tail -n 1 || true)
    [[ -n $new ]] || die "Could not read the new item id from SteamCMD (log: $work/create.log)."
    echo "$new" >"$new_id_file"
    log "Created item $new."
fi

log "Uploading the content as ${new}_dir.vpk, the name MultiAddonManager mounts."
stage_as "$new"
write_vdf "$new" "File names match the item id."
build "$work/update.log"

log "Steam's view of the new item:"
python3 "$serverctl" workshop-status "$new" || true
cat <<NEXT

Item $new replaces $old. Before switching the server over:
  1. Open https://steamcommunity.com/sharedfiles/filedetails/?id=$new while
     logged in as '$login'. If Steam shows a Workshop legal agreement banner,
     accept it; until then the item stays hidden from everyone else.
  2. Run: python3 $serverctl workshop-status $new
     It must print "ok". A new item can take a while to become visible.
  3. Replace $old with $new in mm_extra_addons in
     $server_root/game/csgo/cfg/multiaddonmanager/multiaddonmanager.cfg
NEXT
