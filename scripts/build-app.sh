#!/bin/zsh
set -euo pipefail

ROOT="${0:A:h:h}"
cd "$ROOT"

SWIFT_COMPATIBILITY_FLAGS=()
SWIFT_VERSION="$(/usr/bin/swift --version 2>/dev/null | /usr/bin/head -n 1)"
SDK_SWIFT_INTERFACE="$(/usr/bin/xcrun --sdk macosx --show-sdk-path)/usr/lib/swift/Swift.swiftmodule/arm64e-apple-macos.swiftinterface"
if [[ "$SWIFT_VERSION" == *"swiftlang-6.3.3"* ]] \
    && [[ -f "$SDK_SWIFT_INTERFACE" ]] \
    && /usr/bin/grep -q "swiftlang-6.3.2" "$SDK_SWIFT_INTERFACE"; then
  # CLT 26.6 can temporarily pair Swift 6.3.3 with a 6.3.2 macOS SDK.
  SWIFT_COMPATIBILITY_FLAGS=(-Xswiftc -Xfrontend -Xswiftc -disable-deserialization-safety)
fi

SWIFTPM_MODULECACHE_OVERRIDE="${SWIFTPM_MODULECACHE_OVERRIDE:-$ROOT/.build/release-module-cache}" \
CLANG_MODULE_CACHE_PATH="${CLANG_MODULE_CACHE_PATH:-$ROOT/.build/release-clang-module-cache}" \
  swift build -c release "${SWIFT_COMPATIBILITY_FLAGS[@]}"

APP="$ROOT/dist/공지발송.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/ThirdPartyLicenses"
cp "$ROOT/.build/release/NoticeSender" "$APP/Contents/MacOS/NoticeSender"
cp "$ROOT/Resources/Info.plist" "$APP/Contents/Info.plist"
cp "$ROOT/Vendor/kmsg/LICENSE" "$APP/Contents/Resources/ThirdPartyLicenses/kmsg-LICENSE"
cp "$ROOT/Vendor/kmsg/PINNED_COMMIT" "$APP/Contents/Resources/ThirdPartyLicenses/kmsg-PINNED_COMMIT"
# A persistent local signing identity lets macOS TCC recognize rebuilt versions
# as the same app. Ad-hoc signatures are tied to one specific code version.
IDENTITY_SHA="$("$ROOT/scripts/ensure-local-signing-identity.sh")"
codesign --force --deep --sign "$IDENTITY_SHA" \
  --requirements "=designated => certificate leaf = H\"$IDENTITY_SHA\" and identifier \"kr.onesolution.NoticeSender\"" \
  "$APP"
echo "$APP"
