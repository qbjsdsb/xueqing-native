#!/usr/bin/env bash
set -euo pipefail

actor_id='10000000-0000-0000-0000-000000000001'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_id='50000000-0000-0000-0000-000000000001'
operation_id_initial='90000000-0000-0000-0000-00000000a001'
operation_id_disabled='90000000-0000-0000-0000-00000000a002'
operation_id_membership='90000000-0000-0000-0000-00000000a003'
operation_id_assignment='90000000-0000-0000-0000-00000000a004'
test_email="provider-session-gate-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}@example.com"
test_password="$(python3 -c 'import secrets,string; alphabet=string.ascii_letters+string.digits; print("Xq!"+"".join(secrets.choice(alphabet) for _ in range(24)))')"

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
echo "::add-mask::$test_password"

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

assert_json() {
  local body="$1" expression="$2" expected="$3" actual
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
  local label="$1" expected="$2"
  if [[ "$rpc_status" =~ ^2 ]]; then
    echo "$label unexpectedly succeeded: $rpc_body" >&2
    exit 1
  fi
  assert_json "$rpc_body" 'message' "$expected"
}

created_auth_user_id=''
cleanup() {
  set +e
  psql_db >/dev/null 2>&1 <<SQL
reset role;
delete from public.operation_receipts where operation_id in (
  '$operation_id_initial'::uuid,
  '$operation_id_disabled'::uuid,
  '$operation_id_membership'::uuid,
  '$operation_id_assignment'::uuid
);
delete from public.observations where operation_id in (
  '$operation_id_initial'::uuid,
  '$operation_id_disabled'::uuid,
  '$operation_id_membership'::uuid,
  '$operation_id_assignment'::uuid
);
update public.student_teacher_assignments set active = true where id = '$assignment_id'::uuid;
update public.memberships set status = 'active', can_teach = true
 where organization_id = '$organization_id'::uuid and app_user_id = '$actor_id'::uuid;
delete from public.identity_links
 where provider_key = 'supabase'
   and external_subject = '$created_auth_user_id'
   and app_user_id = '$actor_id'::uuid;
update public.app_users set enabled = true
 where id = '$actor_id'::uuid;
SQL
  if [[ -n "$created_auth_user_id" ]]; then
    curl -sS -o /dev/null -X DELETE "$api_url/auth/v1/admin/users/$created_auth_user_id" \
      -H "apikey: $SERVICE_ROLE_KEY" \
      -H "Authorization: Bearer $SERVICE_ROLE_KEY" || true
  fi
}
trap cleanup EXIT

admin_user_response="$(curl -fsS -X POST "$api_url/auth/v1/admin/users" \
  -H "apikey: $SERVICE_ROLE_KEY" \
  -H "Authorization: Bearer $SERVICE_ROLE_KEY" \
  -H 'Content-Type: application/json' \
  --data "{\"email\":\"$test_email\",\"password\":\"$test_password\",\"email_confirm\":true}")"
created_auth_user_id="$(printf '%s' "$admin_user_response" | json_get 'id')"
[[ -n "$created_auth_user_id" ]] || { echo 'Auth admin create-user returned no id.' >&2; exit 1; }

psql_db -Atc "update public.app_users set enabled = true where id = '$actor_id'::uuid" >/dev/null

session_response="$(curl -fsS -X POST "$api_url/auth/v1/token?grant_type=password" \
  -H "apikey: $ANON_KEY" -H 'Content-Type: application/json' \
  --data "{\"email\":\"$test_email\",\"password\":\"$test_password\"}")"
access_token="$(printf '%s' "$session_response" | json_get 'access_token')"
refresh_token="$(printf '%s' "$session_response" | json_get 'refresh_token')"
[[ -n "$access_token" && -n "$refresh_token" ]] || { echo 'Auth password grant returned an incomplete session.' >&2; exit 1; }
echo "::add-mask::$access_token"
echo "::add-mask::$refresh_token"

provider_issuer="$(jwt_claim "$access_token" 'iss')"
provider_subject="$(jwt_claim "$access_token" 'sub')"
[[ -n "$provider_issuer" && -n "$provider_subject" ]] || {
  echo 'Provider-issued access token is missing issuer or subject.' >&2
  exit 1
}
if [[ "$provider_subject" != "$created_auth_user_id" ]]; then
  echo "Provider token subject '$provider_subject' did not match created Auth user '$created_auth_user_id'." >&2
  exit 1
fi

psql_db >/dev/null <<SQL
insert into public.identity_links (
  app_user_id, provider_key, issuer, external_subject, active
) values (
  '$actor_id'::uuid,
  'supabase',
  '$provider_issuer',
  '$provider_subject',
  true
)
on conflict (provider_key, issuer, external_subject)
do update set app_user_id = excluded.app_user_id, active = true;
SQL

