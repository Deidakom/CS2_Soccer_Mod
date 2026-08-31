#!/usr/bin/env bash
set -euo pipefail

if [[ "$(id -u)" -ne 0 ]]; then
  printf 'Run this installer as root.\n' >&2
  exit 1
fi

release_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
server_root="${CS2_SERVER_ROOT:-/home/gameserver/cs2}"
game_root="${CS2_GAME_ROOT:-$server_root/game/csgo}"
payload_root="$release_root/game/csgo"

if [[ ! -f "$game_root/gameinfo.gi" ]]; then
  printf 'CS2 game directory was not found at %s\n' "$game_root" >&2
  printf 'Set CS2_SERVER_ROOT or CS2_GAME_ROOT and try again.\n' >&2
  exit 1
fi
if [[ ! -d "$game_root/addons/counterstrikesharp" ]]; then
  printf 'CounterStrikeSharp is not installed under %s/addons.\n' "$game_root" >&2
  exit 1
fi
if [[ ! -d "$payload_root" || ! -f "$release_root/SHA256SUMS" ]]; then
  printf 'This release archive is incomplete.\n' >&2
  exit 1
fi

(cd "$release_root" && sha256sum --check SHA256SUMS)

server_user="${CS2_SERVER_USER:-$(stat -c '%U' "$game_root")}"
server_group="${CS2_SERVER_GROUP:-$(stat -c '%G' "$game_root")}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_root="$game_root/addons/soccermod-backups/$timestamp"
installed=0
backed_up=0

while IFS= read -r -d '' source; do
  relative="${source#"$payload_root/"}"
  target="$game_root/$relative"
  if [[ -f "$target" ]]; then
    install -D -m 0644 "$target" "$backup_root/$relative"
    backed_up=$((backed_up + 1))
  fi
  install -D -o "$server_user" -g "$server_group" -m 0644 "$source" "$target"
  installed=$((installed + 1))
done < <(find "$payload_root" -type f -print0)

# The plugin writes JSON settings beside its DLL. Keep only our plugin folder
# writable by the account that runs the CS2 server.
plugin_dir="$game_root/addons/counterstrikesharp/plugins/SoccerModNativeHull"
chown -R "$server_user:$server_group" "$plugin_dir"

printf 'Installed %d SoccerMod files into %s\n' "$installed" "$game_root"
if [[ "$backed_up" -gt 0 ]]; then
  printf 'Backed up %d replaced files to %s\n' "$backed_up" "$backup_root"
fi
printf 'Restart the CS2 server, then run: css_plugins list\n'
printf 'Fresh install: grant your SteamID64 with css_admin_add <steamid64> root via RCON.\n'
