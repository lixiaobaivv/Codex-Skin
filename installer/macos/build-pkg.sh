#!/bin/bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: build-pkg.sh <osx-arm64|osx-x64> <version> <output.pkg>" >&2
  exit 2
fi

RID="$1"
VERSION="$2"
OUTPUT_DIRECTORY="$(dirname "$3")"
mkdir -p "$OUTPUT_DIRECTORY"
OUTPUT="$(cd "$OUTPUT_DIRECTORY" && pwd)/$(basename "$3")"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BUILD_ROOT="${RUNNER_TEMP:-/tmp}/codex-theme-store-${RID}"
PUBLISH_DIR="$BUILD_ROOT/publish"
PAYLOAD_DIR="$BUILD_ROOT/payload"
APP_DIR="$PAYLOAD_DIR/Applications/Codex Theme Store.app"

case "$RID" in
  osx-arm64|osx-x64) ;;
  *) echo "Unsupported RID: $RID" >&2; exit 2 ;;
esac

rm -rf "$BUILD_ROOT"
mkdir -p "$PUBLISH_DIR" "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

dotnet publish "$ROOT/src/CodexThemeStore.Desktop/CodexThemeStore.Desktop.csproj" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:Version="$VERSION" \
  --output "$PUBLISH_DIR"

for theme_id in dilraba-star enfp-pop jackson-sage kun-stage; do
  test -f "$PUBLISH_DIR/themes/$theme_id.json"
  test -f "$PUBLISH_DIR/previews/$theme_id.png"
done

cp -R "$PUBLISH_DIR/." "$APP_DIR/Contents/MacOS/"
sed "s/__VERSION__/$VERSION/g" "$ROOT/installer/macos/Info.plist" > "$APP_DIR/Contents/Info.plist"
chmod 0755 "$APP_DIR/Contents/MacOS/CodexThemeStore.Desktop"
find "$PAYLOAD_DIR" -name '*.pdb' -delete

pkgbuild \
  --root "$PAYLOAD_DIR" \
  --identifier "com.codexskin.themestore" \
  --version "$VERSION" \
  --install-location / \
  "$OUTPUT"
