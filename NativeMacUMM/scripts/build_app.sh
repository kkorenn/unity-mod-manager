#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
DIST_DIR="$ROOT_DIR/dist"
APP_NAME="Unity Mod Manager ADOFAI"
APP_PATH="$DIST_DIR/$APP_NAME.app"
EXECUTABLE_NAME="NativeMacUMM"

if [[ ! -d "$ROOT_DIR/Resources/Payload" ]]; then
  echo "Missing payload directory. Run scripts/prepare_payload_from_game.sh first." >&2
  exit 1
fi

swift build --package-path "$ROOT_DIR" -c release

BIN_PATH="$ROOT_DIR/.build/release/$EXECUTABLE_NAME"
if [[ ! -f "$BIN_PATH" ]]; then
  echo "Missing compiled binary: $BIN_PATH" >&2
  exit 1
fi

rm -rf "$APP_PATH"
mkdir -p "$APP_PATH/Contents/MacOS" "$APP_PATH/Contents/Resources"

cp "$BIN_PATH" "$APP_PATH/Contents/MacOS/$EXECUTABLE_NAME"
chmod +x "$APP_PATH/Contents/MacOS/$EXECUTABLE_NAME"
cp -R "$ROOT_DIR/Resources/Payload" "$APP_PATH/Contents/Resources/Payload"

cat > "$APP_PATH/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>com.koren.nativeumm.adofai</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>$EXECUTABLE_NAME</string>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.utilities</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
PLIST

# Ad-hoc sign for smoother local launching.
codesign --force --deep --sign - "$APP_PATH" >/dev/null 2>&1 || true

echo "Built app: $APP_PATH"
