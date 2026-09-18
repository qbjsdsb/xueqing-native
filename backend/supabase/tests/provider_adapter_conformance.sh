#!/usr/bin/env bash
set -euo pipefail

actor_id='10000000-0000-0000-0000-000000000001'
legacy_auth_subject='a0000000-0000-0000-0000-000000000001'
decoy_auth_subject='d0000000-0000-0000-0000-000000000001'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_id='50000000-0000-0000-0000-000000000001'
operation_id='90000000-0000-0000-0000-00000000b001'

suffix="${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}"
email_a="provider-adapter-a-${suffix}@example.com"
email_b="provider-adapter-b-${suffix}@example.com"
password_a="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+"".join(secrets.choice(a) for _ in range(24)))')"
password_b="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+"".join(secrets.choice(a) for _ in range(24)))')"

status_env="$(mktemp)"
supabase status --workdir backend -o env > "$status_env"
set -a
# shellcheck disable=SC1090
source "$status_env"
set +a
rm -f "$status_env"

: "${API_URL:?API_URL missing from local Supabase status}"
: "${ANON_KEY:?ANON_KEY missing from local Supabase status}"
: "${SERVICE_ROLE_KEY:?SERVICE_ROLE_KEY missing from local Supabase status}"
api_url="${API_URL%/}"

echo "::add-mask::$ANON_KEY"
echo "::add-mask::$SERVICE_ROLE_KEY"
echo "::add-mask::$password_a"
echo "::add-mask::$password_b"

db_container="$(docker ps --filter 'name=supabase_db_' --format '{{.Names}}' | head -n 1)"
if [[ -z "$db_container" ]]; then
  echo 'Local Supabase database container was not found.' >&2
  exit 1
fi

psql_db() {
  docker exec -i "$db_container" psql -U postgres -d postgres -v ON_ERROR_STOP=1 "$@"
}

json_get() {
  local expression="$1"
  python3 -c '
import json
import sys
expression = sys.argv[1]
current = json.load(sys.stdin)
for part in expression.split("."):
    current = current[int(part)] if isinstance(current, list) else current[part]
if isinstance(current, (dict, list)):
    print(json.dumps(current, ensure_ascii=False, separators=(",", ":")))
elif current is None:
    print("")
else:
    print(current)
' "$expression"
}

jwt_claim() {
  local token="$1" claim="$2"
  python3 -c '
import base64
import json
import sys
token, claim = sys.argv[1], sys.argv[2]
payload = token.split(".")[1]
payload += "=" * (-len(payload) % 4)
claims = json.loads(base64.urlsafe_b64decode(payload.encode()).decode())
value = claims.get(claim)
print("" if value is None else value)
' "$token" "$claim"
}

rpc_status=''
rpc_body=''
call_rpc() {
  local token="$1" function_name="$2" payload="$3" output_file
  output_file="$(mktemp)"
  rpc_status="$(curl -sS -o "$output_file" -w '%{http_code}' \
    -X POST "$api_url/rest/v1/rpc/$function_name" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $token" \
    -H 'Content-Type: application/json' \
    --data "$payload")"
  rpc_body="$(cat "$output_file")"
  rm -f "$output_file"
}

expect_success() {
  local label="$1"
  if [[ ! "$rpc_status" =~ ^2 ]]; then
    echo "$label expected success, got HTTP $rpc_status: $rpc_body" >&2
    exit 1
  fi
}

expect_error_message() {
  local label="$1" expected="$2" actual
  if [[ "$rpc_status" =~ ^2 ]]; then
    echo "$label unexpectedly succeeded: $rpc_body" >&2
    exit 1
  fi
  actual="$(printf '%s' "$rpc_body" | json_get 'message')"
  if [[ "$actual" != "$expected" ]]; then
    echo "$label expected '$expected', got '$actual': $rpc_body" >&2
    exit 1
  fi
}

create_auth_user() {
  local email="$1" password="$2"
  curl -fsS -X POST "$api_url/auth/v1/admin/users" \
    -H "apikey: $SERVICE_ROLE_KEY" \
    -H "Authorization: Bearer $SERVICE_ROLE_KEY" \
    -H 'Content-Type: application/json' \
    --data "{\"email\":\"$email\",\"password\":\"$password\",\"email_confirm\":true}"
}

password_grant() {
  local email="$1" password="$2"
  curl -fsS -X POST "$api_url/auth/v1/token?grant_type=password" \
    -H "apikey: $ANON_KEY" \
    -H 'Content-Type: application/json' \
    --data "{\"email\":\"$email\",\"password\":\"$password\"}"
}

auth_user_a=''
auth_user_b=''
subject_a=''
subject_b=''
issuer_a=''
issuer_b=''

cleanup() {
  set +e
  psql_db >/dev/null 2>&1 <<SQL
reset role;
delete from public.operation_receipts where operation_id = '$operation_id'::uuid;
delete from public.observations where operation_id = '$operation_id'::uuid;
delete from public.identity_links
 where provider_key = 'supabase'
   and external_subject in ('$subject_a', '$subject_b')
   and app_user_id = '$actor_id'::uuid;
update public.app_users
   set auth_subject = '$legacy_auth_subject'::uuid,
       enabled = true
 where id = '$actor_id'::uuid;
SQL
  if [[ -n "$auth_user_a" ]]; then
    curl -sS -o /dev/null -X DELETE "$api_url/auth/v1/admin/users/$auth_user_a" \
      -H "apikey: $SERVICE_ROLE_KEY" \
      -H "Authorization: Bearer $SERVICE_ROLE_KEY" || true
  fi
  if [[ -n "$auth_user_b" ]]; then
    curl -sS -o /dev/null -X DELETE "$api_url/auth/v1/admin/users/$auth_user_b" \
      -H "apikey: $SERVICE_ROLE_KEY" \
      -H "Authorization: Bearer $SERVICE_ROLE_KEY" || true
  fi
}
trap cleanup EXIT

