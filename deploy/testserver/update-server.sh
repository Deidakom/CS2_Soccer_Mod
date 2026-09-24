#!/usr/bin/env bash
# Keeps the CS2 SoccerMod server running across CS2 updates. Run as root from
# a checkout of this repository on the server:
#
#   sudo bash deploy/testserver/update-server.sh --check
#   sudo bash deploy/testserver/update-server.sh --build-plugin --sync-payload
#
# Metamod and CounterStrikeSharp must come from the same hook line. Metamod
# builds from 1461 only load KHook plugins (plugin API 18) and refuse
# CounterStrikeSharp up to v1.0.374 and the committed native bridge, which are
# SourceHook builds (API 17). So by default this script never touches Metamod,
# pins CounterStrikeSharp to v1.0.374, and refuses any combination that would
# stop the mod from loading. A KHook migration moves both together:
#   --khook --metamod-build 1469 --cssharp-version <first KHook release>
# and needs a native bridge rebuilt against KHook Metamod (spin is off until then).
#
# An update run, in order - nothing is stopped before every download and the
# optional plugin build have succeeded:
#   1. fetch the pinned CounterStrikeSharp release (with .NET runtime) and, only
#      with --metamod-build, that Metamod drop; build the DLL with --build-plugin;
#   2. wait until no human player is connected (A2S), unless --force;
#   3. back up Metamod, the CounterStrikeSharp runtime, the native bridge, the
#      SoccerMod plugin folder and gameinfo.gi, and write rollback.sh;
#   4. stop the service and update CS2 (app 730) with SteamCMD;
#   5. re-add Metamod's search path: every CS2 update restores gameinfo.gi,
#      which otherwise silently disables Metamod, CounterStrikeSharp and the mod;
#   6. install Metamod / CounterStrikeSharp only when they changed, keeping
#      metaplugins.ini and CounterStrikeSharp's configs and plugins;
#   7. install the SoccerMod DLL and, with --sync-payload, the committed payload
#      (native bridge, ball model, menu and radar resources) where it differs;
#   8. start the service and verify the map, Metamod, CounterStrikeSharp, the
#      native bridge and the SoccerMod plugin over A2S and RCON. A new DLL that
#      does not load is rolled back automatically.
#
# Options:
#   --check                  report the current state and change nothing
#   --build-plugin           build the plugin DLL from this checkout (installs a
#                            private .NET 10 SDK under /opt/soccermod-dotnet if needed)
#   --plugin-dll PATH        install this DLL instead (requires --plugin-sha256)
#   --plugin-sha256 SHA256
#   --sync-payload           install differing payload files (never the payload DLL)
#   --validate               let SteamCMD validate every game file (slower)
#   --cssharp-version TAG    CounterStrikeSharp release to install (default v1.0.374)
#   --metamod-build N        install Metamod 2.0 drop gitN (default: leave Metamod alone)
#   --khook                  allow the KHook line (Metamod >= 1467 with a newer
#                            CounterStrikeSharp); only for a planned migration
#   --skip-cs2 | --skip-cssharp
#   --force                  do not wait for the server to be empty
#   --wait-minutes N         how long to wait for an empty server (default 60)
set -Eeuo pipefail

server_root=${CS2_SERVER_ROOT:-/home/gameserver/cs2}
game_root=$server_root/game/csgo
server_user=${CS2_SERVER_USER:-gameserver}
service=${CS2_SERVICE:-cs2-soccermod-test.service}
port=${CS2_SERVER_PORT:-27017}
env_file=${CS2_ENV_FILE:-/etc/cs2-soccermod-test.env}
backup_root=${SOCCERMOD_BACKUP_ROOT:-/home/gameserver/cs2-soccermod-backups}
dotnet_root=${SOCCERMOD_DOTNET_ROOT:-/opt/soccermod-dotnet}
verify_seconds=${SOCCERMOD_VERIFY_SECONDS:-300}
workshop_map=soccer_cssl_stadium_v8
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
serverctl=$repo_root/deploy/testserver/serverctl.py
payload_root=$repo_root/deploy/release/payload/game/csgo
plugin_relative=addons/counterstrikesharp/plugins/SoccerModNativeHull
plugin_dir=$game_root/$plugin_relative
metamod_drop=https://mms.alliedmods.net/mmsdrop/2.0
# Hook lines (see the header): Metamod builds and CounterStrikeSharp releases.
metamod_first_khook=1461
metamod_min_for_khook_cssharp=1467
cssharp_last_sourcehook=374

