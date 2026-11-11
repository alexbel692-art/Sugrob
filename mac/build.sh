#!/bin/bash
# Сборка Sugrob.app (универсальный: Apple Silicon + Intel). Нужен Xcode Command Line Tools.
set -euo pipefail
cd "$(dirname "$0")"
rm -rf build && mkdir -p build
for arch in arm64 x86_64; do
  swiftc -O -swift-version 5 -target ${arch}-apple-macos13.0 \
    -framework AppKit -framework SwiftUI -framework Carbon -framework ServiceManagement \
    Sources/*.swift -o build/Sugrob-${arch}
done
APP=build/Sugrob.app
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
lipo -create build/Sugrob-arm64 build/Sugrob-x86_64 -output "$APP/Contents/MacOS/Sugrob"
cp Info.plist "$APP/Contents/Info.plist"
cp AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
codesign --force --deep --sign - "$APP"
(cd build && ditto -c -k --keepParent Sugrob.app Sugrob-mac.zip)
echo "OK: mac/build/Sugrob.app"
