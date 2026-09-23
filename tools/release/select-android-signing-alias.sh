#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: select-android-signing-alias.sh <keystore>" >&2
  exit 64
fi

keystore="$1"
: "${XUEQING_ANDROID_KEYSTORE_PASSWORD:?XUEQING_ANDROID_KEYSTORE_PASSWORD is required}"
: "${XUEQING_ANDROID_KEY_ALIAS:?XUEQING_ANDROID_KEY_ALIAS is required}"

if [[ ! -f "$keystore" ]]; then
  echo "Android signing keystore file is missing." >&2
  exit 65
fi

export LC_ALL=C

tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT

alias_details() {
  local alias_name="$1"
  keytool -list -v     -keystore "$keystore"     -storepass "$XUEQING_ANDROID_KEYSTORE_PASSWORD"     -alias "$alias_name"     >"$2" 2>/dev/null
}

entry_type_from() {
  awk -F': ' '/^Entry type: / { print $2; exit }' "$1"
}

certificate_sha256_from() {
  awk '
    /SHA256:/ {
      line=$0
      sub(/^.*SHA256:[[:space:]]*/, "", line)
      gsub(/:/, "", line)
      print tolower(line)
      exit
    }
  ' "$1"
}

configured="$XUEQING_ANDROID_KEY_ALIAS"
configured_file="$tmpdir/configured.txt"

if ! alias_details "$configured" "$configured_file"; then
  echo "Configured Android signing alias is not present in the keystore." >&2
  exit 66
fi

configured_type="$(entry_type_from "$configured_file")"
if [[ "$configured_type" == "PrivateKeyEntry" ]]; then
  printf '%s\n' "$configured"
  exit 0
fi

configured_sha="$(certificate_sha256_from "$configured_file")"
if [[ ! "$configured_sha" =~ ^[0-9a-f]{64}$ ]]; then
  echo "Configured Android signing alias is not a private key entry and has no usable certificate fingerprint." >&2
  exit 67
fi

all_file="$tmpdir/all.txt"
keytool -list -v   -keystore "$keystore"   -storepass "$XUEQING_ANDROID_KEYSTORE_PASSWORD"   >"$all_file" 2>/dev/null

mapfile -t private_aliases < <(
  awk -F': ' '
    /^Alias name: / { alias_name=$2 }
    /^Entry type: PrivateKeyEntry/ { print alias_name }
  ' "$all_file"
)

matches=()
for candidate in "${private_aliases[@]}"; do
  candidate_file="$tmpdir/candidate-${#matches[@]}.txt"
  if ! alias_details "$candidate" "$candidate_file"; then
    continue
  fi
  candidate_sha="$(certificate_sha256_from "$candidate_file")"
  if [[ "$candidate_sha" == "$configured_sha" ]]; then
    matches+=("$candidate")
  fi
done

case "${#matches[@]}" in
  1)
    printf '%s\n' "${matches[0]}"
    ;;
  0)
    echo "Configured Android signing certificate has no matching PrivateKeyEntry in the keystore." >&2
    exit 68
    ;;
  *)
    echo "Configured Android signing certificate matches multiple PrivateKeyEntry entries; refusing ambiguous selection." >&2
    exit 69
    ;;
esac
