#!/bin/zsh
set -euo pipefail

IDENTITY_NAME="NoticeSender Local Code Signing"
LOGIN_KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"

find_identity() {
  /usr/bin/security find-identity -v -p codesigning "$LOGIN_KEYCHAIN" 2>/dev/null |
    /usr/bin/awk -v name="$IDENTITY_NAME" 'index($0, "\"" name "\"") { print $2; exit }'
}

if EXISTING_IDENTITY="$(find_identity)" && [[ -n "$EXISTING_IDENTITY" ]]; then
  print -r -- "$EXISTING_IDENTITY"
  exit 0
fi

TEMP_DIR="$(/usr/bin/mktemp -d "${TMPDIR:-/tmp}/notice-sender-signing.XXXXXX")"
trap '/bin/rm -rf "$TEMP_DIR"' EXIT

CERTIFICATE="$TEMP_DIR/notice-sender-local.cer"
PRIVATE_KEY="$TEMP_DIR/notice-sender-local.key"
IDENTITY_P12="$TEMP_DIR/notice-sender-local.p12"
IMPORT_PASSWORD="notice-sender-local-import"

/usr/bin/openssl req \
  -x509 \
  -newkey rsa:2048 \
  -sha256 \
  -days 3650 \
  -nodes \
  -subj "/CN=$IDENTITY_NAME/O=NoticeSender Local Development" \
  -addext "keyUsage=critical,digitalSignature" \
  -addext "extendedKeyUsage=codeSigning" \
  -keyout "$PRIVATE_KEY" \
  -out "$CERTIFICATE"

/usr/bin/openssl pkcs12 \
  -export \
  -inkey "$PRIVATE_KEY" \
  -in "$CERTIFICATE" \
  -name "$IDENTITY_NAME" \
  -passout "pass:$IMPORT_PASSWORD" \
  -out "$IDENTITY_P12"

# Only /usr/bin/codesign is granted private-key access.
/usr/bin/security import "$IDENTITY_P12" \
  -k "$LOGIN_KEYCHAIN" \
  -P "$IMPORT_PASSWORD" \
  -T /usr/bin/codesign >/dev/null

# A user-local trust root makes the self-signed identity valid for code signing.
/usr/bin/security add-trusted-cert \
  -r trustRoot \
  -p codeSign \
  -k "$LOGIN_KEYCHAIN" \
  "$CERTIFICATE"

NEW_IDENTITY="$(find_identity)"
if [[ -z "$NEW_IDENTITY" ]]; then
  print -u2 -- "로컬 코드 서명 인증서를 만들었지만 유효한 서명 ID를 찾지 못했습니다."
  exit 1
fi

print -r -- "$NEW_IDENTITY"
