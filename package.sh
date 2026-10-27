#!/bin/bash
# 打包可发布的安装包：分别编译 Apple Silicon (arm64) 与 Intel (x86_64)，打成 zip。
set -euo pipefail
cd "$(dirname "$0")"

APP_NAME="DashKey"
VERSION=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" Info.plist)
OUT_DIR="Build"
ARCHS=(arm64 x86_64)
mkdir -p "$OUT_DIR"

sign_app() {
    local app="$1"
    if security find-identity -v -p codesigning 2>/dev/null | grep -qi "DashKey"; then
        codesign --force --sign "DashKey" "$app" >/dev/null 2>&1 || codesign --force --sign - "$app"
    else
        codesign --force --sign - "$app"
    fi
}

for ARCH in "${ARCHS[@]}"; do
    echo "==> 编译 ${ARCH}（macOS 13.0+）"
    APP="${OUT_DIR}/${APP_NAME}-${ARCH}.app"
    rm -rf "$APP"
    mkdir -p "${APP}/Contents/MacOS"
    swiftc -O -target "${ARCH}-apple-macos13.0" \
        -o "${APP}/Contents/MacOS/${APP_NAME}" \
        Sources/main.swift \
        -framework Cocoa \
        -framework Carbon \
        -framework ServiceManagement
    cp Info.plist "${APP}/Contents/Info.plist"
    mkdir -p "${APP}/Contents/Resources"
    cp Assets/AppIcon.icns "${APP}/Contents/Resources/AppIcon.icns"
    cp Assets/StatusIcon.png Assets/StatusIcon@2x.png "${APP}/Contents/Resources/"
    sign_app "$APP"
    echo "    ✓ $(lipo -archs "${APP}/Contents/MacOS/${APP_NAME}")  $(du -sh "$APP" | cut -f1)"
done

echo "==> 压缩"
for ARCH in "${ARCHS[@]}"; do
    APP="${OUT_DIR}/${APP_NAME}-${ARCH}.app"
    ZIP="${OUT_DIR}/${APP_NAME}-v${VERSION}-${ARCH}.zip"
    rm -f "$ZIP"
    ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
    echo "    ✓ ${ZIP}"
done

echo "==> 完成"
ls -lh "${OUT_DIR}"/*.zip
