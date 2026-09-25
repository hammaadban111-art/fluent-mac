#!/bin/bash
# Builds the drag-into-Applications disk image, Fluent-<version>.dmg, around a Fluent.app made by
# build-app.sh. Needs dmgbuild (pip3 install dmgbuild); it writes the window layout without Finder,
# so it also works on a locked or headless Mac.
#   mac/scripts/build-dmg.sh [version]        (expects $OUT/Fluent.app, default mac/dist)
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:-$(sed -n 's/^RELEASE //p' RELEASE)}"
OUT="${OUT:-$PWD/dist}"
APP="$OUT/Fluent.app"
DMG="$OUT/Fluent-$VERSION.dmg"
[ -d "$APP" ] || { echo "No $APP, run scripts/build-app.sh first" >&2; exit 1; }
rm -f "$DMG"
dmgbuild -s dmg/settings.py -D app="$APP" -D dmgdir="$PWD/dmg" "Fluent" "$DMG"
hdiutil verify "$DMG"
echo "Built $DMG ($(du -h "$DMG" | cut -f1))"
