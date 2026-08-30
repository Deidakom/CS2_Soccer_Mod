#!/usr/bin/env bash
set -euo pipefail

server_root="${CS2_SERVER_ROOT:-/home/gameserver/cs2}"
server_port="${CS2_SERVER_PORT:-27017}"

printf 'os='; . /etc/os-release; printf '%s %s\n' "${ID:-unknown}" "${VERSION_ID:-unknown}"
printf 'architecture=%s\n' "$(uname -m)"
printf 'server_root=%s\n' "$server_root"
printf 'server_port=%s\n' "$server_port"
printf 'steamcmd='
if command -v steamcmd >/dev/null 2>&1; then
  command -v steamcmd
elif command -v steamcmd.sh >/dev/null 2>&1; then
  command -v steamcmd.sh
elif [[ -x /home/gameserver/steamcmd/steamcmd.sh ]]; then
  printf '/home/gameserver/steamcmd/steamcmd.sh\n'
else
  printf 'missing\n'
fi
printf 'free_space_bytes=%s\n' "$(df --output=avail -B1 "$(dirname "$server_root")" | tail -n 1 | tr -d ' ')"
printf 'gameserver_user='; id gameserver >/dev/null 2>&1 && printf 'present\n' || printf 'missing\n'
printf 'steamclient64='
if [[ -r /home/gameserver/.steam/sdk64/steamclient.so ]]; then
  printf 'present\n'
else
  printf 'missing\n'
fi
printf 'port_listener='
port_listener="$(ss -H -l -u -n "sport = :$server_port" 2>/dev/null || true)"
if [[ -n "$port_listener" ]]; then
  printf '%s\n' "$port_listener"
else
  printf 'none\n'
fi
printf 'css_service='; systemctl is-active cssserver.service 2>/dev/null || true
printf 'cs2_test_service='; systemctl is-active cs2-soccermod-test.service 2>/dev/null || true
