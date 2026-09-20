#!/usr/bin/env bash
set -euo pipefail

actor_id='10000000-0000-0000-0000-000000000001'
legacy_auth_subject='a0000000-0000-0000-0000-000000000001'
decoy_auth_subject='d0000000-0000-0000-0000-000000000001'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_id='50000000-0000-0000-0000-000000000001'
other_organization_id='20000000-0000-0000-0000-000000000002'
other_student_id='30000000-0000-0000-0000-000000000002'
other_profile_id='40000000-0000-0000-0000-000000000002'
observation_operation_id='9a000000-0000-4000-8000-00000000c001'
case_operation_id='9a000000-0000-4000-8000-00000000c002'

suffix="${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}"
email_a="projection-a-${suffix}@example.com"
email_b="projection-b-${suffix}@example.com"
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

# Inspect the final migrated database definitions, not historical migration text.
# Provider-specific Auth extraction is allowed only behind the internal identity
# adapter; accepted business projections must not call Supabase auth helpers
# directly.
psql_db >/dev/null <<'SQL'
do $
declare
    v_signature text;
    v_definition text;
begin
    foreach v_signature in array array[
        'public.get_personal_bootstrap_v1()',
        'public.get_student_recent_observations_v1(uuid,uuid,uuid)',
        'public.get_student_learning_focus_v1(uuid,uuid,uuid)',
        'public.get_student_learning_cases_v1(uuid,uuid,uuid)',
        'public.get_personal_today_actions_v1()',
        'public.get_organization_management_v1(uuid)'
    ]
    loop
        select pg_catalog.pg_get_functiondef(v_signature::pg_catalog.regprocedure)
          into v_definition;

        if v_definition ~* 'auth[.](jwt|uid)[[:space:]]*[(]' then
            raise exception 'XQ_PROVIDER_PROJECTION_DIRECT_AUTH_DEPENDENCY: %', v_signature;
        end if;
    end loop;
end;
$;
SQL

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

canonical_projection() {
  python3 -c '
import json
import sys

volatile = {"generated_at_server", "organization_business_date"}
banned = {
    "provider_key",
    "issuer",
    "external_subject",
    "auth_subject",
    "access_token",
    "refresh_token",
    "session_id",
}

def normalize(value):
    if isinstance(value, dict):
        output = {}
        for key, item in value.items():
            if key in banned:
                raise SystemExit(f"provider/session field leaked into projection: {key}")
            if key in volatile:
                continue
            output[key] = normalize(item)
        return output
    if isinstance(value, list):
        return [normalize(item) for item in value]
    return value

print(json.dumps(normalize(json.load(sys.stdin)), ensure_ascii=False, sort_keys=True, separators=(",", ":")))
'
}

assert_projection_shape() {
  local body="$1" expected_contract="$2" actor_path="$3" nonempty_path="$4"
  printf '%s' "$body" |     EXPECTED_CONTRACT="$expected_contract"     EXPECTED_ACTOR="$actor_id"     ACTOR_PATH="$actor_path"     NONEMPTY_PATH="$nonempty_path"     python3 -c '
import json
import os
import sys

value = json.load(sys.stdin)
assert value.get("contract") == os.environ["EXPECTED_CONTRACT"], value

current = value
for part in os.environ["ACTOR_PATH"].split("."):
    current = current[int(part)] if isinstance(current, list) else current[part]
assert current == os.environ["EXPECTED_ACTOR"], (os.environ["ACTOR_PATH"], current)

path = os.environ.get("NONEMPTY_PATH", "")
if path:
    current = value
    for part in path.split("."):
        current = current[int(part)] if isinstance(current, list) else current[part]
    assert isinstance(current, list) and len(current) > 0, (path, current)
'
}