check_only=0 build_plugin=0 sync_payload=0 validate=0 force=0 wait_minutes=60
skip_cs2=0 skip_cssharp=0 cssharp_version=v1.0.$cssharp_last_sourcehook metamod_build="" khook=0
plugin_dll="" plugin_sha=""
while (($#)); do
    case $1 in
        --check) check_only=1 ;;
        --build-plugin) build_plugin=1 ;;
        --plugin-dll) plugin_dll=${2:?--plugin-dll needs a path}; shift ;;
        --plugin-sha256) plugin_sha=${2:?--plugin-sha256 needs a value}; shift ;;
        --sync-payload) sync_payload=1 ;;
        --validate) validate=1 ;;
        --cssharp-version) cssharp_version=${2:?--cssharp-version needs a tag}; shift ;;
        --skip-cs2) skip_cs2=1 ;;
        --metamod-build) metamod_build=${2:?--metamod-build needs a build number}; shift ;;
        --khook) khook=1 ;;
        --skip-metamod) metamod_build="" ;;  # accepted for older instructions; Metamod is opt-in
        --skip-cssharp) skip_cssharp=1 ;;
        --force) force=1 ;;
        --wait-minutes) wait_minutes=${2:?--wait-minutes needs a number}; shift ;;
        -h|--help) sed -n '2,/^set -Eeuo/p' "$0" | sed '$d; s/^# \{0,1\}//'; exit 0 ;;
        *) echo "Unknown option: $1 (see --help)" >&2; exit 2 ;;
    esac
    shift
done

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }
die() { log "ERROR: $*" >&2; exit 1; }

[[ $EUID == 0 ]] || die "Run as root."
[[ $wait_minutes =~ ^[0-9]+$ ]] || die "--wait-minutes must be a whole number."
if [[ -n $plugin_dll || -n $plugin_sha ]]; then
    [[ -f $plugin_dll && $plugin_sha =~ ^[0-9a-f]{64}$ ]] || die "--plugin-dll needs an existing file and --plugin-sha256."
    [[ $(sha256sum "$plugin_dll" | cut -d' ' -f1) == "$plugin_sha" ]] || die "Plugin DLL checksum mismatch."
    ((build_plugin == 0)) || die "Use either --build-plugin or --plugin-dll."
fi
[[ -z $metamod_build || $metamod_build =~ ^[0-9]+$ ]] || die "--metamod-build must be a drop number, e.g. 1411."
[[ $cssharp_version =~ ^v1\.0\.([0-9]+)$ ]] || die "--cssharp-version must look like v1.0.374."
cssharp_number=${BASH_REMATCH[1]}
if ((!khook)); then
    ((cssharp_number <= cssharp_last_sourcehook)) \
        || die "CounterStrikeSharp $cssharp_version is newer than v1.0.$cssharp_last_sourcehook and built for KHook Metamod; migrate deliberately with --khook and --metamod-build."
    [[ -z $metamod_build ]] || ((metamod_build < metamod_first_khook)) \
        || die "Metamod build $metamod_build only loads KHook plugins; CounterStrikeSharp $cssharp_version and the native bridge would stop loading. Use 1411 or older, or migrate with --khook."
