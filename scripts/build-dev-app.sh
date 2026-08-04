#!/bin/zsh
set -euo pipefail

ROOT="${0:A:h:h}"
cd "$ROOT"

SWIFTPM_MODULECACHE_OVERRIDE="${SWIFTPM_MODULECACHE_OVERRIDE:-$ROOT/.build/dev-module-cache}" \
  swift build -c debug --disable-sandbox

APP="$ROOT/dist/TEST_공지.app"
EXECUTABLE="TEST_NoticeSender"
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