rpc_status=''
rpc_body=''
call_rpc() {
  local token="$1" function_name="$2" payload="$3" output_file
  output_file="$(mktemp)"
  rpc_status="$(curl -sS -o "$output_file" -w '%{http_code}'     -X POST "$api_url/rest/v1/rpc/$function_name"     -H "apikey: $ANON_KEY"     -H "Authorization: Bearer $token"     -H 'Content-Type: application/json'     --data "$payload")"
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
  curl -fsS -X POST "$api_url/auth/v1/admin/users"     -H "apikey: $SERVICE_ROLE_KEY"     -H "Authorization: Bearer $SERVICE_ROLE_KEY"     -H 'Content-Type: application/json'     --data "{\"email\":\"$email\",\"password\":\"$password\",\"email_confirm\":true}"
}

password_grant() {
  local email="$1" password="$2"
  curl -fsS -X POST "$api_url/auth/v1/token?grant_type=password"     -H "apikey: $ANON_KEY"     -H 'Content-Type: application/json'     --data "{\"email\":\"$email\",\"password\":\"$password\"}"
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
delete from public.learning_case_observation_links
 where operation_id = '$case_operation_id'::uuid;
delete from public.learning_case_events
 where operation_id = '$case_operation_id'::uuid;
delete from public.learning_case_actions
 where case_id in (
   select (receipt.result_payload ->> 'case_id')::uuid
     from public.operation_receipts as receipt
    where receipt.operation_id = '$case_operation_id'::uuid
 );
delete from public.learning_cases
 where id in (
   select (receipt.result_payload ->> 'case_id')::uuid
     from public.operation_receipts as receipt
    where receipt.operation_id = '$case_operation_id'::uuid
 );
delete from public.operation_receipts
 where operation_id in (
   '$case_operation_id'::uuid,
   '$observation_operation_id'::uuid
 );
delete from public.observations
 where operation_id = '$observation_operation_id'::uuid;
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
    curl -sS -o /dev/null -X DELETE "$api_url/auth/v1/admin/users/$auth_user_a"       -H "apikey: $SERVICE_ROLE_KEY"       -H "Authorization: Bearer $SERVICE_ROLE_KEY" || true
  fi
  if [[ -n "$auth_user_b" ]]; then
    curl -sS -o /dev/null -X DELETE "$api_url/auth/v1/admin/users/$auth_user_b"       -H "apikey: $SERVICE_ROLE_KEY"       -H "Authorization: Bearer $SERVICE_ROLE_KEY" || true
  fi
}
trap cleanup EXIT

user_a_json="$(create_auth_user "$email_a" "$password_a")"
user_b_json="$(create_auth_user "$email_b" "$password_b")"
auth_user_a="$(printf '%s' "$user_a_json" | json_get 'id')"
auth_user_b="$(printf '%s' "$user_b_json" | json_get 'id')"
[[ -n "$auth_user_a" && -n "$auth_user_b" ]] || {
  echo 'Provider admin API returned incomplete Auth identities.' >&2
  exit 1
}

session_a="$(password_grant "$email_a" "$password_a")"
session_b="$(password_grant "$email_b" "$password_b")"
token_a="$(printf '%s' "$session_a" | json_get 'access_token')"
token_b="$(printf '%s' "$session_b" | json_get 'access_token')"
[[ -n "$token_a" && -n "$token_b" ]] || {
  echo 'Provider password grant returned incomplete sessions.' >&2
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
  echo 'Provider token subject did not match the created Auth identity.' >&2
  exit 1
}

# Both real external identities intentionally resolve to the same durable
# AppUser. The old direct auth_subject binding is made wrong on purpose.
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

call_rpc "$token_a" 'create_observation'   "{\"p_operation_id\":\"$observation_operation_id\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"Provider projection conformance observation.\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"provider-projection-conformance\"}}"
expect_success 'Provider identity A CreateObservation'
observation_id="$(printf '%s' "$rpc_body" | json_get 'observation_id')"
[[ -n "$observation_id" ]] || {
  echo 'CreateObservation returned no observation id.' >&2
  exit 1
}

