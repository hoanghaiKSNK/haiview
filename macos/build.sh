#!/bin/zsh
# Build HaiView for macOS and package it.
#   ./build.sh            build + package into ../dist
#   ./build.sh --install  also copy to ~/Applications
set -euo pipefail
cd "$(dirname "$0")"
VERSION=$(/usr/libexec/PlistBuddy -c "Print CFBundleShortVersionString" Support/Info.plist)
# Build each architecture, then merge into one universal binary (Apple Silicon + Intel).
swift build -c release --triple arm64-apple-macosx15.0
swift build -c release --triple x86_64-apple-macosx15.0
ARM=$(swift build -c release --triple arm64-apple-macosx15.0 --show-bin-path)/HaiView
X86=$(swift build -c release --triple x86_64-apple-macosx15.0 --show-bin-path)/HaiView
mkdir -p .build/universal
lipo -create "$ARM" "$X86" -output .build/universal/HaiView
BIN=.build/universal/HaiView
APP="build/HaiView.app"
rm -rf build && mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/HaiView"
cp Support/Info.plist "$APP/Contents/"
cp Support/AppIcon.icns "$APP/Contents/Resources/"
codesign --force --deep --sign - "$APP"
mkdir -p ../dist
ZIP="../dist/HaiView-${VERSION}-macOS.zip"
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"
echo "Packaged: $ZIP"
if [[ "${1:-}" == "--install" ]]; then
  rm -rf ~/Applications/HaiView.app && cp -R "$APP" ~/Applications/
  /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f ~/Applications/HaiView.app
  echo "Installed: ~/Applications/HaiView.app"
fi
