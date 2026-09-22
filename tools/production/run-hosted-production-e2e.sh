#!/usr/bin/env bash
set -euo pipefail

: "${XUEQING_ACCEPTANCE_API_URL:?missing hosted API URL}"
: "${XUEQING_ACCEPTANCE_PUBLISHABLE_KEY:?missing publishable key}"
: "${XUEQING_ACCEPTANCE_SERVICE_ROLE_KEY:?missing protected service-role key}"
: "${XUEQING_ACCEPTANCE_EDGE_REGION:?missing required Edge region}"

api_url="${XUEQING_ACCEPTANCE_API_URL%/}"
anon_key="$XUEQING_ACCEPTANCE_PUBLISHABLE_KEY"
service_key="$XUEQING_ACCEPTANCE_SERVICE_ROLE_KEY"
edge_region="$XUEQING_ACCEPTANCE_EDGE_REGION"
bucket='teaching-attachments-v1'

app_a='91000000-0000-4000-8000-000000000001'
app_b='91000000-0000-4000-8000-000000000002'
org_a='92000000-0000-4000-8000-000000000001'
org_b='92000000-0000-4000-8000-000000000002'
student_a='93000000-0000-4000-8000-000000000001'
student_b='93000000-0000-4000-8000-000000000002'
profile_a='94000000-0000-4000-8000-000000000001'
profile_b='94000000-0000-4000-8000-000000000002'
assignment_a='95000000-0000-4000-8000-000000000001'
assignment_b='95000000-0000-4000-8000-000000000002'
observation_operation='96000000-0000-4000-8000-000000000001'
attachment_id='97000000-0000-4000-8000-000000000001'
attachment_operation='98000000-0000-4000-8000-000000000001'

suffix="${GITHUB_RUN_ID:-manual}-${GITHUB_RUN_ATTEMPT:-0}"
email_a="xueqing-hosted-a-${suffix}@example.com"
email_b="xueqing-hosted-b-${suffix}@example.com"
password_a="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+ "".join(secrets.choice(a) for _ in range(28)))')"
password_b="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+ "".join(secrets.choice(a) for _ in range(28)))')"

echo "::add-mask::$service_key"
echo "::add-mask::$password_a"
echo "::add-mask::$password_b"

json_get() {
  local expression="$1"
  python3 -c '
import json,sys
value=json.load(sys.stdin)
for part in sys.argv[1].split("."):
    value=value[int(part)] if isinstance(value,list) else value[part]
if isinstance(value,(dict,list)):
    print(json.dumps(value,ensure_ascii=False,separators=(",",":")))
elif value is None:
    print("")
else:
    print(value)
' "$expression"
}

jwt_claim() {
  local token="$1" claim="$2"
  python3 -c '
import base64,json,sys
token,claim=sys.argv[1],sys.argv[2]
payload=token.split(".")[1]
payload += "=" * (-len(payload)%4)
print(json.loads(base64.urlsafe_b64decode(payload.encode()).decode()).get(claim,""))
' "$token" "$claim"
}

admin_rest() {
  local method="$1" path="$2" body="${3:-}"
  if [[ -n "$body" ]]; then
    curl --fail-with-body --silent --show-error -X "$method" "$api_url/rest/v1/$path"       -H "apikey: $service_key"       -H "Authorization: Bearer $service_key"       -H 'Content-Type: application/json'       -H 'Prefer: return=minimal'       --data "$body"
  else
    curl --fail-with-body --silent --show-error -X "$method" "$api_url/rest/v1/$path"       -H "apikey: $service_key"       -H "Authorization: Bearer $service_key"
  fi
}

rpc_status=''
rpc_body=''
call_rpc() {
  local token="$1" name="$2" payload="$3" out
  out="$(mktemp)"
  rpc_status="$(curl --silent --show-error -o "$out" -w '%{http_code}'     -X POST "$api_url/rest/v1/rpc/$name"     -H "apikey: $anon_key"     -H "Authorization: Bearer $token"     -H 'Content-Type: application/json'     --data "$payload")"
  rpc_body="$(cat "$out")"
  rm -f "$out"
}

expect_rpc_success() {
  local label="$1"
  if [[ ! "$rpc_status" =~ ^2 ]]; then
    echo "$label expected success, got HTTP $rpc_status: $rpc_body" >&2
    exit 1
  fi
}