call_rpc "$access_token" 'get_personal_bootstrap_v1' '{}'
expect_success 'PersonalBootstrap with live provider session'
assert_json "$rpc_body" 'contract' 'personal_bootstrap_v1'
assert_json "$rpc_body" 'actor.app_user_id' "$actor_id"

call_rpc "$access_token" 'get_student_recent_observations_v1' \
  "{\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\"}"
expect_success 'Recent projection with live provider session'
assert_json "$rpc_body" 'actor_app_user_id' "$actor_id"

call_rpc "$access_token" 'create_observation' \
  "{\"p_operation_id\":\"$operation_id_initial\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"Provider session conformance baseline observation.\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"provider-session-auth-conformance\"}}"
expect_success 'CreateObservation with live provider session'
assert_json "$rpc_body" 'command' 'create_observation_v1'

psql_db -Atc "update public.app_users set enabled = false where id = '$actor_id'::uuid" >/dev/null

call_rpc "$access_token" 'get_personal_bootstrap_v1' '{}'
expect_error_message 'Old token after AppUser disable / bootstrap' 'XQ_ACTOR_DISABLED'
call_rpc "$access_token" 'get_student_recent_observations_v1' \
  "{\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\"}"
expect_error_message 'Old token after AppUser disable / recent' 'XQ_ACTOR_DISABLED'
call_rpc "$access_token" 'create_observation' \
  "{\"p_operation_id\":\"$operation_id_disabled\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"Must not commit while AppUser is disabled.\",\"p_client_capture_metadata\":{\"fixture\":true}}"
expect_error_message 'Old token after AppUser disable / create' 'XQ_ACTOR_DISABLED'

refresh_response="$(curl -fsS -X POST "$api_url/auth/v1/token?grant_type=refresh_token" \
  -H "apikey: $ANON_KEY" -H 'Content-Type: application/json' \
  --data "{\"refresh_token\":\"$refresh_token\"}")"
refreshed_access_token="$(printf '%s' "$refresh_response" | json_get 'access_token')"
[[ -n "$refreshed_access_token" ]] || { echo 'Provider refresh returned no access token.' >&2; exit 1; }
echo "::add-mask::$refreshed_access_token"
call_rpc "$refreshed_access_token" 'get_personal_bootstrap_v1' '{}'
expect_error_message 'Refreshed provider token after AppUser disable' 'XQ_ACTOR_DISABLED'

psql_db >/dev/null <<SQL
update public.app_users set enabled = true where id = '$actor_id'::uuid;
update public.memberships set status = 'disabled'
 where organization_id = '$organization_id'::uuid and app_user_id = '$actor_id'::uuid;
SQL
call_rpc "$access_token" 'get_personal_bootstrap_v1' '{}'
expect_success 'PersonalBootstrap after membership revoke'
assert_json "$rpc_body" 'teaching_contexts' '[]'
call_rpc "$access_token" 'get_student_recent_observations_v1' \
  "{\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\"}"
expect_error_message 'Old token after membership revoke / recent' 'XQ_TEACHING_CONTEXT_UNAVAILABLE'
call_rpc "$access_token" 'create_observation' \
  "{\"p_operation_id\":\"$operation_id_membership\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"Must not commit after membership revoke.\",\"p_client_capture_metadata\":{\"fixture\":true}}"
expect_error_message 'Old token after membership revoke / create' 'XQ_MEMBERSHIP_DISABLED'

psql_db >/dev/null <<SQL
update public.memberships set status = 'active', can_teach = true
 where organization_id = '$organization_id'::uuid and app_user_id = '$actor_id'::uuid;
update public.student_teacher_assignments set active = false where id = '$assignment_id'::uuid;
SQL
call_rpc "$access_token" 'get_student_recent_observations_v1' \
  "{\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\"}"
expect_error_message 'Old token after assignment revoke / recent' 'XQ_TEACHING_CONTEXT_UNAVAILABLE'
call_rpc "$access_token" 'create_observation' \
  "{\"p_operation_id\":\"$operation_id_assignment\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"Must not commit after assignment revoke.\",\"p_client_capture_metadata\":{\"fixture\":true}}"
expect_error_message 'Old token after assignment revoke / create' 'XQ_TEACHER_ASSIGNMENT_REQUIRED'

rejected_fact_count="$(psql_db -Atc "select count(*) from public.observations where operation_id in ('$operation_id_disabled'::uuid, '$operation_id_membership'::uuid, '$operation_id_assignment'::uuid)")"
rejected_receipt_count="$(psql_db -Atc "select count(*) from public.operation_receipts where operation_id in ('$operation_id_disabled'::uuid, '$operation_id_membership'::uuid, '$operation_id_assignment'::uuid)")"
if [[ "$rejected_fact_count" != '0' || "$rejected_receipt_count" != '0' ]]; then
  echo "Rejected operations left side effects: observations=$rejected_fact_count receipts=$rejected_receipt_count" >&2
  exit 1
fi

echo 'Provider Session/Auth conformance passed.'
