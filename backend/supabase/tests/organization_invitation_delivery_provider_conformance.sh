#!/usr/bin/env bash
set -euo pipefail

organization_id='20000000-0000-0000-0000-000000000001'
invite_create_operation='97000000-0000-0000-0000-00000000d101'
delivery_operation='97000000-0000-0000-0000-00000000d102'
recipient='delivery.e2e@example.com'

status_env="$(mktemp)"
function_log="$(mktemp)"
function_env="$(mktemp)"
function_pid=''

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
: "${SERVICE_ROLE_KEY:?SERVICE_ROLE_KEY missing from local Supabase status}"
: "${JWT_SECRET:?JWT_SECRET missing from local Supabase status}"

echo "::add-mask::$ANON_KEY"
echo "::add-mask::$SERVICE_ROLE_KEY"

make_jwt() {
  JWT_SUBJECT="$1" python3 - <<'PY'
import base64
import hashlib
import hmac
import json
import os
import time

def b64url(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b'=').decode('ascii')

now = int(time.time())
header = {"alg": "HS256", "typ": "JWT"}
payload = {
    "iss": "supabase-demo",
    "sub": os.environ["JWT_SUBJECT"],
    "aud": "authenticated",
    "role": "authenticated",
    "iat": now,
    "exp": now + 3600,
}
encoded_header = b64url(json.dumps(header, separators=(',', ':')).encode())
encoded_payload = b64url(json.dumps(payload, separators=(',', ':')).encode())
signing_input = f"{encoded_header}.{encoded_payload}".encode()
signature = hmac.new(
    os.environ["JWT_SECRET"].encode(),
    signing_input,
    hashlib.sha256,
).digest()
print(f"{encoded_header}.{encoded_payload}.{b64url(signature)}")
PY
}

owner_token="$(make_jwt 'a0000000-0000-0000-0000-000000000001')"
echo "::add-mask::$owner_token"

rpc() {
  local token="$1" name="$2" body="$3"
  curl --fail-with-body --silent --show-error     -X POST "${API_URL%/}/rest/v1/rpc/$name"     -H "apikey: $ANON_KEY"     -H "Authorization: Bearer $token"     -H 'Content-Type: application/json'     --data "$body"
}

db_container="$(docker ps --filter 'name=supabase_db_' --format '{{.Names}}' | head -n 1)"
test -n "$db_container"
membership_count_before="$(
  docker exec -i "$db_container" psql -U postgres -d postgres -Atc     "select count(*) from public.memberships"
)"
assignment_count_before="$(
  docker exec -i "$db_container" psql -U postgres -d postgres -Atc     "select count(*) from public.student_teacher_assignments"
)"

invitation_response="$(
  rpc "$owner_token" create_organization_invitation_v1     "{\"p_operation_id\":\"$invite_create_operation\",\"p_organization_id\":\"$organization_id\",\"p_invited_email\":\"$recipient\",\"p_target_role\":\"teacher\",\"p_target_can_teach\":true}"
)"
invitation_id="$(
  printf '%s' "$invitation_response" | python3 -c     'import json,sys; value=json.load(sys.stdin).get("invitation_id"); assert value; print(value)'
)"

cat > "$function_env" <<EOF
XUEQING_INVITATION_REDIRECT_URL=http://127.0.0.1:3000/invitation
EOF

supabase functions serve organization-invitation-delivery   --workdir backend   --env-file "$function_env"   >"$function_log" 2>&1 &
function_pid="$!"

function_url="${API_URL%/}/functions/v1/organization-invitation-delivery"
ready='false'
for _ in $(seq 1 60); do
  status="$(
    curl --silent --output /dev/null --write-out '%{http_code}'       -X OPTIONS "$function_url"       -H "apikey: $ANON_KEY"       -H "Authorization: Bearer $owner_token" || true
  )"
  if [[ "$status" == '200' ]]; then
    ready='true'
    break
  fi
  if ! kill -0 "$function_pid" >/dev/null 2>&1; then
    cat "$function_log" >&2
    echo 'Invitation delivery function exited before becoming ready.' >&2
    exit 1
  fi
  sleep 1
done
if [[ "$ready" != 'true' ]]; then
  cat "$function_log" >&2
  echo 'Invitation delivery function did not become ready.' >&2
  exit 1
fi

