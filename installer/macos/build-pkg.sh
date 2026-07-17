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
APP_DIR="$PAYLOAD_DIR/Applications/Codex-Skin.app"

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

test ! -d "$PUBLISH_DIR/themes"
test ! -d "$PUBLISH_DIR/previews"

cp -R "$PUBLISH_DIR/." "$APP_DIR/Contents/MacOS/"
ICONSET="$BUILD_ROOT/Codex-Skin.iconset"
mkdir -p "$ICONSET"
sips -z 16 16 "$ROOT/docs/images/icon.png" --out "$ICONSET/icon_16x16.png" >/dev/null
sips -z 32 32 "$ROOT/docs/images/icon.png" --out "$ICONSET/icon_16x16@2x.png" >/dev/null
sips -z 32 32 "$ROOT/docs/images/icon.png" --out "$ICONSET/icon_32x32.png" >/dev/null
sips -z 64 64 "$ROOT/docs/images/icon.png" --out "$ICONSET/icon_32x32@2x.png" >/dev/null
sips -z 128 128 "$ROOT/docs/images/icon.png" --out "$ICONSET/icon_128x128.png" >/dev/null
sips -z 256 256 "$ROOT/docs/images/icon.png" --out "$ICONSET/icon_128x128@2x.png" >/dev/null
sips -z 256 256 "$ROOT/docs/images/icon.png" --out "$ICONSET/icon_256x256.png" >/dev/null
sips -z 512 512 "$ROOT/docs/images/icon.png" --out "$ICONSET/icon_256x256@2x.png" >/dev/null
sips -z 512 512 "$ROOT/docs/images/icon.png" --out "$ICONSET/icon_512x512.png" >/dev/null
sips -z 1024 1024 "$ROOT/docs/images/icon.png" --out "$ICONSET/icon_512x512@2x.png" >/dev/null
iconutil -c icns "$ICONSET" -o "$APP_DIR/Contents/Resources/Codex-Skin.icns"
sed "s/__VERSION__/$VERSION/g" "$ROOT/installer/macos/Info.plist" > "$APP_DIR/Contents/Info.plist"
chmod 0755 "$APP_DIR/Contents/MacOS/Codex-Skin"
plutil -lint "$APP_DIR/Contents/Info.plist"
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleURLTypes:0:CFBundleURLSchemes:0' "$APP_DIR/Contents/Info.plist")" = "dreamskin"
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleDocumentTypes:0:CFBundleTypeExtensions:0' "$APP_DIR/Contents/Info.plist")" = "dreamskin"
test -s "$APP_DIR/Contents/Resources/Codex-Skin.icns"
find "$PAYLOAD_DIR" -name '*.pdb' -delete

pkgbuild \
  --root "$PAYLOAD_DIR" \
  --identifier "com.codexskin.themestore" \
  --version "$VERSION" \
  --install-location / \
  "$OUTPUT"