expect_rpc_denied() {
  local label="$1"
  if [[ "$rpc_status" =~ ^2 ]]; then
    echo "$label unexpectedly succeeded: $rpc_body" >&2
    exit 1
  fi
}

auth_user_a=''
auth_user_b=''
token_a=''
token_b=''
refresh_b=''
issuer_a=''
issuer_b=''
subject_a=''
subject_b=''
observation_id=''
object_name=''
tmp_png="$(mktemp --suffix=.png)"

cleanup_business() {
  set +e
  [[ -n "$object_name" ]] && curl -sS -o /dev/null -X DELETE     "$api_url/storage/v1/object/$bucket/$object_name"     -H "apikey: $service_key" -H "Authorization: Bearer $service_key"
  admin_rest DELETE "observation_attachments?id=eq.$attachment_id" >/dev/null 2>&1
  admin_rest DELETE "operation_receipts?operation_id=in.($attachment_operation,$observation_operation)" >/dev/null 2>&1
  admin_rest DELETE "observations?operation_id=eq.$observation_operation" >/dev/null 2>&1
  admin_rest DELETE "identity_links?app_user_id=in.($app_a,$app_b)" >/dev/null 2>&1
  admin_rest DELETE "student_teacher_assignments?id=in.($assignment_a,$assignment_b)" >/dev/null 2>&1
  admin_rest DELETE "student_subject_profiles?id=in.($profile_a,$profile_b)" >/dev/null 2>&1
  admin_rest DELETE "students?id=in.($student_a,$student_b)" >/dev/null 2>&1
  admin_rest DELETE "memberships?app_user_id=in.($app_a,$app_b)" >/dev/null 2>&1
  admin_rest DELETE "organizations?id=in.($org_a,$org_b)" >/dev/null 2>&1
  admin_rest DELETE "app_users?id=in.($app_a,$app_b)" >/dev/null 2>&1
  set -e
}

cleanup() {
  set +e
  cleanup_business
  for id in "$auth_user_a" "$auth_user_b"; do
    [[ -n "$id" ]] || continue
    curl -sS -o /dev/null -X DELETE "$api_url/auth/v1/admin/users/$id"       -H "apikey: $service_key" -H "Authorization: Bearer $service_key"
  done
  rm -f "$tmp_png"
}
trap cleanup EXIT
cleanup_business

create_user() {
  local email="$1" password="$2"
  curl --fail-with-body --silent --show-error -X POST "$api_url/auth/v1/admin/users"     -H "apikey: $service_key"     -H "Authorization: Bearer $service_key"     -H 'Content-Type: application/json'     --data "{\"email\":\"$email\",\"password\":\"$password\",\"email_confirm\":true}"
}

password_grant() {
  local email="$1" password="$2"
  curl --fail-with-body --silent --show-error     -X POST "$api_url/auth/v1/token?grant_type=password"     -H "apikey: $anon_key"     -H 'Content-Type: application/json'     --data "{\"email\":\"$email\",\"password\":\"$password\"}"
}

user_json="$(create_user "$email_a" "$password_a")"
auth_user_a="$(printf '%s' "$user_json" | json_get id)"
user_json="$(create_user "$email_b" "$password_b")"
auth_user_b="$(printf '%s' "$user_json" | json_get id)"

session_a="$(password_grant "$email_a" "$password_a")"
session_b="$(password_grant "$email_b" "$password_b")"
token_a="$(printf '%s' "$session_a" | json_get access_token)"
token_b="$(printf '%s' "$session_b" | json_get access_token)"
refresh_b="$(printf '%s' "$session_b" | json_get refresh_token)"
echo "::add-mask::$token_a"
echo "::add-mask::$token_b"
echo "::add-mask::$refresh_b"

issuer_a="$(jwt_claim "$token_a" iss)"
issuer_b="$(jwt_claim "$token_b" iss)"
subject_a="$(jwt_claim "$token_a" sub)"
subject_b="$(jwt_claim "$token_b" sub)"
[[ "$subject_a" == "$auth_user_a" && "$subject_b" == "$auth_user_b" ]]
[[ -n "$issuer_a" && -n "$issuer_b" ]]

