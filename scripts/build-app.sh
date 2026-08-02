#!/bin/zsh
set -euo pipefail

ROOT="${0:A:h:h}"
cd "$ROOT"
swift build -c release

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
