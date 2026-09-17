#!/usr/bin/env bash
set -euo pipefail

# Provider Session/Auth conformance gate.
#
# This test deliberately uses a real local Supabase Auth session instead of a
# hand-minted JWT. It proves that Xueqing's live application authority checks
# remain effective even while an already-issued provider access token is still
# cryptographically valid.

actor_id='10000000-0000-0000-0000-000000000001'
original_auth_subject='a0000000-0000-0000-0000-000000000001'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_id='50000000-0000-0000-0000-000000000001'
operation_id_initial='90000000-0000-0000-0000-00000000a001'
operation_id_disabled='90000000-0000-0000-0000-00000000a002'
operation_id_membership='90000000-0000-0000-0000-00000000a003'
operation_id_assignment='90000000-0000-0000-0000-00000000a004'
test_email='provider-session-gate@example.com'
test_password='ProviderSessionGate-2026!'

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

# Use the actual local database so authority changes are independent of the
# client token and visible immediately to SECURITY DEFINER RPCs.
db_container="$(docker ps --filter 'name=supabase_db_' --format '{{.Names}}' | head -n 1)"
if [[ -z "$db_container" ]]; then
  echo 'Local Supabase database container was not found.' >&2
  exit 1
fi

psql_db() {
  docker exec -i "$db_container" psql -U postgres -d postgres -v ON_ERROR_STOP=1 "$@"
}

json_field() {
  local field="$1"
  python3 -c 'import json,sys; value=json.load(sys.stdin); out=value'"$(printf '%s' "$field" | sed 's/\./]["/g')"'' 2>/dev/null
}

json_get() {
  local expression="$1"
  python3 - "$expression" <<'PY'
import json
import sys

expression = sys.argv[1]
data = json.load(sys.stdin)
current = data
for part in expression.split('.'):
    if isinstance(current, list):
        current = current[int(part)]
    else:
        current = current[part]
if isinstance(current, (dict, list)):
    print(json.dumps(current, ensure_ascii=False, separators=(',', ':')))
elif current is None:
    print('')
else:
    print(current)
PY
}

assert_json() {
  local body="$1"
  local expression="$2"
  local expected="$3"
  local actual
  actual="$(printf '%s' "$body" | json_get "$expression")"
  if [[ "$actual" != "$expected" ]]; then
    echo "JSON assertion failed: $expression expected '$expected', got '$actual'." >&2
    echo "$body" >&2
    exit 1
  fi
}

rpc_status=''
rpc_body=''
call_rpc() {
  local token="$1"
  local function_name="$2"
  local payload="$3"
  local output_file
  output_file="$(mktemp)"
  rpc_status="$(curl -sS \
    -o "$output_file" \
    -w '%{http_code}' \
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
    echo "$label expected success, got HTTP $rpc_status:" >&2
    echo "$rpc_body" >&2
    exit 1
  fi
}

expect_error_message() {
  local label="$1"
  local expected="$2"
  if [[ "$rpc_status" =~ ^2 ]]; then
    echo "$label unexpectedly succeeded with an authority-revoked session:" >&2
    echo "$rpc_body" >&2
    exit 1
  fi
  assert_json "$rpc_body" 'message' "$expected"
}

created_auth_user_id=''
access_token=''
refresh_token=''
cleanup() {
  set +e

  psql_db >/dev/null 2>&1 <<SQL
reset role;
delete from public.operation_receipts
 where operation_id in (
   '$operation_id_initial'::uuid,
   '$operation_id_disabled'::uuid,
   '$operation_id_membership'::uuid,
   '$operation_id_assignment'::uuid
 );
delete from public.observations
 where operation_id in (
   '$operation_id_initial'::uuid,
   '$operation_id_disabled'::uuid,
   '$operation_id_membership'::uuid,
   '$operation_id_assignment'::uuid
 );
update public.student_teacher_assignments
   set active = true
 where id = '$assignment_id'::uuid;
update public.memberships
   set status = 'active', can_teach = true
 where organization_id = '$organization_id'::uuid
   and app_user_id = '$actor_id'::uuid;
update public.app_users
   set enabled = true,
       auth_subject = '$original_auth_subject'::uuid
 where id = '$actor_id'::uuid;
SQL

  if [[ -n "$created_auth_user_id" ]]; then
    curl -sS -o /dev/null \
      -X DELETE "$api_url/auth/v1/admin/users/$created_auth_user_id" \
      -H "apikey: $SERVICE_ROLE_KEY" \
      -H "Authorization: Bearer $SERVICE_ROLE_KEY" || true
  fi
}
trap cleanup EXIT

