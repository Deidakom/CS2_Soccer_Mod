#!/usr/bin/env bash
# Builds soccermod_native.so (Linux x86_64) against pinned Metamod:Source,
# hl2sdk and AMBuild revisions:
#
#   bash src/native-plugin/soccermod_native/build-linux.sh            # build
#   bash src/native-plugin/soccermod_native/build-linux.sh --install  # and update the release payload
#
# Needs git, python3, clang/clang++ (or CC/CXX) and preferably lld. Sources are
# cached under $SOCCERMOD_NATIVE_DEPS (default ~/.cache/soccermod-native).
#
# Metamod:Source must match the server's hook line: builds from 1461 load only
# KHook plugins (plugin API 18), so this builds against Metamod 2.0 master
# (KHook, drop 1469) together with the hl2sdk revision CounterStrikeSharp
# v1.0.375 uses for CS2 1.41.8.2.
set -Eeuo pipefail

metamod_commit=fa6f80e4662e5b96cc2e97722d812f374581dfd8
hl2sdk_commit=625bfd4e39816ad0e1eae3b3144a3b5425c5c7a0
ambuild_commit=01212cb57c96561f664b6dfb2ee10e66de6f81e4

here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$here/../../.." && pwd)
deps=${SOCCERMOD_NATIVE_DEPS:-${XDG_CACHE_HOME:-$HOME/.cache}/soccermod-native}
payload=$repo_root/deploy/release/payload/game/csgo/addons/soccermod_native/bin/linuxsteamrt64/soccermod_native.so
install=0
case ${1:-} in
    --install) install=1 ;;
    "") ;;
    *) echo "Usage: $0 [--install]" >&2; exit 2 ;;
esac

# Shallow checkout of one commit, reused while it stays the pinned one.
checkout() {
    local dir=$1 url=$2 commit=$3
    if [[ ! -d $dir/.git ]]; then
        git init -q "$dir"
        git -C "$dir" remote add origin "$url"
    fi
    if [[ $(git -C "$dir" rev-parse -q --verify 'HEAD^{commit}' 2>/dev/null || true) != "$commit" ]]; then
        git -C "$dir" fetch -q --depth 1 origin "$commit"
        git -C "$dir" checkout -q --force --detach FETCH_HEAD
    fi
}
checkout "$deps/metamod-source" https://github.com/alliedmodders/metamod-source "$metamod_commit"
git -C "$deps/metamod-source" submodule update -q --init --depth 1 --recursive
checkout "$deps/hl2sdk-root/hl2sdk-cs2" https://github.com/alliedmodders/hl2sdk "$hl2sdk_commit"
checkout "$deps/ambuild" https://github.com/alliedmodders/ambuild "$ambuild_commit"

build=$deps/build
rm -rf "$build"
mkdir -p "$build"
(
    cd "$build"
    export PYTHONPATH=$deps/ambuild${PYTHONPATH:+:$PYTHONPATH}
    CC=${CC:-clang} CXX=${CXX:-clang++} python3 "$here/configure.py" -s cs2 --targets=x86_64 --enable-optimize \
        --mms_path "$deps/metamod-source" --hl2sdk-root "$deps/hl2sdk-root" \
        --hl2sdk-manifests "$deps/metamod-source/hl2sdk-manifests" >/dev/null
    python3 -c 'from ambuild2 import run; run.cli_run()'
)

so=$build/package/cs2/addons/soccermod_native/bin/linuxsteamrt64/soccermod_native.so
strip=$(command -v llvm-strip || command -v strip)
"$strip" --strip-debug "$so"
echo "Built $so ($(sha256sum "$so" | cut -c1-12))"
if ((install)); then
    install -m 644 "$so" "$payload"
    echo "Installed into $payload"
fi