call_rpc "$token_b" 'create_learning_case'   "{\"p_operation_id\":\"$case_operation_id\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_owner_assignment_id\":\"$assignment_id\",\"p_title\":\"Provider projection conformance case\",\"p_primary_action_text\":\"Verify provider-neutral projection semantics\",\"p_primary_action_due_on\":null,\"p_source_observation_id\":\"$observation_id\"}"
expect_success 'Provider identity B CreateLearningCase'

compare_projection() {
  local label="$1" function_name="$2" payload="$3" contract="$4" actor_path="$5" nonempty_path="$6"

  call_rpc "$token_a" "$function_name" "$payload"
  expect_success "$label identity A"
  local body_a="$rpc_body"
  assert_projection_shape "$body_a" "$contract" "$actor_path" "$nonempty_path"
  local canonical_a
  canonical_a="$(printf '%s' "$body_a" | canonical_projection)"

  call_rpc "$token_b" "$function_name" "$payload"
  expect_success "$label identity B"
  local body_b="$rpc_body"
  assert_projection_shape "$body_b" "$contract" "$actor_path" "$nonempty_path"
  local canonical_b
  canonical_b="$(printf '%s' "$body_b" | canonical_projection)"

  if [[ "$canonical_a" != "$canonical_b" ]]; then
    echo "$label differed across two external identities mapped to the same AppUser." >&2
    echo "identity A: $canonical_a" >&2
    echo "identity B: $canonical_b" >&2
    exit 1
  fi

  echo "$label provider-neutral payload equivalence passed."
}

teaching_scope_payload="{\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\"}"
management_payload="{\"p_organization_id\":\"$organization_id\"}"

compare_projection   'PersonalBootstrap v1'   'get_personal_bootstrap_v1'   '{}'   'personal_bootstrap_v1'   'actor.app_user_id'   'teaching_contexts'

compare_projection   'Student Recent Observations v1'   'get_student_recent_observations_v1'   "$teaching_scope_payload"   'student_recent_observations_v1'   'actor_app_user_id'   'observations'

compare_projection   'Student Learning Focus v1'   'get_student_learning_focus_v1'   "$teaching_scope_payload"   'student_learning_focus_v1'   'actor_app_user_id'   'cases'

compare_projection   'Student Learning Cases v1'   'get_student_learning_cases_v1'   "$teaching_scope_payload"   'student_learning_cases_v1'   'actor_app_user_id'   'cases'

compare_projection   'Personal Today Actions v1'   'get_personal_today_actions_v1'   '{}'   'personal_today_actions_v1'   'actor_app_user_id'   'actions'

compare_projection   'OrganizationManagement v1'   'get_organization_management_v1'   "$management_payload"   'organization_management_v1'   'actor.app_user_id'   'members'

# Provider equivalence must not turn a real external identity into broader
# business authority. Teacher A is deliberately an active teaching-capable
# member of Organization B, but has no assignment there and is not a manager.
cross_teaching_payload="{\"p_organization_id\":\"$other_organization_id\",\"p_student_id\":\"$other_student_id\",\"p_subject_profile_id\":\"$other_profile_id\"}"
cross_management_payload="{\"p_organization_id\":\"$other_organization_id\"}"

call_rpc "$token_a" 'get_student_recent_observations_v1' "$cross_teaching_payload"
expect_error_message 'Identity A cross-Organization teaching projection' 'XQ_TEACHING_CONTEXT_UNAVAILABLE'

call_rpc "$token_b" 'get_student_learning_focus_v1' "$cross_teaching_payload"
expect_error_message 'Identity B cross-Organization learning projection' 'XQ_TEACHING_CONTEXT_UNAVAILABLE'

call_rpc "$token_a" 'get_organization_management_v1' "$cross_management_payload"
expect_error_message 'Identity A teacher-only Organization Management projection' 'XQ_ORGANIZATION_MANAGEMENT_REQUIRED'

echo 'Provider Projection conformance passed: six accepted projections preserve application-owned semantics across independent real external identities and real provider sessions remain fail-closed outside live business scope.'
