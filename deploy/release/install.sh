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
if [[ ! -d "$game_root/addons/metamod" ]]; then
  printf 'Metamod:Source is not installed under %s/addons.\n' "$game_root" >&2
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
  install -D -o "$server_user" -g "$server_group" -m 0755 "$source" "$target"
  installed=$((installed + 1))
done < <(find "$payload_root" -type f -print0)

# The plugin writes JSON settings beside its DLL. Keep only our plugin folder
# writable by the account that runs the CS2 server.
plugin_dir="$game_root/addons/counterstrikesharp/plugins/SoccerModNativeHull"
chown -R "$server_user:$server_group" "$plugin_dir"

# Recommended gameplay cvars: install as an example only if nothing with
# this name already exists, so a rerun on an existing server never
# clobbers the operator's own settings.
example_cfg_source="$release_root/examples/soccermod_server.cfg"
example_cfg_target="$game_root/cfg/soccermod_server.cfg"
if [[ -f "$example_cfg_source" && ! -f "$example_cfg_target" ]]; then
  install -D -o "$server_user" -g "$server_group" -m 0644 \
    "$example_cfg_source" "$example_cfg_target"
  printf 'Installed example config: %s (review it, then add `exec soccermod_server.cfg` to your startup)\n' \
    "$example_cfg_target"
fi

# No metaplugins.ini edit needed: the installed soccermod_native.vdf
# tells Metamod to auto-load the plugin on its own.

printf 'Installed %d SoccerMod files into %s\n' "$installed" "$game_root"
if [[ "$backed_up" -gt 0 ]]; then
  printf 'Backed up %d replaced files to %s\n' "$backed_up" "$backup_root"
fi
printf '\n'
printf 'Restart the CS2 server, then verify in the server console:\n'
printf '  meta list           -> must list "SoccerMod Native Physics Bridge"\n'
printf '  css_plugins list    -> must list "CS2 SoccerMod" (1.1.0)\n'
printf 'Fresh install: grant your own SteamID64 root via RCON:\n'
printf '  css_admin_add <steamid64> root\n'
