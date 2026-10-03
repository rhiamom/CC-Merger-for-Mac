#!/bin/bash
###############################################################################
#  Build build/AppIcon.icns from build/make-icon.swift (three package boxes merging
#  into one big box). The Windows CCMerger had no icon of its own.
#
#  Usage:  build/make-icon.sh
###############################################################################
set -euo pipefail
cd "$(dirname "$0")"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
swift make-icon.swift "$TMP/src.png"
SET="$TMP/AppIcon.iconset"; mkdir "$SET"
for s in 16 32 128 256 512; do
    sips -z $s $s "$TMP/src.png" --out "$SET/icon_${s}x${s}.png" >/dev/null
    d=$((s * 2))
    sips -z $d $d "$TMP/src.png" --out "$SET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$SET" -o AppIcon.icns
echo "Wrote $(pwd)/AppIcon.icns"
