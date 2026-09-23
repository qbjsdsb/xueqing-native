#!/usr/bin/env bash
set -euo pipefail

repo="${1:-$(pwd)}"
out="${2:-$repo/artifacts/release-dry-run/android}"
props="$repo/release/version.properties"

get_prop() {
  sed -n "s/^$1=//p" "$props"
}

release_version="$(get_prop releaseVersion)"
expected_code="$(get_prop androidVersionCode)"
expected_name="$(get_prop androidVersionName)"

cd "$repo/apps/android"
./gradlew --no-daemon :app:assembleRelease

apk="$repo/apps/android/app/build/outputs/apk/release/app-release-unsigned.apk"
if [[ ! -f "$apk" ]]; then
  apk="$repo/apps/android/app/build/outputs/apk/release/app-release.apk"
fi
test -f "$apk"

mkdir -p "$out"
dest="$out/Xueqing-$release_version-unsigned.apk"
cp "$apk" "$dest"

apkanalyzer="$ANDROID_HOME/cmdline-tools/latest/bin/apkanalyzer"
if [[ -x "$apkanalyzer" ]]; then
  actual_code="$("$apkanalyzer" manifest version-code "$dest")"
  actual_name="$("$apkanalyzer" manifest version-name "$dest")"
  [[ "$actual_code" == "$expected_code" ]]
  [[ "$actual_name" == "$expected_name" ]]
fi

sha256sum "$dest" | sed "s#  .*/#  #" > "$out/SHA256SUMS-android.txt"
echo "Production-shaped unsigned APK ready: $dest"