unauthenticated_status="$(
  curl --silent --output /dev/null --write-out '%{http_code}' \
    -X POST "$function_url" \
    -H "apikey: $ANON_KEY" \
    -H 'Content-Type: application/json' \
    --data "{\"operation_id\":\"$delivery_operation\",\"invitation_id\":\"$invitation_id\"}" || true
)"
if [[ "$unauthenticated_status" != '401' ]]; then
  cat "$function_log" >&2
  echo "Invitation delivery explicit auth boundary expected HTTP 401, got $unauthenticated_status." >&2
  exit 1
fi

deliver() {
  curl --silent --show-error     -X POST "$function_url"     -H "apikey: $ANON_KEY"     -H "Authorization: Bearer $owner_token"     -H 'Content-Type: application/json'     --data "{\"operation_id\":\"$delivery_operation\",\"invitation_id\":\"$invitation_id\"}"
}

first_response="$(deliver)"
printf '%s' "$first_response" |   OPERATION_ID="$delivery_operation" INVITATION_ID="$invitation_id" python3 -c '
import json,os,sys
value=json.load(sys.stdin)
assert value.get("ok") is True, value
assert value.get("state") == "sent", value
assert value.get("operation_id") == os.environ["OPERATION_ID"], value
assert value.get("invitation_id") == os.environ["INVITATION_ID"], value
'

second_response="$(deliver)"
printf '%s' "$second_response" |   OPERATION_ID="$delivery_operation" python3 -c '
import json,os,sys
value=json.load(sys.stdin)
assert value.get("ok") is True, value
assert value.get("state") == "sent", value
assert value.get("operation_id") == os.environ["OPERATION_ID"], value
'

messages_json="$(curl --fail --silent --show-error 'http://127.0.0.1:54324/api/v1/messages?limit=50')"
message_id="$(
  printf '%s' "$messages_json" | RECIPIENT="$recipient" python3 -c '
import json,os,sys
recipient=os.environ["RECIPIENT"].lower()
payload=json.load(sys.stdin)
matches=[]
for message in payload.get("messages", []):
    recipients=message.get("To") or message.get("to") or []
    addresses=[]
    for item in recipients:
        if isinstance(item, dict):
            address=item.get("Address") or item.get("address")
            if address:
                addresses.append(str(address).lower())
        elif isinstance(item, str):
            addresses.append(item.lower())
    if recipient in addresses:
        matches.append(message)
assert len(matches) == 1, f"expected exactly one Mailpit message for {recipient}, got {len(matches)}"
identifier=matches[0].get("ID") or matches[0].get("id")
assert identifier, matches[0]
print(identifier)
'
)"

message_detail="$(curl --fail --silent --show-error "http://127.0.0.1:54324/api/v1/message/$message_id")"
printf '%s' "$message_detail" | INVITATION_ID="$invitation_id" python3 -c '
import json,os,sys
payload=json.load(sys.stdin)
serialized=json.dumps(payload, ensure_ascii=False)
invitation_id=os.environ["INVITATION_ID"]
assert invitation_id in serialized, "delivery email does not carry the opaque invitation id in its redirect"
'

delivery_state="$(
  docker exec -i "$db_container" psql -U postgres -d postgres -Atc     "select status from public.organization_invitation_deliveries where operation_id = '$delivery_operation'::uuid"
)"
delivery_count="$(
  docker exec -i "$db_container" psql -U postgres -d postgres -Atc     "select count(*) from public.organization_invitation_deliveries where invitation_id = '$invitation_id'::uuid"
)"
membership_count_after="$(
  docker exec -i "$db_container" psql -U postgres -d postgres -Atc     "select count(*) from public.memberships"
)"
assignment_count_after="$(
  docker exec -i "$db_container" psql -U postgres -d postgres -Atc     "select count(*) from public.student_teacher_assignments"
)"

if [[ "$delivery_state" != 'sent' || "$delivery_count" != '1' ]]; then
  echo "Delivery persistence mismatch: state=$delivery_state count=$delivery_count" >&2
  exit 1
fi
if [[ "$membership_count_after" != "$membership_count_before" || "$assignment_count_after" != "$assignment_count_before" ]]; then
  echo "Delivery fabricated business authority: memberships=$membership_count_before->$membership_count_after assignments=$assignment_count_before->$assignment_count_after" >&2
  exit 1
fi

echo 'Invitation delivery provider E2E passed: one Mailpit message, one sent delivery, no business-authority side effects.'