# 1. Create a real provider identity and obtain a provider-issued session.
admin_user_response="$(curl -fsS \
  -X POST "$api_url/auth/v1/admin/users" \
  -H "apikey: $SERVICE_ROLE_KEY" \
  -H "Authorization: Bearer $SERVICE_ROLE_KEY" \
  -H 'Content-Type: application/json' \
  --data "{\"email\":\"$test_email\",\"password\":\"$test_password\",\"email_confirm\":true}")"
created_auth_user_id="$(printf '%s' "$admin_user_response" | json_get 'id')"
if [[ -z "$created_auth_user_id" ]]; then
  echo 'Supabase Auth admin create-user did not return an id.' >&2
  echo "$admin_user_response" >&2
  exit 1
fi

psql_db -Atc "update public.app_users set auth_subject = '$created_auth_user_id'::uuid, enabled = true where id = '$actor_id'::uuid" >/dev/null

session_response="$(curl -fsS \
  -X POST "$api_url/auth/v1/token?grant_type=password" \
  -H "apikey: $ANON_KEY" \
  -H 'Content-Type: application/json' \
  --data "{\"email\":\"$test_email\",\"password\":\"$test_password\"}")"
access_token="$(printf '%s' "$session_response" | json_get 'access_token')"
refresh_token="$(printf '%s' "$session_response" | json_get 'refresh_token')"
if [[ -z "$access_token" || -z "$refresh_token" ]]; then
  echo 'Supabase Auth password grant did not issue both access and refresh tokens.' >&2
  echo "$session_response" >&2
  exit 1
fi

echo "::add-mask::$access_token"
echo "::add-mask::$refresh_token"

# 2. The provider-issued token authorizes the three currently supported API
# surfaces before any application authority is revoked.
call_rpc "$access_token" 'get_personal_bootstrap_v1' '{}'
expect_success 'PersonalBootstrap with live provider session'
assert_json "$rpc_body" 'contract' 'personal_bootstrap_v1'
assert_json "$rpc_body" 'actor.app_user_id' "$actor_id"

call_rpc "$access_token" 'get_student_recent_observations_v1' \
  "{\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\"}"
expect_success 'RecentObservation projection with live provider session'
assert_json "$rpc_body" 'contract' 'student_recent_observations_v1'
assert_json "$rpc_body" 'actor_app_user_id' "$actor_id"

call_rpc "$access_token" 'create_observation' \
  "{\"p_operation_id\":\"$operation_id_initial\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"Provider session conformance baseline observation.\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"provider-session-auth-conformance\"}}"
expect_success 'CreateObservation with live provider session'
assert_json "$rpc_body" 'command' 'create_observation_v1'
assert_json "$rpc_body" 'actor_app_user_id' "$actor_id"

# 3. Disable the application-owned identity WITHOUT changing the already-issued
# provider token. The same old JWT must immediately lose all business access.
psql_db -Atc "update public.app_users set enabled = false where id = '$actor_id'::uuid" >/dev/null

call_rpc "$access_token" 'get_personal_bootstrap_v1' '{}'
expect_error_message 'Old token after AppUser disable / PersonalBootstrap' 'XQ_ACTOR_DISABLED'

call_rpc "$access_token" 'get_student_recent_observations_v1' \
  "{\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\"}"
expect_error_message 'Old token after AppUser disable / recent projection' 'XQ_ACTOR_DISABLED'

call_rpc "$access_token" 'create_observation' \
  "{\"p_operation_id\":\"$operation_id_disabled\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"This must never commit while the AppUser is disabled.\",\"p_client_capture_metadata\":{\"fixture\":true}}"
expect_error_message 'Old token after AppUser disable / CreateObservation' 'XQ_ACTOR_DISABLED'

