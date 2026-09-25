#!/bin/bash
# Builds Fluent.app for Apple Silicon and Intel (one universal binary), ad-hoc signs it and zips
# it as Fluent-mac.zip. Run on a Mac with Xcode or the Command Line Tools:  mac/scripts/build-app.sh [version] [build]
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:-1.0.0}"
BUILD="${2:-1}"
OUT="${OUT:-$PWD/dist}"

# One build per architecture, joined with lipo. (`--arch a --arch b` needs Xcode's xcbuild;
# this way the Command Line Tools alone are enough.)
BINS=()
for ARCH in arm64 x86_64; do
    swift build -c release --triple "$ARCH-apple-macosx14.0" --product Fluent
    BINS+=("$(swift build -c release --triple "$ARCH-apple-macosx14.0" --show-bin-path)/Fluent")
done
BIN="$OUT/Fluent-universal"
mkdir -p "$OUT"
lipo -create "${BINS[@]}" -output "$BIN"

APP="$OUT/Fluent.app"
rm -rf "$APP" "$OUT/Fluent-mac.zip"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Fluent"
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
sed -e "s/__VERSION__/$VERSION/" -e "s/__BUILD__/$BUILD/" Resources/Info.plist > "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"
plutil -lint "$APP/Contents/Info.plist"

# No paid Apple Developer account: ad-hoc signature, no notarization.
codesign --force --deep -s - --identifier com.hammaad.fluent.mac "$APP"
codesign --verify --verbose=2 "$APP"
echo "Architectures: $(lipo -archs "$APP/Contents/MacOS/Fluent")"
rm -f "$BIN"

(cd "$OUT" && ditto -c -k --sequesterRsrc --keepParent Fluent.app Fluent-mac.zip)
echo "Built $APP ($VERSION build $BUILD) and $OUT/Fluent-mac.zip"
