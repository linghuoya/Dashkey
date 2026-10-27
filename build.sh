#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"

APP_NAME="DashKey"
OUT="Build/${APP_NAME}.app"

rm -rf Build
mkdir -p "${OUT}/Contents/MacOS"

echo "==> 编译"
swiftc -O \
    -o "${OUT}/Contents/MacOS/${APP_NAME}" \
    Sources/main.swift \
    -framework Cocoa \
    -framework Carbon \
    -framework ServiceManagement

echo "==> 组装 bundle"
cp Info.plist "${OUT}/Contents/Info.plist"
mkdir -p "${OUT}/Contents/Resources"
cp Assets/AppIcon.icns "${OUT}/Contents/Resources/AppIcon.icns"
cp Assets/StatusIcon.png Assets/StatusIcon@2x.png "${OUT}/Contents/Resources/"

echo "==> 签名"
if security find-identity -v -p codesigning 2>/dev/null | grep -qi "DashKey"; then
    codesign --force --sign "DashKey" "${OUT}" \
        && echo "使用 DashKey 证书签名" \
        || codesign --force --sign - "${OUT}"
else
    codesign --force --sign - "${OUT}" \
        && echo "使用 ad-hoc 签名（未找到 DashKey 证书；可在『钥匙串访问』建一个『代码签名』自签证书命名为 DashKey，下次构建自动启用）"
fi

echo "==> 完成: $(pwd)/${OUT}"
