#!/usr/bin/env bash
# One Workshop item per map (owner 2026-10-02: "every map item shall contain all features"): the item of a
# map = the feature base + the map's own package, so a server needs only the item of the map it runs.
#
#   build-map-item.sh base                          (re)builds the feature base
#   build-map-item.sh item <map-only.vpk> <out.vpk>  feature base + one map package (the map's files win)
#
# Feature base = the Feature Package (everything the plugin shows and plays: models, materials, sounds,
# particles, panorama) + the brand boards + the number-key menu's three client files. No map in it.
# Paths are the VPS's (override with the variables). vpk_tool.py: tools/arena/vpk_tool.py.
set -Eeuo pipefail
TOOL=${VPK_TOOL:-/root/arena-plugin/vpk_tool.py}
FEATURES=${FEATURE_PACKAGE:-/home/gameserver/arena-upload/3797479770_dir.vpk}
BRANDS=${BRANDS_DIR:-/root/arena-plugin/brands-pack}
KEYMENU=${KEYMENU_DIR:-/root/keymenu-plugin/addon}
BASE=${FEATURE_BASE:-/root/arena-plugin/feature-base.vpk}
case "${1:-}" in
  base)
    tmp=$(mktemp -d)
    python3 "$TOOL" merge "$tmp/a.vpk" "$FEATURES" "$BRANDS" | tail -1
    python3 "$TOOL" merge "$BASE" "$tmp/a.vpk" "$KEYMENU" | tail -1
    rm -f "$tmp/a.vpk"; rmdir "$tmp"
    if python3 "$TOOL" list "$BASE" | grep -q '^maps/'; then echo "the feature base contains a map - it must not" >&2; exit 1; fi
    ;;
  item)
    [[ -f ${2:-} && -n ${3:-} ]] || { echo "usage: build-map-item.sh item <map-only.vpk> <out.vpk>" >&2; exit 2; }
    [[ -f $BASE ]] || { echo "no feature base ($BASE): run 'build-map-item.sh base' first" >&2; exit 1; }
    python3 "$TOOL" merge "$3" "$BASE" "$2" | tail -1
    maps=$(python3 "$TOOL" list "$3" | grep -c '^maps/.*\.vpk ' || true)
    [[ $maps == 1 ]] || { echo "the item holds $maps maps - it must hold exactly one" >&2; exit 1; }
    python3 "$TOOL" list "$3" | grep '^maps/'
    ;;
  *) sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'; exit 2 ;;
esac
