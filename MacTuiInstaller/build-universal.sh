#!/usr/bin/env bash
# Build a macOS universal (x64 + arm64) single-file binary.
# Publishes both RIDs, fuses the single-file hosts with lipo, ad-hoc re-signs.
set -euo pipefail
cd "$(dirname "$0")"

CONFIG="${1:-Release}"
NAME="adofai-umm"
OUT="bin/universal"
PUB_X64="bin/_pub-x64"
PUB_ARM64="bin/_pub-arm64"

rm -rf "$OUT" "$PUB_X64" "$PUB_ARM64"
mkdir -p "$OUT"

echo "==> publish osx-x64"
dotnet publish -c "$CONFIG" -r osx-x64   --self-contained true -p:PublishSingleFile=true -o "$PUB_X64"

echo "==> publish osx-arm64"
dotnet publish -c "$CONFIG" -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o "$PUB_ARM64"

echo "==> lipo -> universal host"
lipo -create -output "$OUT/$NAME" "$PUB_X64/$NAME" "$PUB_ARM64/$NAME"

echo "==> ad-hoc codesign (lipo invalidates per-slice signatures)"
codesign --remove-signature "$OUT/$NAME" 2>/dev/null || true
codesign --force --sign - "$OUT/$NAME"

echo "==> verify"
file "$OUT/$NAME"
echo -n "archs: "; lipo -archs "$OUT/$NAME"
codesign --verify --verbose "$OUT/$NAME" 2>&1 | tail -1 || true

echo "==> smoke test"
host_arch="$(uname -m)"
if "$OUT/$NAME" --help >/dev/null 2>&1; then echo "native ($host_arch) slice: OK"; else echo "native ($host_arch) slice: FAILED"; fi
if [ "$host_arch" = "arm64" ] && /usr/bin/arch -x86_64 true 2>/dev/null; then
  if /usr/bin/arch -x86_64 "$OUT/$NAME" --help >/dev/null 2>&1; then echo "x64 slice (Rosetta): OK"; else echo "x64 slice (Rosetta): FAILED"; fi
else
  echo "x64 slice (Rosetta): skipped (no Rosetta or not on arm64)"
fi

echo
echo "Universal binary: $OUT/$NAME"