user_a_json="$(create_auth_user "$email_a" "$password_a")"
user_b_json="$(create_auth_user "$email_b" "$password_b")"
auth_user_a="$(printf '%s' "$user_a_json" | json_get 'id')"
auth_user_b="$(printf '%s' "$user_b_json" | json_get 'id')"
[[ -n "$auth_user_a" && -n "$auth_user_b" ]] || {
  echo 'Provider admin API returned an incomplete user identity.' >&2
  exit 1
}

session_a="$(password_grant "$email_a" "$password_a")"
session_b="$(password_grant "$email_b" "$password_b")"
token_a="$(printf '%s' "$session_a" | json_get 'access_token')"
token_b="$(printf '%s' "$session_b" | json_get 'access_token')"
[[ -n "$token_a" && -n "$token_b" ]] || {
  echo 'Provider password grant returned an incomplete session.' >&2
  exit 1
}
echo "::add-mask::$token_a"
echo "::add-mask::$token_b"

issuer_a="$(jwt_claim "$token_a" 'iss')"
issuer_b="$(jwt_claim "$token_b" 'iss')"
subject_a="$(jwt_claim "$token_a" 'sub')"
subject_b="$(jwt_claim "$token_b" 'sub')"
[[ -n "$issuer_a" && -n "$issuer_b" && -n "$subject_a" && -n "$subject_b" ]] || {
  echo 'Provider token is missing issuer or subject.' >&2
  exit 1
}
[[ "$subject_a" == "$auth_user_a" && "$subject_b" == "$auth_user_b" ]] || {
  echo 'Provider token subject did not match the created provider identity.' >&2
  exit 1
}

# Two different external identities intentionally resolve to the same durable
# AppUser. The legacy auth_subject column is then changed to a decoy value. Any
# successful business call below therefore proves that the old direct binding
# is no longer authoritative.
psql_db >/dev/null <<SQL
insert into public.identity_links (
  app_user_id, provider_key, issuer, external_subject, active
) values
  ('$actor_id'::uuid, 'supabase', '$issuer_a', '$subject_a', true),
  ('$actor_id'::uuid, 'supabase', '$issuer_b', '$subject_b', true);
update public.app_users
   set auth_subject = '$decoy_auth_subject'::uuid,
       enabled = true
 where id = '$actor_id'::uuid;
SQL

call_rpc "$token_a" 'get_personal_bootstrap_v1' '{}'
expect_success 'Identity A PersonalBootstrap'
actor_a="$(printf '%s' "$rpc_body" | json_get 'actor.app_user_id')"
[[ "$actor_a" == "$actor_id" ]] || {
  echo "Identity A resolved to unexpected AppUser '$actor_a'." >&2
  exit 1
}

call_rpc "$token_b" 'get_personal_bootstrap_v1' '{}'
expect_success 'Identity B PersonalBootstrap'
actor_b="$(printf '%s' "$rpc_body" | json_get 'actor.app_user_id')"
[[ "$actor_b" == "$actor_id" ]] || {
  echo "Identity B resolved to unexpected AppUser '$actor_b'." >&2
  exit 1
}

call_rpc "$token_b" 'get_student_recent_observations_v1' \
  "{\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\"}"
expect_success 'Identity B recent Observation projection'
recent_actor="$(printf '%s' "$rpc_body" | json_get 'actor_app_user_id')"
[[ "$recent_actor" == "$actor_id" ]] || {
  echo "Recent projection resolved to unexpected AppUser '$recent_actor'." >&2
  exit 1
}

call_rpc "$token_b" 'create_observation' \
  "{\"p_operation_id\":\"$operation_id\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"Provider adapter identity-link conformance observation.\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"provider-adapter-conformance\"}}"
expect_success 'Identity B CreateObservation'
created_actor="$(printf '%s' "$rpc_body" | json_get 'actor_app_user_id')"
[[ "$created_actor" == "$actor_id" ]] || {
  echo "CreateObservation resolved to unexpected AppUser '$created_actor'." >&2
  exit 1
}

# Revoking one external link affects only that provider identity. Another
# external identity linked to the same AppUser keeps the same business identity
# and authority.
psql_db -Atc "update public.identity_links set active = false where provider_key = 'supabase' and issuer = '$issuer_a' and external_subject = '$subject_a'" >/dev/null

call_rpc "$token_a" 'get_personal_bootstrap_v1' '{}'
expect_error_message 'Disabled IdentityLink A' 'XQ_ACTOR_NOT_FOUND'

call_rpc "$token_b" 'get_personal_bootstrap_v1' '{}'
expect_success 'Identity B after IdentityLink A revoke'
actor_b_after_revoke="$(printf '%s' "$rpc_body" | json_get 'actor.app_user_id')"
[[ "$actor_b_after_revoke" == "$actor_id" ]] || {
  echo "Identity B changed business identity after sibling link revoke." >&2
  exit 1
}

echo 'Provider Adapter conformance passed: provider identity tuples resolve through IdentityLink to stable AppUser semantics.'
