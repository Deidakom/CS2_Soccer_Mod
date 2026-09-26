#!/usr/bin/env bash
# Builds the native bridge (soccermod_native.so) so it loads on older Linux
# hosts too: game-server hosters often run glibc 2.31-2.36, while a build on a
# current distro needs glibc 2.38 (__isoc23_sscanf) and fails there with
# "version `GLIBC_2.38' not found" (seen on myarena.ru, 2026-09-25).
#
# Runs the pinned src/native-plugin/soccermod_native/build-linux.sh inside an
# ubuntu:20.04 container (glibc 2.31), checks that the result needs no newer
# glibc than 2.31, and with --install copies it into the release payload.
#
#   bash deploy/native/build-portable.sh            # build and check
#   bash deploy/native/build-portable.sh --install  # and update the payload
#
# Needs docker. Dependencies are cached in $SOCCERMOD_NATIVE_DEPS
# (default ~/.cache/soccermod-native-portable).
set -Eeuo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
deps=${SOCCERMOD_NATIVE_DEPS:-${XDG_CACHE_HOME:-$HOME/.cache}/soccermod-native-portable}
payload=$repo_root/deploy/release/payload/game/csgo/addons/soccermod_native/bin/linuxsteamrt64/soccermod_native.so
max_glibc=2.31
install=0
case ${1:-} in
    --install) install=1 ;;
    "") ;;
    *) echo "Usage: $0 [--install]" >&2; exit 2 ;;
esac
command -v docker >/dev/null || { echo "docker is required" >&2; exit 1; }
mkdir -p "$deps"

docker run --rm \
    -v "$repo_root:/repo:ro" \
    -v "$deps:/deps" \
    -e SOCCERMOD_NATIVE_DEPS=/deps -e CC=clang-12 -e CXX=clang++-12 \
    ubuntu:20.04 bash -Eeuo pipefail -c '
        export DEBIAN_FRONTEND=noninteractive
        apt-get update -qq
        apt-get install -y -qq --no-install-recommends git ca-certificates python3 \
            clang-12 lld-12 binutils libc6-dev libstdc++-10-dev >/dev/null
        ln -sf /usr/bin/ld.lld-12 /usr/local/bin/ld.lld
        ln -sf /usr/bin/ld.lld-12 /usr/local/bin/lld
        bash /repo/src/native-plugin/soccermod_native/build-linux.sh
    '

so=$deps/build/package/cs2/addons/soccermod_native/bin/linuxsteamrt64/soccermod_native.so
[[ -f $so ]] || { echo "build produced no $so" >&2; exit 1; }
needed=$(objdump -T "$so" | grep -o 'GLIBC_[0-9.]*' | sed 's/GLIBC_//' | sort -uV | tail -n 1)
if [[ $(printf '%s\n%s\n' "$needed" "$max_glibc" | sort -V | tail -n 1) != "$max_glibc" ]]; then
    echo "$so needs glibc $needed, more than $max_glibc" >&2
    exit 1
fi
echo "Built $so: needs glibc $needed (limit $max_glibc), sha256 $(sha256sum "$so" | cut -c1-12)"
if ((install)); then
    install -m 644 "$so" "$payload"
    echo "Installed into $payload"
fi
