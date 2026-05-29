#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
PAYLOAD_DIR="$ROOT_DIR/Resources/Payload"
GAME_ROOT="${1:-$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice}"

MANAGED_DIR="$GAME_ROOT/ADanceOfFireAndIce.app/Contents/Resources/Data/Managed"
UMM_DIR="$MANAGED_DIR/UnityModManager"
MACQOL_DIR="$GAME_ROOT/Mods/MacQOL"

require_file() {
  if [[ ! -e "$1" ]]; then
    echo "Missing required source: $1" >&2
    exit 1
  fi
}

require_file "$MANAGED_DIR/UnityEngine.CoreModule.dll"
require_file "$UMM_DIR/UnityModManager.dll"
require_file "$UMM_DIR/0Harmony.dll"
require_file "$UMM_DIR/dnlib.dll"
require_file "$MACQOL_DIR/Info.json"
require_file "$MACQOL_DIR/MacQOL.dll"

rm -rf "$PAYLOAD_DIR"
mkdir -p "$PAYLOAD_DIR/Managed/UnityModManager" "$PAYLOAD_DIR/Mods"

cp "$MANAGED_DIR/UnityEngine.CoreModule.dll" "$PAYLOAD_DIR/Managed/"
cp "$UMM_DIR/UnityModManager.dll" "$PAYLOAD_DIR/Managed/UnityModManager/"
cp "$UMM_DIR/0Harmony.dll" "$PAYLOAD_DIR/Managed/UnityModManager/"
cp "$UMM_DIR/dnlib.dll" "$PAYLOAD_DIR/Managed/UnityModManager/"

if [[ -f "$UMM_DIR/UnityModManager.xml" ]]; then
  cp "$UMM_DIR/UnityModManager.xml" "$PAYLOAD_DIR/Managed/UnityModManager/"
fi

cp -R "$MACQOL_DIR" "$PAYLOAD_DIR/Mods/"
find "$PAYLOAD_DIR" -name '*.cache' -delete

cat > "$PAYLOAD_DIR/README.txt" <<PAYLOAD
This payload is bundled into NativeMacUMM.
Supported game: A Dance of Fire and Ice (Steam, macOS).
Generated from: $GAME_ROOT
Generated at: $(date)
PAYLOAD

echo "Payload prepared at: $PAYLOAD_DIR"