admin_rest POST app_users "[
 {\"id\":\"$app_a\",\"auth_subject\":\"$auth_user_a\",\"display_name\":\"虚构验收教师甲\",\"enabled\":true},
 {\"id\":\"$app_b\",\"auth_subject\":\"$auth_user_b\",\"display_name\":\"虚构验收教师乙\",\"enabled\":true}
]"
admin_rest POST organizations "[
 {\"id\":\"$org_a\",\"name\":\"虚构验收机构甲\",\"time_zone\":\"Asia/Shanghai\"},
 {\"id\":\"$org_b\",\"name\":\"虚构验收机构乙\",\"time_zone\":\"Asia/Shanghai\"}
]"
admin_rest POST memberships "[
 {\"organization_id\":\"$org_a\",\"app_user_id\":\"$app_a\",\"membership_role\":\"owner\",\"status\":\"active\",\"can_teach\":true},
 {\"organization_id\":\"$org_b\",\"app_user_id\":\"$app_b\",\"membership_role\":\"owner\",\"status\":\"active\",\"can_teach\":true}
]"
admin_rest POST students "[
 {\"id\":\"$student_a\",\"organization_id\":\"$org_a\",\"display_name\":\"虚构验收学生甲\",\"active\":true},
 {\"id\":\"$student_b\",\"organization_id\":\"$org_b\",\"display_name\":\"虚构验收学生乙\",\"active\":true}
]"
admin_rest POST student_subject_profiles "[
 {\"id\":\"$profile_a\",\"organization_id\":\"$org_a\",\"student_id\":\"$student_a\",\"subject_key\":\"chinese\",\"active\":true},
 {\"id\":\"$profile_b\",\"organization_id\":\"$org_b\",\"student_id\":\"$student_b\",\"subject_key\":\"chinese\",\"active\":true}
]"
admin_rest POST student_teacher_assignments "[
 {\"id\":\"$assignment_a\",\"organization_id\":\"$org_a\",\"student_id\":\"$student_a\",\"subject_profile_id\":\"$profile_a\",\"teacher_app_user_id\":\"$app_a\",\"active\":true},
 {\"id\":\"$assignment_b\",\"organization_id\":\"$org_b\",\"student_id\":\"$student_b\",\"subject_profile_id\":\"$profile_b\",\"teacher_app_user_id\":\"$app_b\",\"active\":true}
]"
admin_rest POST identity_links "[
 {\"app_user_id\":\"$app_a\",\"provider_key\":\"supabase\",\"issuer\":\"$issuer_a\",\"external_subject\":\"$subject_a\",\"active\":true},
 {\"app_user_id\":\"$app_b\",\"provider_key\":\"supabase\",\"issuer\":\"$issuer_b\",\"external_subject\":\"$subject_b\",\"active\":true}
]"

call_rpc "$token_a" get_personal_bootstrap_v1 '{}'
expect_rpc_success 'Hosted identity A PersonalBootstrap'
[[ "$(printf '%s' "$rpc_body" | json_get actor.app_user_id)" == "$app_a" ]]
printf '%s' "$rpc_body" | ORG="$org_a" STUDENT="$student_a" python3 -c '
import json,os,sys
v=json.load(sys.stdin)
assert [x["organization_id"] for x in v["organizations"]] == [os.environ["ORG"]], v
assert [x["student_id"] for x in v["teaching_contexts"]] == [os.environ["STUDENT"]], v
'

call_rpc "$token_b" get_personal_bootstrap_v1 '{}'
expect_rpc_success 'Hosted identity B PersonalBootstrap'
[[ "$(printf '%s' "$rpc_body" | json_get actor.app_user_id)" == "$app_b" ]]
printf '%s' "$rpc_body" | ORG="$org_b" STUDENT="$student_b" python3 -c '
import json,os,sys
v=json.load(sys.stdin)
assert [x["organization_id"] for x in v["organizations"]] == [os.environ["ORG"]], v
assert [x["student_id"] for x in v["teaching_contexts"]] == [os.environ["STUDENT"]], v
'

call_rpc "$token_b" get_student_learning_focus_v1   "{\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\"}"
expect_rpc_denied 'Account B reading account A teaching scope'

call_rpc "$token_a" create_observation   "{\"p_operation_id\":\"$observation_operation\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\",\"p_assignment_id\":\"$assignment_a\",\"p_raw_text\":\"Hosted fictional acceptance observation.\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"hosted-production-e2e\"}}"
expect_rpc_success 'Hosted CreateObservation'
observation_id="$(printf '%s' "$rpc_body" | json_get observation_id)"

call_rpc "$token_a" get_student_learning_focus_v1   "{\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\"}"
expect_rpc_success 'Hosted Current Focus read'
[[ "$(printf '%s' "$rpc_body" | json_get contract)" == 'student_learning_focus_v1' ]]

call_rpc "$token_a" get_personal_today_actions_v1 '{}'
expect_rpc_success 'Hosted Today read'
[[ "$(printf '%s' "$rpc_body" | json_get contract)" == 'personal_today_actions_v1' ]]

printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z0xkAAAAASUVORK5CYII=' | base64 -d > "$tmp_png"
object_name="v1/org/$org_a/student/$student_a/profile/$profile_a/observation/$observation_id/attachment/$attachment_id"

upload_status="$(curl --silent --show-error -o /tmp/xq-upload.json -w '%{http_code}'   -X POST "$api_url/storage/v1/object/$bucket/$object_name"   -H "apikey: $anon_key" -H "Authorization: Bearer $token_a"   -H 'Content-Type: image/png' --data-binary "@$tmp_png")"
[[ "$upload_status" =~ ^2 ]]

call_rpc "$token_a" commit_observation_attachment   "{\"p_operation_id\":\"$attachment_operation\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\",\"p_assignment_id\":\"$assignment_a\",\"p_observation_id\":\"$observation_id\",\"p_attachment_id\":\"$attachment_id\"}"
expect_rpc_success 'Hosted Attachment commit'
[[ "$(printf '%s' "$rpc_body" | json_get object_name)" == "$object_name" ]]

read_status="$(curl --silent --show-error -o /dev/null -w '%{http_code}'   "$api_url/storage/v1/object/authenticated/$bucket/$object_name"   -H "apikey: $anon_key" -H "Authorization: Bearer $token_a")"
[[ "$read_status" =~ ^2 ]]

cross_read_status="$(curl --silent --show-error -o /dev/null -w '%{http_code}'   "$api_url/storage/v1/object/authenticated/$bucket/$object_name"   -H "apikey: $anon_key" -H "Authorization: Bearer $token_b")"
[[ ! "$cross_read_status" =~ ^2 ]]

anon_read_status="$(curl --silent --show-error -o /dev/null -w '%{http_code}'   "$api_url/storage/v1/object/authenticated/$bucket/$object_name"   -H "apikey: $anon_key")"
[[ ! "$anon_read_status" =~ ^2 ]]

headers="$(mktemp)"
region_body="$(mktemp)"
region_status="$(curl --silent --show-error --dump-header "$headers" --output "$region_body" -w '%{http_code}'   -X GET "$api_url/functions/v1/organization-invitation-delivery"   -H "apikey: $anon_key" -H "x-region: $edge_region")"
[[ "$region_status" == '405' ]]
actual_region="$(awk -F': *' 'tolower($1)=="x-sb-edge-region"{gsub("\r","",$2);print $2}' "$headers" | tail -n1)"
[[ "$actual_region" == "$edge_region" ]]
rm -f "$headers" "$region_body"

logout_status="$(curl --silent --show-error -o /dev/null -w '%{http_code}'   -X POST "$api_url/auth/v1/logout?scope=local"   -H "apikey: $anon_key" -H "Authorization: Bearer $token_b")"
[[ "$logout_status" =~ ^2 ]]

refresh_status="$(curl --silent --show-error -o /dev/null -w '%{http_code}'   -X POST "$api_url/auth/v1/token?grant_type=refresh_token"   -H "apikey: $anon_key" -H 'Content-Type: application/json'   --data "{\"refresh_token\":\"$refresh_b\"}")"
[[ ! "$refresh_status" =~ ^2 ]]

admin_rest PATCH "identity_links?app_user_id=eq.$app_a" '{"active":false}'

call_rpc "$token_a" get_personal_bootstrap_v1 '{}'
expect_rpc_denied 'Revoked IdentityLink A bootstrap'

revoked_read_status="$(curl --silent --show-error -o /dev/null -w '%{http_code}'   "$api_url/storage/v1/object/authenticated/$bucket/$object_name"   -H "apikey: $anon_key" -H "Authorization: Bearer $token_a")"
[[ ! "$revoked_read_status" =~ ^2 ]]

echo 'Hosted fictional production E2E passed: Auth -> IdentityLink -> Bootstrap -> Observation -> Attachment -> Today/Focus -> region guard -> logout/revoke -> account isolation.'
