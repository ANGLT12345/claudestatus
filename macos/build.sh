#!/bin/bash
# Builds a universal (Apple Silicon + Intel) ClaudeUsageBar.app and a zip of it in macos/build/.
# Usage: ./build.sh [version]
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' ../ClaudeUsageBar.csproj)}"
APP=build/ClaudeUsageBar.app

swift build -c release --arch arm64 --arch x86_64
BIN_DIR=$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)

rm -rf build
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/ClaudeUsageBar" "$APP/Contents/MacOS/ClaudeUsageBar"
sed "s/__VERSION__/$VERSION/g" Resources/Info.plist > "$APP/Contents/Info.plist"
iconutil -c icns Resources/AppIcon.iconset -o "$APP/Contents/Resources/AppIcon.icns"

# Ad-hoc signature (no paid Apple Developer ID): required to run on Apple Silicon.
codesign --force --deep --sign - "$APP"

(cd build && ditto -c -k --keepParent ClaudeUsageBar.app ClaudeUsageBar-macOS.zip)
echo "Built $APP (version $VERSION) and build/ClaudeUsageBar-macOS.zip"
