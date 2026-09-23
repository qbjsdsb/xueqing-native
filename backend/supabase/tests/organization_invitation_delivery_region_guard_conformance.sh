#!/usr/bin/env bash
set -euo pipefail

status_env="$(mktemp)"
function_log="$(mktemp)"
function_env="$(mktemp)"
function_pid=""

cleanup() {
  set +e
  if [[ -n "$function_pid" ]]; then
    kill "$function_pid" >/dev/null 2>&1 || true
    wait "$function_pid" >/dev/null 2>&1 || true
  fi
  rm -f "$status_env" "$function_log" "$function_env"
}
trap cleanup EXIT

supabase status --workdir backend -o env > "$status_env"
set -a
# shellcheck disable=SC1090
source "$status_env"
set +a

: "${API_URL:?API_URL missing from local Supabase status}"
: "${ANON_KEY:?ANON_KEY missing from local Supabase status}"

echo "::add-mask::$ANON_KEY"

cat > "$function_env" <<'EOF'
XUEQING_REQUIRED_EDGE_REGION=ap-southeast-1
XUEQING_INVITATION_REDIRECT_URL=http://127.0.0.1:3000/invitation
EOF

supabase functions serve organization-invitation-delivery \
  --workdir backend \
  --env-file "$function_env" \
  >"$function_log" 2>&1 &
function_pid="$!"

function_url="${API_URL%/}/functions/v1/organization-invitation-delivery"
ready="false"
for _ in $(seq 1 60); do
  status="$(
    curl --silent --output /dev/null --write-out '%{http_code}' \
      -X OPTIONS "$function_url" \
      -H "apikey: $ANON_KEY" || true
  )"
  if [[ "$status" == "200" ]]; then
    ready="true"
    break
  fi
  if ! kill -0 "$function_pid" >/dev/null 2>&1; then
    cat "$function_log" >&2
    echo "Region-guard probe function exited before becoming ready." >&2
    exit 1
  fi
  sleep 1
done

if [[ "$ready" != "true" ]]; then
  cat "$function_log" >&2
  echo "Region-guard probe function did not become ready." >&2
  exit 1
fi

body_file="$(mktemp)"
trap 'rm -f "$body_file"; cleanup' EXIT

status=""
for attempt in $(seq 1 10); do
  status="$(
    curl --silent --show-error \
      --output "$body_file" \
      --write-out '%{http_code}' \
      -X POST "$function_url" \
      -H "apikey: $ANON_KEY" \
      -H 'Content-Type: application/json' \
      --data '{}' || true
  )"

  if [[ "$status" == "503" ]]; then
    break
  fi

  # The local Kong/Edge runtime may briefly return 502 after OPTIONS is ready
  # while the function worker finishes becoming request-ready. Do not weaken
  # the contract: retry only this transient local upstream status and still
  # require the function's own 503 region-guard response below.
  if [[ "$status" != "502" || "$attempt" == "10" ]]; then
    break
  fi
  if ! kill -0 "$function_pid" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

if [[ "$status" != "503" ]]; then
  cat "$function_log" >&2
  echo "Configured region guard expected HTTP 503 for missing/mismatched runtime region, got $status." >&2
  cat "$body_file" >&2 || true
  exit 1
fi

python3 - "$body_file" <<'PY'
import json
import pathlib
import sys

payload = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
assert payload.get("error") == "XQ_INVITATION_DELIVERY_REGION_UNAVAILABLE", payload
PY

echo "Invitation Delivery region guard passed: configured hosted region fails closed before auth/business processing when runtime region is unavailable or mismatched."