else
    ((cssharp_number > cssharp_last_sourcehook)) || die "--khook needs a KHook CounterStrikeSharp release newer than v1.0.$cssharp_last_sourcehook."
    [[ -z $metamod_build ]] || ((metamod_build >= metamod_min_for_khook_cssharp)) \
        || die "KHook CounterStrikeSharp needs Metamod build $metamod_min_for_khook_cssharp or newer."
fi
for tool in python3 tar sha256sum systemctl find cmp; do
    command -v "$tool" >/dev/null || die "Missing required tool: $tool"
done
command -v curl >/dev/null || command -v wget >/dev/null || die "Missing curl or wget."
[[ -f $serverctl ]] || die "Missing $serverctl; run this from a full repository checkout."
[[ -f $game_root/gameinfo.gi ]] || die "CS2 is not installed at $server_root."
id "$server_user" >/dev/null 2>&1 || die "Missing server user $server_user."

as_server_user() {
    if command -v runuser >/dev/null; then runuser -u "$server_user" -- "$@"
    else su -s /bin/bash "$server_user" -c "$(printf '%q ' "$@")"; fi
}
fetch() {
    if command -v curl >/dev/null; then curl -fsSL --retry 3 --connect-timeout 20 -o "$2" "$1"
    else wget -q --tries=3 -O "$2" "$1"; fi
}
find_steamcmd() {
    local candidate
    for candidate in "$(command -v steamcmd 2>/dev/null)" "$(command -v steamcmd.sh 2>/dev/null)" \
        "/home/$server_user/steamcmd/steamcmd.sh" /usr/games/steamcmd; do
        [[ -n $candidate && -x $candidate ]] && { printf '%s\n' "$candidate"; return 0; }
    done
    return 1
}
a2s() { python3 "$serverctl" a2s 127.0.0.1 "$port" 2>/dev/null; }
rcon() { [[ -r $env_file ]] && python3 "$serverctl" rcon 127.0.0.1 "$port" "$env_file" "$@"; }
humans() {
    local info players bots
    info=$(a2s) || { echo 0; return; }
    players=$(sed -n 's/.*players=\([0-9]*\).*/\1/p' <<<"$info")
    bots=$(sed -n 's/.*bots=\([0-9]*\).*/\1/p' <<<"$info")
    echo $(( ${players:-0} - ${bots:-0} ))
}
installed_build() { python3 "$serverctl" buildid-installed "$server_root/steamapps/appmanifest_730.acf" 2>/dev/null || echo unknown; }
patch_version() { sed -n 's/^PatchVersion=//p' "$game_root/steam.inf" 2>/dev/null | tr -d '\r' || true; }
short_sha() { [[ -f $1 ]] && sha256sum "$1" | cut -c1-12 || echo missing; }
metamod_present() { grep -Eq '^[[:space:]]*Game[[:space:]]+csgo/addons/metamod[[:space:]]*(//.*)?$' "$game_root/gameinfo.gi"; }
# Build number compiled into the CS2 Metamod binary ("2.0.0-dev+1410"), or empty.
metamod_build_of() { grep -aoE '2\.0\.0-dev\+[0-9]+' "$1/addons/metamod/bin/linuxsteamrt64/metamod.2.cs2.so" 2>/dev/null | head -n 1 | sed 's/.*+//' || true; }
# Release number of a CounterStrikeSharp API assembly ("1.0.374+Branch..." -> 374), or empty.
cssharp_number_of() { grep -aoE '1\.0\.[0-9]+\+Branch' "$1/addons/counterstrikesharp/api/CounterStrikeSharp.API.dll" 2>/dev/null | head -n 1 | sed 's/^1\.0\.//; s/+.*//' || true; }
# Metamod >= 1461 loads only KHook plugins; CounterStrikeSharp <= 374 is SourceHook.
hook_lines_match() {
    local metamod=$1 cssharp=$2
    [[ -z $metamod || -z $cssharp ]] && return 0
    if ((metamod >= metamod_first_khook)); then ((cssharp > cssharp_last_sourcehook)); else ((cssharp <= cssharp_last_sourcehook)); fi
}

