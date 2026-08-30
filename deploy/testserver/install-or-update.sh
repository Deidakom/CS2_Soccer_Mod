#!/usr/bin/env bash
set -euo pipefail

if [[ "$(id -u)" -ne 0 ]]; then
  printf 'Run this installer as root.\n' >&2
  exit 1
fi

package_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
server_root="${CS2_SERVER_ROOT:-/home/gameserver/cs2}"
server_user="${CS2_SERVER_USER:-gameserver}"
steamcmd_root="${STEAMCMD_ROOT:-/home/gameserver/steamcmd}"
addon_name="soccermod_phase1"
addon_source="$package_root/payload/game/csgo_addons/$addon_name"
addon_target="$server_root/game/csgo_addons/$addon_name"
cfg_source="$package_root/deploy/testserver/soccermod_test.cfg"
cfg_target="$server_root/game/csgo/cfg/soccermod_test.cfg"
map_cfg_source="$package_root/deploy/testserver/soccer_cssl_stadium_v8.cfg"
map_cfg_target="$server_root/game/csgo/cfg/maps/soccer_cssl_stadium_v8.cfg"
service_source="$package_root/deploy/testserver/cs2-soccermod-test.service"
service_target="/etc/systemd/system/cs2-soccermod-test.service"
environment_target="/etc/cs2-soccermod-test.env"
steamclient_source="$steamcmd_root/linux64/steamclient.so"

if ! id "$server_user" >/dev/null 2>&1; then
  printf 'Missing server user: %s\n' "$server_user" >&2
  exit 1
fi
server_home="$(getent passwd "$server_user" | cut -d: -f6)"
steamclient_target="$server_home/.steam/sdk64/steamclient.so"
if [[ ! -x "$server_root/game/cs2.sh" || ! -x "$server_root/game/bin/linuxsteamrt64/cs2" ]]; then
  printf 'CS2 dedicated server is not installed at %s\n' "$server_root" >&2
  exit 1
fi
if [[ ! -r "$steamclient_source" ]]; then
  printf 'Missing 64-bit Steam client library: %s\n' "$steamclient_source" >&2
  exit 1
fi
if [[ ! -d "$addon_source" || ! -f "$cfg_source" || ! -f "$map_cfg_source" || ! -f "$service_source" ]]; then
  printf 'The extracted deployment package is incomplete.\n' >&2
  exit 1
fi
if [[ ! -f "$package_root/SHA256SUMS" ]]; then
  printf 'Missing SHA256SUMS.\n' >&2
  exit 1
fi

(cd "$package_root" && sha256sum --check SHA256SUMS)

install -d -o "$server_user" -g "$server_user" "$addon_target"
cp -a "$addon_source/." "$addon_target/"
chown -R "$server_user:$server_user" "$addon_target"

install -d -o "$server_user" -g "$server_user" "$(dirname "$cfg_target")"
install -o "$server_user" -g "$server_user" -m 0644 "$cfg_source" "$cfg_target"
install -d -o "$server_user" -g "$server_user" "$(dirname "$map_cfg_target")"
install -o "$server_user" -g "$server_user" -m 0644 "$map_cfg_source" "$map_cfg_target"
install -o root -g root -m 0644 "$service_source" "$service_target"

if [[ ! -e "$steamclient_target" ]]; then
  install -d -o "$server_user" -g "$server_user" -m 0755 \
    "$(dirname "$steamclient_target")"
  ln -s "$steamclient_source" "$steamclient_target"
  chown -h "$server_user:$server_user" "$steamclient_target"
fi
if [[ ! -r "$steamclient_target" ]]; then
  printf 'Steam client library is not readable through %s\n' \
    "$steamclient_target" >&2
  exit 1
fi

if [[ ! -f "$environment_target" ]]; then
  install -o root -g root -m 0600 \
    "$package_root/deploy/testserver/cs2-soccermod-test.env.example" \
    "$environment_target"
  printf 'Created %s. Fill in GSLT and RCON values before starting.\n' \
    "$environment_target"
fi

systemctl daemon-reload
printf 'Installed addon=%s\n' "$addon_target"
printf 'Installed config=%s\n' "$cfg_target"
printf 'Installed map config=%s\n' "$map_cfg_target"
printf 'Installed service=%s\n' "$service_target"
printf 'Next: set secrets in %s, then enable/start cs2-soccermod-test.service.\n' \
  "$environment_target"
