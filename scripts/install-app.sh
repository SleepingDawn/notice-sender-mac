#!/bin/zsh
set -euo pipefail

ROOT="${0:A:h:h}"
"$ROOT/scripts/build-app.sh"

SOURCE_APP="$ROOT/dist/공지발송.app"
INSTALLED_APP="/Applications/공지발송.app"

# Replace only this app at its stable installation path.
/usr/bin/pkill -x NoticeSender 2>/dev/null || true
/bin/rm -rf "$INSTALLED_APP"
/usr/bin/ditto "$SOURCE_APP" "$INSTALLED_APP"
/usr/bin/codesign --verify --deep --strict "$INSTALLED_APP"
/usr/bin/open "$INSTALLED_APP"

print -r -- "$INSTALLED_APP"
