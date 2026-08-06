#!/bin/zsh
set -euo pipefail

ROOT="${0:A:h:h}"
cd "$ROOT"

APP="$ROOT/dist/TEST_공지.app"
INSTALLED_APP="/Applications/TEST_공지.app"
EXECUTABLE="TEST_NoticeSender"

# Keep just one test app available. A stale installed copy can otherwise be
# opened by mistake after this build produces a newer app in dist.
/usr/bin/pkill -x "$EXECUTABLE" 2>/dev/null || true
/bin/rm -rf "$INSTALLED_APP"

SWIFT_COMPATIBILITY_FLAGS=()
SWIFT_VERSION="$(/usr/bin/swift --version 2>/dev/null | /usr/bin/head -n 1)"
SDK_SWIFT_INTERFACE="$(/usr/bin/xcrun --sdk macosx --show-sdk-path)/usr/lib/swift/Swift.swiftmodule/arm64e-apple-macos.swiftinterface"
if [[ "$SWIFT_VERSION" == *"swiftlang-6.3.3"* ]] \
    && [[ -f "$SDK_SWIFT_INTERFACE" ]] \
    && /usr/bin/grep -q "swiftlang-6.3.2" "$SDK_SWIFT_INTERFACE"; then
  # CLT 26.6 can temporarily pair Swift 6.3.3 with a 6.3.2 macOS SDK.
  SWIFT_COMPATIBILITY_FLAGS=(-Xswiftc -Xfrontend -Xswiftc -disable-deserialization-safety)
fi

SWIFTPM_MODULECACHE_OVERRIDE="${SWIFTPM_MODULECACHE_OVERRIDE:-$ROOT/.build/dev-module-cache}" \
CLANG_MODULE_CACHE_PATH="${CLANG_MODULE_CACHE_PATH:-$ROOT/.build/dev-clang-module-cache}" \
  swift build -c debug --disable-sandbox "${SWIFT_COMPATIBILITY_FLAGS[@]}"

BUNDLE_ID="kr.onesolution.NoticeSender.dev"
DEV_VERSION="$(/usr/bin/tr -d '[:space:]' < "$ROOT/Resources/DevVersion.txt")"
/bin/rm -rf "$APP"
/bin/mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/ThirdPartyLicenses"
/bin/cp "$ROOT/.build/debug/NoticeSender" "$APP/Contents/MacOS/$EXECUTABLE"
/bin/cp "$ROOT/Resources/Info.plist" "$APP/Contents/Info.plist"
/bin/cp "$ROOT/Vendor/kmsg/LICENSE" "$APP/Contents/Resources/ThirdPartyLicenses/kmsg-LICENSE"
/bin/cp "$ROOT/Vendor/kmsg/PINNED_COMMIT" "$APP/Contents/Resources/ThirdPartyLicenses/kmsg-PINNED_COMMIT"

PLIST="$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleExecutable $EXECUTABLE" "$PLIST"
/usr/libexec/PlistBuddy -c "Set :CFBundleIdentifier $BUNDLE_ID" "$PLIST"
/usr/libexec/PlistBuddy -c "Set :CFBundleName TEST_공지" "$PLIST"
/usr/libexec/PlistBuddy -c "Set :CFBundleDisplayName TEST_공지" "$PLIST"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $DEV_VERSION" "$PLIST"
/usr/libexec/PlistBuddy -c "Add :NoticeSenderDataDirectoryName string NoticeSenderDev" "$PLIST"

# Keep development builds independent from the production app's TCC identity.
IDENTITY_SHA="$("$ROOT/scripts/ensure-local-signing-identity.sh")"
/usr/bin/codesign --force --deep --sign "$IDENTITY_SHA" \
  --requirements "=designated => certificate leaf = H\"$IDENTITY_SHA\" and identifier \"$BUNDLE_ID\"" \
  "$APP"

print -r -- "$APP"
