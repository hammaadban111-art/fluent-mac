#!/bin/bash
# Builds Fluent.app for Apple Silicon and Intel (one universal binary), ad-hoc signs it and zips
# it as Fluent-mac.zip. Run on a Mac with Xcode:  mac/scripts/build-app.sh [version] [build]
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:-1.0.0}"
BUILD="${2:-1}"
OUT="${OUT:-$PWD/dist}"

ARCHS=(--arch arm64 --arch x86_64)
swift build -c release "${ARCHS[@]}" --product Fluent
BIN="$(swift build -c release "${ARCHS[@]}" --show-bin-path)/Fluent"

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

(cd "$OUT" && ditto -c -k --sequesterRsrc --keepParent Fluent.app Fluent-mac.zip)
echo "Built $APP ($VERSION build $BUILD) and $OUT/Fluent-mac.zip"
