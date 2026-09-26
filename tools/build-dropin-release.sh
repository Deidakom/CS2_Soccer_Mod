#!/usr/bin/env bash
# Builds the drop-in releases (owner, 2026-09-26: "copy the mod into the
# Windows or Linux gameserver and it's ready to use"):
#
#   artifacts/releases/CS2-SoccerMod-<version>-linux.zip   (+ .sha256)
#   artifacts/releases/CS2-SoccerMod-<version>-windows.zip (+ .sha256)
#
# Each ZIP holds one folder whose contents go into the CS2 server folder:
# game/csgo/... (Metamod, CounterStrikeSharp with runtime, MultiAddonManager,
# SoccerMod plugin + native bridge, cfgs), install script, INSTALL.txt,
# LICENSES/, VERSION and SHA256SUMS.
#
#   bash tools/build-dropin-release.sh v1.5.0
#
# Inputs: the built plugin (dotnet build ... -c Release), the Linux bridge in
# deploy/release/payload, the Windows bridge in deploy/release/payload-windows
# (CI: native-windows.yml). Third-party files come from deploy/release/dropin/
# deps.lock and are checked against their sha256 (cached in $DROPIN_CACHE).
# Needs curl, unzip, tar, sha256sum and python3.
set -Eeuo pipefail

version=${1:?usage: $0 <version, e.g. v1.5.0>}
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
dropin=$repo/deploy/release/dropin
out=$repo/artifacts/releases
cache=${DROPIN_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/soccermod-dropin}
plugin=$repo/src/server-plugin/SoccerModMvp/bin/Release/net10.0/SoccerModNativeHull.dll
winbridge=$repo/deploy/release/payload-windows/game/csgo/addons/soccermod_native/bin/win64/soccermod_native.dll
linbridge=$repo/deploy/release/payload/game/csgo/addons/soccermod_native/bin/linuxsteamrt64/soccermod_native.so
for f in "$plugin" "$winbridge" "$linbridge"; do
    [[ -f $f ]] || { echo "missing build input: $f" >&2; exit 1; }
done
mkdir -p "$cache" "$out"

# Download (once) and verify one pinned file; prints its cached path.
fetch() {
    local sha=$1 url=$2 file=$cache/$1
    if [[ ! -f $file ]]; then
        curl -fsSL --retry 3 -o "$file.part" "$url"
        mv "$file.part" "$file"
    fi
    if [[ $(sha256sum "$file" | cut -d' ' -f1) != "$sha" ]]; then
        rm -f "$file"
        echo "checksum mismatch for $url" >&2
        exit 1
    fi
    printf '%s' "$file"
}

commit=$(git -C "$repo" rev-parse HEAD)
for os in linux windows; do
    name=CS2-SoccerMod-${version#v}-$os
    stage=$out/$name
    rm -rf "$stage" "$out/$name.zip" "$out/$name.zip.sha256"
    game=$stage/game/csgo
    mkdir -p "$game" "$stage/LICENSES"

    # Third-party: archives unpack straight into game/csgo (they all start
    # with addons/ or cfg/); licences go to LICENSES/.
    while read -r los kind sha url dest; do
        [[ -z $los || $los == \#* ]] && continue
        [[ $los == "$os" || $los == any ]] || continue
        file=$(fetch "$sha" "$url")
        case $kind in
            archive)
                case $url in
                    *.zip) unzip -q -o "$file" -d "$game" ;;
                    *.tar.gz) tar xzf "$file" -C "$game" ;;
                esac ;;
            license) cp "$file" "$stage/LICENSES/$dest" ;;
        esac
    done < "$dropin/deps.lock"

    # SoccerMod: committed payload (map resources, menu, bridge .vdf), then the
    # bridge for this OS and the freshly built plugin.
    cp -r "$repo/deploy/release/payload/game/." "$stage/game/"
    if [[ $os == windows ]]; then
        rm -rf "$game/addons/soccermod_native/bin/linuxsteamrt64"
        cp -r "$repo/deploy/release/payload-windows/game/." "$stage/game/"
    fi
    install -D -m 644 "$plugin" "$game/addons/counterstrikesharp/plugins/SoccerModNativeHull/SoccerModNativeHull.dll"

    # Server configs (MultiAddonManager's own default cfg is replaced).
    install -D -m 644 "$repo/deploy/release/examples/multiaddonmanager.cfg" "$game/cfg/multiaddonmanager/multiaddonmanager.cfg"
    install -D -m 644 "$repo/deploy/release/examples/gamemode_casual_server.cfg" "$game/cfg/gamemode_casual_server.cfg"
    install -D -m 644 "$repo/deploy/release/examples/maps/soccer_cssl_stadium_v8.cfg" "$game/cfg/maps/soccer_cssl_stadium_v8.cfg"
    install -D -m 644 "$repo/deploy/release/soccermod_server.cfg" "$game/cfg/soccermod_server.cfg"

    # Installer + docs, with the line endings each OS expects.
    if [[ $os == linux ]]; then
        tr -d '\r' < "$dropin/install.sh" > "$stage/install.sh"
        chmod 755 "$stage/install.sh"
        tr -d '\r' < "$dropin/INSTALL.txt" > "$stage/INSTALL.txt"
    else
        for f in install.bat install.ps1 INSTALL.txt; do
            tr -d '\r' < "$dropin/$f" | sed 's/$/\r/' > "$stage/$f"
        done
    fi
    printf '%s\ncommit=%s\nos=%s\n' "$version" "$commit" "$os" > "$stage/VERSION"
    (cd "$stage" && find . -type f ! -name SHA256SUMS -printf '%P\n' | LC_ALL=C sort | xargs -d '\n' sha256sum > SHA256SUMS)

    # python's zipfile keeps the Unix modes (install.sh stays executable).
    python3 - "$stage" "$out/$name.zip" <<'PY'
import os, sys, zipfile
stage, target = sys.argv[1], sys.argv[2]
base = os.path.dirname(stage)
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for folder, _, files in sorted(os.walk(stage)):
        for name in sorted(files):
            path = os.path.join(folder, name)
            info = zipfile.ZipInfo.from_file(path, os.path.relpath(path, base))
            info.compress_type = zipfile.ZIP_DEFLATED
            with open(path, "rb") as f:
                z.writestr(info, f.read())
PY
    (cd "$out" && sha256sum "$name.zip" > "$name.zip.sha256")
    echo "$out/$name.zip: $(find "$stage" -type f | wc -l) files, $(du -h "$out/$name.zip" | cut -f1)"
done