# The external provider may still consider the refresh token valid because the
# business AppUser disable is intentionally provider-neutral. Even a newly
# refreshed provider access token must remain unable to bypass Xueqing authority.
refresh_response="$(curl -fsS \
  -X POST "$api_url/auth/v1/token?grant_type=refresh_token" \
  -H "apikey: $ANON_KEY" \
  -H 'Content-Type: application/json' \
  --data "{\"refresh_token\":\"$refresh_token\"}")"
refreshed_access_token="$(printf '%s' "$refresh_response" | json_get 'access_token')"
if [[ -z "$refreshed_access_token" ]]; then
  echo 'Provider refresh did not issue a replacement access token.' >&2
  echo "$refresh_response" >&2
  exit 1
fi

echo "::add-mask::$refreshed_access_token"
call_rpc "$refreshed_access_token" 'get_personal_bootstrap_v1' '{}'
expect_error_message 'Refreshed provider token after AppUser disable' 'XQ_ACTOR_DISABLED'

# 4. Re-enable the AppUser but revoke organization membership. PersonalBootstrap
# may still authenticate the actor, but it must expose no organization/context;
# scoped reads/writes must fail with live authority errors using the SAME old JWT.
psql_db >/dev/null <<SQL
update public.app_users set enabled = true where id = '$actor_id'::uuid;
update public.memberships
   set status = 'disabled'
 where organization_id = '$organization_id'::uuid
   and app_user_id = '$actor_id'::uuid;
SQL

call_rpc "$access_token" 'get_personal_bootstrap_v1' '{}'
expect_success 'PersonalBootstrap after membership revoke'
assert_json "$rpc_body" 'organizations' '[]'
assert_json "$rpc_body" 'teaching_contexts' '[]'

call_rpc "$access_token" 'get_student_recent_observations_v1' \
  "{\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\"}"
expect_error_message 'Old token after membership revoke / recent projection' 'XQ_TEACHING_CONTEXT_UNAVAILABLE'

call_rpc "$access_token" 'create_observation' \
  "{\"p_operation_id\":\"$operation_id_membership\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"This must never commit after membership revoke.\",\"p_client_capture_metadata\":{\"fixture\":true}}"
expect_error_message 'Old token after membership revoke / CreateObservation' 'XQ_MEMBERSHIP_DISABLED'

# 5. Restore membership but revoke the concrete teaching assignment. Again the
# same old provider session must not retain stale teaching authority.
psql_db >/dev/null <<SQL
update public.memberships
   set status = 'active', can_teach = true
 where organization_id = '$organization_id'::uuid
   and app_user_id = '$actor_id'::uuid;
update public.student_teacher_assignments
   set active = false
 where id = '$assignment_id'::uuid;
SQL

call_rpc "$access_token" 'get_student_recent_observations_v1' \
  "{\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\"}"
expect_error_message 'Old token after assignment revoke / recent projection' 'XQ_TEACHING_CONTEXT_UNAVAILABLE'

call_rpc "$access_token" 'create_observation' \
  "{\"p_operation_id\":\"$operation_id_assignment\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"This must never commit after assignment revoke.\",\"p_client_capture_metadata\":{\"fixture\":true}}"
expect_error_message 'Old token after assignment revoke / CreateObservation' 'XQ_TEACHER_ASSIGNMENT_REQUIRED'

# No rejected operation may have produced a durable teaching fact or receipt.
rejected_fact_count="$(psql_db -Atc "select count(*) from public.observations where operation_id in ('$operation_id_disabled'::uuid, '$operation_id_membership'::uuid, '$operation_id_assignment'::uuid)")"
rejected_receipt_count="$(psql_db -Atc "select count(*) from public.operation_receipts where operation_id in ('$operation_id_disabled'::uuid, '$operation_id_membership'::uuid, '$operation_id_assignment'::uuid)")"
if [[ "$rejected_fact_count" != '0' || "$rejected_receipt_count" != '0' ]]; then
  echo "Rejected stale-authority operations left durable side effects: observations=$rejected_fact_count receipts=$rejected_receipt_count" >&2
  exit 1
fi

echo 'Provider Session/Auth conformance passed: real provider session issued, old/refreshed tokens fail closed after live application authority revocation.'
