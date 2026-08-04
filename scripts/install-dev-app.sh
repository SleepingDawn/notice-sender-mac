#!/bin/zsh
set -euo pipefail

ROOT="${0:A:h:h}"
"$ROOT/scripts/build-dev-app.sh"

SOURCE_APP="$ROOT/dist/TEST_공지.app"
INSTALLED_APP="/Applications/TEST_공지.app"

/usr/bin/pkill -x TEST_NoticeSender 2>/dev/null || true
/bin/rm -rf "$INSTALLED_APP"
/usr/bin/ditto "$SOURCE_APP" "$INSTALLED_APP"
/usr/bin/codesign --verify --deep --strict "$INSTALLED_APP"
/usr/bin/open "$INSTALLED_APP"

print -r -- "$INSTALLED_APP"
