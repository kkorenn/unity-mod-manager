#!/usr/bin/env bash
# Full release build: native C# core -> Xcode app (Release) -> .dmg.
# Outputs to dist/. Usage: ./build-app.sh   (or ./build-app.sh Debug)
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$(pwd)"
CONFIG="${1:-Release}"
APP="UnityModManagerMac"
DIST="$ROOT/dist"
DD="$ROOT/build/dd"

echo "==> 1/4 native core (libNativeUmm.a + payload + xcconfig)"
./build-native.sh >/dev/null
echo "    ok"

echo "==> 2/4 xcodebuild $CONFIG"
rm -rf "$DD"
xcodebuild -project "$APP.xcodeproj" -scheme "$APP" -configuration "$CONFIG" \
  -derivedDataPath "$DD" \
  CODE_SIGN_IDENTITY="-" CODE_SIGN_STYLE=Manual CODE_SIGNING_REQUIRED=NO DEVELOPMENT_TEAM="" \
  build 2>&1 | grep -iE "error:|BUILD SUCCEEDED|BUILD FAILED" || true

SRC_APP="$DD/Build/Products/$CONFIG/$APP.app"
[ -d "$SRC_APP" ] || { echo "ERROR: $SRC_APP not produced"; exit 1; }

echo "==> 3/4 stage app"
rm -rf "$DIST"; mkdir -p "$DIST"
cp -R "$SRC_APP" "$DIST/$APP.app"
codesign --force --deep --sign - "$DIST/$APP.app"

echo "==> 4/4 build dmg"
STAGE="$DIST/.stage"
rm -rf "$STAGE"; mkdir -p "$STAGE"
cp -R "$DIST/$APP.app" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "$APP" -srcfolder "$STAGE" -ov -format UDZO "$DIST/$APP.dmg" >/dev/null
rm -rf "$STAGE"

echo
echo "App: $DIST/$APP.app"
echo "DMG: $DIST/$APP.dmg"
