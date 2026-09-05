#!/usr/bin/env bash
# Targeted German CS2 update. Usage: sudo bash install-ball-handling.sh DLL SHA256 [improved|preserve]
# Existing assets, service configuration, player ranks and admin data stay live.
set -Eeuo pipefail
[[ $EUID == 0 ]] || { echo 'Run as root.' >&2; exit 1; }
source_dll=${1:?DLL path required}
expected_sha=${2:?SHA256 required}
profile_mode=${3:-improved}
[[ $profile_mode == improved || $profile_mode == preserve ]] || { echo 'Use improved or preserve for profile mode.' >&2; exit 1; }
[[ $expected_sha =~ ^[0-9a-f]{64}$ ]] || { echo 'Invalid checksum.' >&2; exit 1; }
[[ -f $source_dll && $(sha256sum "$source_dll" | cut -d' ' -f1) == "$expected_sha" ]] || { echo 'DLL checksum mismatch.' >&2; exit 1; }
service=cs2-soccermod-test.service
plugin=/home/gameserver/cs2/game/csgo/addons/counterstrikesharp/plugins/SoccerModNativeHull
[[ -f $plugin/SoccerModNativeHull.dll ]] || { echo 'Existing plugin not found; refusing fresh install.' >&2; exit 1; }
systemctl is-active --quiet "$service" || { echo 'Expected live service is not active.' >&2; exit 1; }
backup_root=/home/gameserver/cs2-soccermod-backups
install -d -m 700 "$backup_root"
backup=$(mktemp -d "$backup_root/ball-handling-$(date -u +%Y%m%dT%H%M%SZ)-XXXXXX")
# Validate and copy the artifact before stopping the service.
install -m 644 "$source_dll" "$backup/new.dll"
cp "$0" "$backup/installer.sh"
cat > "$backup/rollback.sh" <<'ROLLBACK'
#!/usr/bin/env bash
set -Eeuo pipefail
[[ $EUID == 0 ]] || { echo 'Run as root.' >&2; exit 1; }
backup=$(cd -- "$(dirname -- "$0")" && pwd)
plugin=/home/gameserver/cs2/game/csgo/addons/counterstrikesharp/plugins/SoccerModNativeHull
service=cs2-soccermod-test.service
[[ -f $backup/complete && -f $backup/plugin/SoccerModNativeHull.dll ]] || { echo 'Backup incomplete.' >&2; exit 1; }
# Ranks, bans, admins and matches keep their latest values. Only the binary
# and ball/match/menu tuning return to their exact pre-install state.
# Competitive history and saved training layouts are retained too.
systemctl stop "$service"
for name in SoccerModNativeHull.dll soccermod_settings.json soccermod_ball_handling.json soccermod_match_settings.json soccermod_menu_parity.json; do
    if [[ -f $backup/plugin/$name ]]; then
        cp -a "$backup/plugin/$name" "$plugin/$name"
    else
        rm -f -- "$plugin/$name"
    fi
done
systemctl start "$service"
systemctl is-active --quiet "$service"
echo "Restored ball plugin and settings from $backup"
ROLLBACK
chmod 700 "$backup/rollback.sh"
recover() {
    rc=$?
    trap - ERR
    if [[ -f $backup/complete ]]; then bash "$backup/rollback.sh" || true
    else systemctl start "$service" || true
    fi
    echo "Update failed; recovery attempted. Backup: $backup" >&2
    exit "$rc"
}
trap recover ERR
systemctl stop "$service"
cp -a "$plugin" "$backup/plugin"
sha256sum "$backup/plugin/SoccerModNativeHull.dll" > "$backup/previous.sha256"
touch "$backup/complete"
install -o gameserver -g gameserver -m 644 "$backup/new.dll" "$plugin/SoccerModNativeHull.dll"
# Select tested consistency changes; creativity is available explicitly.
if [[ $profile_mode == improved ]]; then
    printf '{"Profile":"improved"}\n' > "$plugin/soccermod_ball_handling.json"
    chown gameserver:gameserver "$plugin/soccermod_ball_handling.json"
fi
systemctl start "$service"
systemctl is-active --quiet "$service"
trap - ERR
printf 'Backup: %s\nRollback: bash %s/rollback.sh\n' "$backup" "$backup"
sha256sum "$plugin/SoccerModNativeHull.dll"