# 0 when any regular file below $1 differs from its counterpart below $2.
tree_differs() {
    local source=$1 target=$2 file
    while IFS= read -r -d '' file; do
        cmp -s "$file" "$target/${file#"$source/"}" || return 0
    done < <(find "$source" -type f -print0)
    return 1
}
# Drop staged files under the given prefixes whose target already exists, so
# operator-owned files (metaplugins.ini, configs, plugins) are never replaced.
keep_existing() {
    local staging=$1 target=$2 prefix file; shift 2
    for prefix in "$@"; do
        [[ -e $staging/$prefix ]] || continue
        while IFS= read -r -d '' file; do
            [[ -e $target/${file#"$staging/"} ]] && rm -f -- "$file"
        done < <(find "$staging/$prefix" -type f -print0)
    done
}

report() {
    local info
    log "CS2 build $(installed_build), PatchVersion $(patch_version)"
    if metamod_present; then log "gameinfo.gi: Metamod search path present"
    else log "gameinfo.gi: Metamod search path MISSING - Metamod, CounterStrikeSharp and SoccerMod will not load"; fi
    log "service $service: $(systemctl is-active "$service" 2>/dev/null || true)"
    info=$(a2s) && log "server: $info (humans=$(humans))" || log "server: no A2S reply on port $port"
    log "SoccerMod DLL sha256 $(short_sha "$plugin_dir/SoccerModNativeHull.dll"), native bridge $(short_sha "$game_root/addons/soccermod_native/bin/linuxsteamrt64/soccermod_native.so")"
    local mm cs
    mm=$(metamod_build_of "$game_root"); cs=$(cssharp_number_of "$game_root")
    log "Metamod build ${mm:-unknown}, CounterStrikeSharp $([[ -n $cs ]] && echo "v1.0.$cs" || echo unknown)"
    if ! hook_lines_match "$mm" "$cs"; then
        log "INCOMPATIBLE: Metamod ${mm} and CounterStrikeSharp v1.0.${cs} are on different hook lines; CounterStrikeSharp will not load."
    fi
    if [[ -n $mm ]] && ((mm >= metamod_first_khook)); then
        log "Note: the committed native bridge is a SourceHook build; KHook Metamod refuses it (ball spin off) until it is rebuilt."
    fi
    if [[ -n ${info:-} ]] && rcon_output=$(rcon "meta list" "css_plugins list" 2>/dev/null); then
        printf '%s\n' "$rcon_output" | sed 's/^/    /'
    fi
}

if ((check_only)); then
    report
    steamcmd=$(find_steamcmd) || { log "SteamCMD not found"; exit 0; }
    info_log=$(mktemp)
    as_server_user "$steamcmd" +login anonymous +app_info_update 1 +app_info_print 730 +quit >"$info_log" 2>&1 || true
    latest=$(python3 "$serverctl" buildid-latest "$info_log" 2>/dev/null || echo unknown)
    rm -f "$info_log"
    log "latest public CS2 build: $latest (installed $(installed_build))"
    log "latest Metamod 2.0 drop: $(curl -fsSL "$metamod_drop/mmsource-latest-linux" 2>/dev/null || echo unknown) (builds from $metamod_first_khook are KHook-only)"
    exit 0
fi

work=$(mktemp -d /tmp/soccermod-update.XXXXXX)
trap 'rm -rf "$work"' EXIT
steamcmd=$(find_steamcmd) || { ((skip_cs2)) || die "SteamCMD not found."; }
df_free=$(df --output=avail -B1 "$server_root" | tail -n 1 | tr -d ' ')
((df_free > 4 * 1024 * 1024 * 1024)) || die "Less than 4 GB free under $server_root."

# --- 1. downloads and builds, before anything is stopped ------------------
metamod_stage="" cssharp_stage="" new_dll=""
if [[ -n $metamod_build ]]; then
    metamod_file=mmsource-2.0.0-git$metamod_build-linux.tar.gz
    fetch "$metamod_drop/$metamod_file" "$work/metamod.tar.gz" || die "Metamod drop $metamod_file could not be downloaded."
    metamod_stage=$work/metamod
    mkdir -p "$metamod_stage"
    tar -xzf "$work/metamod.tar.gz" -C "$metamod_stage" || die "Metamod archive is damaged."
    [[ -d $metamod_stage/addons/metamod ]] || die "Metamod archive has an unexpected layout."
    [[ $(metamod_build_of "$metamod_stage") == "$metamod_build" ]] || die "Metamod archive does not contain build $metamod_build."
    keep_existing "$metamod_stage" "$game_root" addons/metamod/metaplugins.ini
    if tree_differs "$metamod_stage" "$game_root"; then log "Metamod: will install $metamod_file"
    else log "Metamod: $metamod_file already installed"; metamod_stage=""; fi
fi
if ((!skip_cssharp)); then
    release_url=https://api.github.com/repos/roflmuffin/CounterStrikeSharp/releases/tags/$cssharp_version
    fetch "$release_url" "$work/cssharp-release.json" || die "Cannot read CounterStrikeSharp $cssharp_version from GitHub."
    read -r cssharp_tag cssharp_url < <(python3 "$serverctl" github-asset "$work/cssharp-release.json" with-runtime linux) \
        || die "No CounterStrikeSharp with-runtime Linux asset found."
    fetch "$cssharp_url" "$work/cssharp.zip" || die "CounterStrikeSharp download failed."
    cssharp_stage=$work/cssharp
    python3 "$serverctl" unzip "$work/cssharp.zip" "$cssharp_stage" || die "CounterStrikeSharp archive is damaged."
    [[ -f $cssharp_stage/addons/counterstrikesharp/api/CounterStrikeSharp.API.dll ]] || die "CounterStrikeSharp archive has an unexpected layout."
    [[ $(cssharp_number_of "$cssharp_stage") == "$cssharp_number" ]] || die "CounterStrikeSharp archive is not $cssharp_version."
    keep_existing "$cssharp_stage" "$game_root" addons/counterstrikesharp/configs addons/counterstrikesharp/plugins
    if tree_differs "$cssharp_stage" "$game_root"; then log "CounterStrikeSharp: will install $cssharp_tag"
    else log "CounterStrikeSharp: $cssharp_tag already installed"; cssharp_stage=""; fi
fi
if ((build_plugin)); then
    dotnet=$(command -v dotnet || true)
    if [[ -z $dotnet ]] || ! "$dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
        dotnet=$dotnet_root/dotnet
        if ! [[ -x $dotnet ]] || ! "$dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
            log "Installing a private .NET 10 SDK under $dotnet_root"
            fetch https://dot.net/v1/dotnet-install.sh "$work/dotnet-install.sh" || die "Cannot download dotnet-install.sh."
            bash "$work/dotnet-install.sh" --channel 10.0 --install-dir "$dotnet_root" >/dev/null || die ".NET SDK installation failed."
        fi
    fi
    log "Building SoccerModNativeHull.dll from $(git -C "$repo_root" rev-parse --short HEAD 2>/dev/null || echo checkout)"
    DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$dotnet" build "$repo_root/src/server-plugin/SoccerModMvp/SoccerModMvp.csproj" -c Release -nologo -v quiet \
        || die "Plugin build failed; nothing was changed."
    new_dll=$repo_root/src/server-plugin/SoccerModMvp/bin/Release/net10.0/SoccerModNativeHull.dll
elif [[ -n $plugin_dll ]]; then
    new_dll=$plugin_dll
fi
if [[ -n $new_dll ]] && cmp -s "$new_dll" "$plugin_dir/SoccerModNativeHull.dll"; then
    log "SoccerMod DLL unchanged"; new_dll=""
fi
# The pair that will be running afterwards must share a hook line, whatever
# was requested and whatever is installed now; otherwise stop before changing anything.
target_metamod=${metamod_build:-$(metamod_build_of "$game_root")}
target_cssharp=$cssharp_number
((skip_cssharp)) && target_cssharp=$(cssharp_number_of "$game_root")
hook_lines_match "$target_metamod" "$target_cssharp" \
    || die "Metamod build $target_metamod with CounterStrikeSharp v1.0.$target_cssharp would not load (SourceHook/KHook mismatch); nothing was changed."
log "Target: Metamod build ${target_metamod:-unknown}, CounterStrikeSharp v1.0.${target_cssharp:-unknown}"

payload_files=()
if ((sync_payload)); then
    while IFS= read -r -d '' file; do
        relative=${file#"$payload_root/"}
        [[ $relative == "$plugin_relative/SoccerModNativeHull.dll" ]] && continue
        cmp -s "$file" "$game_root/$relative" || payload_files+=("$relative")
    done < <(find "$payload_root" -type f -print0)
    log "Payload: ${#payload_files[@]} file(s) to install"
fi

cs2_update=0 latest_build=unknown
if ((!skip_cs2)); then
    as_server_user "$steamcmd" +login anonymous +app_info_update 1 +app_info_print 730 +quit >"$work/app_info.log" 2>&1 || true
    latest_build=$(python3 "$serverctl" buildid-latest "$work/app_info.log" 2>/dev/null || echo unknown)
    if [[ $latest_build == unknown || $latest_build != "$(installed_build)" || $validate == 1 ]]; then cs2_update=1; fi
    log "CS2: installed $(installed_build), latest $latest_build -> $( ((cs2_update)) && echo update || echo current)"
fi

gameinfo_ok=0; metamod_present && gameinfo_ok=1
if ((!cs2_update)) && [[ -z $metamod_stage && -z $cssharp_stage && -z $new_dll && ${#payload_files[@]} -eq 0 && $gameinfo_ok == 1 ]]; then
    log "Everything is up to date; nothing restarted."
    report
    exit 0
fi

# --- 2. wait for an empty server -------------------------------------------
if ((!force)); then
    deadline=$((SECONDS + wait_minutes * 60))
    while (( $(humans) > 0 )); do
        ((SECONDS < deadline)) || die "Players are still connected after $wait_minutes minutes; nothing was changed. Use --force to restart anyway."
        log "$(humans) player(s) connected; waiting for an empty server..."
        sleep 30
    done
fi

# --- 3. backup ---------------------------------------------------------------
install -d -m 700 "$backup_root"
backup=$(mktemp -d "$backup_root/server-update-$(date -u +%Y%m%dT%H%M%SZ)-XXXXXX")
# Every step checks itself: errexit is suspended inside a `|| die` subshell.
(
    cd "$game_root" || exit 1
    shopt -s nullglob
    metamod_paths=(addons/*.vdf)
    if [[ -d addons/metamod ]]; then metamod_paths+=(addons/metamod); fi
    if ((${#metamod_paths[@]})); then tar -czf "$backup/metamod.tar.gz" "${metamod_paths[@]}" || exit 1; fi
    if [[ -d addons/counterstrikesharp ]]; then
        tar -czf "$backup/cssharp-runtime.tar.gz" --exclude=addons/counterstrikesharp/configs \
            --exclude=addons/counterstrikesharp/plugins --exclude=addons/counterstrikesharp/logs \
            addons/counterstrikesharp || exit 1
    fi
    if [[ -d addons/counterstrikesharp/configs ]]; then tar -czf "$backup/cssharp-configs.tar.gz" addons/counterstrikesharp/configs || exit 1; fi
    if [[ -d addons/soccermod_native ]]; then tar -czf "$backup/native.tar.gz" addons/soccermod_native || exit 1; fi
    if [[ -d $plugin_relative ]]; then tar -czf "$backup/plugin.tar.gz" "$plugin_relative" || exit 1; fi
    cp -a gameinfo.gi "$backup/gameinfo.gi" || exit 1
    for relative in "${payload_files[@]}"; do
        if [[ -f $relative ]]; then install -D -m 644 "$relative" "$backup/payload/$relative" || exit 1; fi
    done
) || die "Backup to $backup failed; nothing was changed."
cp -a "$serverctl" "$backup/serverctl.py"
printf 'cs2_build_before=%s\npatch_before=%s\n' "$(installed_build)" "$(patch_version)" >"$backup/versions.txt"
cat >"$backup/rollback.sh" <<ROLLBACK
#!/usr/bin/env bash
# Restores the Metamod, CounterStrikeSharp runtime, native bridge, SoccerMod DLL
# and payload files saved at $backup. Plugin data (ranks, admins, bans, settings)
# keeps its latest values. CS2 itself cannot be downgraded with SteamCMD.
set -Eeuo pipefail
[[ \$EUID == 0 ]] || { echo 'Run as root.' >&2; exit 1; }
backup=$backup
game_root=$game_root
systemctl stop $service
aside=\$backup/replaced-\$(date -u +%Y%m%dT%H%M%SZ)
mkdir -p "\$aside"
cd "\$game_root"
if [[ -f \$backup/metamod.tar.gz ]]; then
    [[ -d addons/metamod ]] && mv addons/metamod "\$aside/metamod"
    tar -xzf "\$backup/metamod.tar.gz"
fi
if [[ -f \$backup/cssharp-runtime.tar.gz ]]; then
    for part in api bin dotnet gamedata shared; do
        [[ -e addons/counterstrikesharp/\$part ]] && mkdir -p "\$aside/counterstrikesharp" && mv "addons/counterstrikesharp/\$part" "\$aside/counterstrikesharp/\$part"
    done
    tar -xzf "\$backup/cssharp-runtime.tar.gz"
fi
[[ -f \$backup/native.tar.gz ]] && tar -xzf "\$backup/native.tar.gz"
if [[ -f \$backup/plugin.tar.gz ]]; then
    tar -xzf "\$backup/plugin.tar.gz" -C "\$aside" $plugin_relative/SoccerModNativeHull.dll
    install -m 644 "\$aside/$plugin_relative/SoccerModNativeHull.dll" $plugin_relative/SoccerModNativeHull.dll
fi
[[ -d \$backup/payload ]] && cp -a "\$backup/payload/." "\$game_root/"
python3 "\$backup/serverctl.py" gameinfo-metamod "\$game_root/gameinfo.gi"
chown -R $server_user:$server_user "\$game_root/addons"
systemctl start $service
echo "Rolled back to \$backup (replaced files kept in \$aside)"
ROLLBACK
chmod 700 "$backup/rollback.sh"
log "Backup: $backup"

# --- 4-7. stop, update, install ----------------------------------------------
installing=1
restore_on_failure() {
    local code=$?
    ((installing)) || return
    log "Update step failed (exit $code); restoring the backup and restarting."
    bash "$backup/rollback.sh" || systemctl start "$service" || true
    exit "$code"
}
trap restore_on_failure ERR
log "Stopping $service"
systemctl stop "$service"

if ((cs2_update)); then
    steam_args=(+force_install_dir "$server_root" +login anonymous +app_update 730)
    ((validate)) && steam_args+=(validate)
    for attempt in 1 2 3; do
        log "SteamCMD app_update 730 (attempt $attempt)"
        as_server_user "$steamcmd" "${steam_args[@]}" +quit >"$work/steamcmd.log" 2>&1 || true
        grep -Eq "Success! App '730' (fully installed|already up to date)" "$work/steamcmd.log" && break
        tail -n 5 "$work/steamcmd.log" | sed 's/^/    /'
        ((attempt < 3)) || { log "SteamCMD did not report success."; false; }
    done
    log "CS2 now at build $(installed_build), PatchVersion $(patch_version)"
fi

log "gameinfo.gi Metamod search path: $(python3 "$serverctl" gameinfo-metamod "$game_root/gameinfo.gi")"
if [[ -n $metamod_stage ]]; then
    cp -a "$metamod_stage/." "$game_root/"
    log "Metamod installed"
fi
if [[ -n $cssharp_stage ]]; then
    cp -a "$cssharp_stage/." "$game_root/"
    log "CounterStrikeSharp $cssharp_tag installed"
fi
if [[ -n $new_dll ]]; then
    install -D -m 644 "$new_dll" "$plugin_dir/SoccerModNativeHull.dll"
    log "SoccerMod DLL installed ($(short_sha "$plugin_dir/SoccerModNativeHull.dll"))"
fi
for relative in "${payload_files[@]}"; do
    install -D -m 644 "$payload_root/$relative" "$game_root/$relative"
    log "payload: $relative"
done
chown -R "$server_user:$server_user" "$game_root/addons"
installing=0
trap - ERR

# --- 8. start and verify -----------------------------------------------------
verify() {
    local deadline=$((SECONDS + verify_seconds)) info="" meta plugins ok=1 started
    started=$(date -u '+%Y-%m-%d %H:%M:%S')
    systemctl restart "$service"
    while ((SECONDS < deadline)); do
        info=$(a2s || true)
        [[ $info == *"map=$workshop_map"* ]] && break
        sleep 5
    done
    [[ $info == *"map=$workshop_map"* ]] || { log "Map $workshop_map did not come up: ${info:-no A2S reply}"; ok=0; }
    sleep 10
    meta=$(rcon "meta list" 2>/dev/null || true)
    plugins=$(rcon "css_plugins list" 2>/dev/null || true)
    if [[ -z $meta$plugins ]]; then
        log "RCON returned nothing; checking the service journal instead."
        meta=$(journalctl -u "$service" --since "$started UTC" --no-pager 2>/dev/null || true)
        plugins=$meta
    fi
    grep -qi "CounterStrikeSharp" <<<"$meta" || { log "Metamod does not list CounterStrikeSharp."; ok=0; }
    grep -q "SoccerMod Native Physics Bridge\|\[SM2NATIVE\] loaded" <<<"$meta" || log "Warning: native bridge not listed (ball spin unavailable)."
    grep -Eq 'LOADED\]: "CS2 SoccerMod"|\[SM2DIAG\] load version' <<<"$plugins" || { log "CS2 SoccerMod is not loaded."; ok=0; }
    ((ok))
}
if verify; then
    log "Verified: map, Metamod, CounterStrikeSharp and CS2 SoccerMod are running."
elif [[ -n $new_dll && -f $backup/plugin.tar.gz ]]; then
    log "The new SoccerMod DLL did not load; restoring the previous DLL."
    tar -xzf "$backup/plugin.tar.gz" -C "$work" "$plugin_relative/SoccerModNativeHull.dll"
    install -m 644 -o "$server_user" -g "$server_user" "$work/$plugin_relative/SoccerModNativeHull.dll" "$plugin_dir/SoccerModNativeHull.dll"
    verify && log "Previous DLL is running again." || log "Still not healthy; see journalctl -u $service."
    exit 3
else
    log "Server is not healthy after the update. Newer Metamod/CounterStrikeSharp builds may be needed for this CS2 build."
    log "Journal: journalctl -u $service -n 200 --no-pager | Rollback of the mod stack: bash $backup/rollback.sh"
    exit 3
fi
report
log "Rollback of the mod stack if ever needed: bash $backup/rollback.sh"
