#!/usr/bin/env bash
set -euo pipefail

: "${XUEQING_ACCEPTANCE_API_URL:?missing API URL}"
: "${XUEQING_ACCEPTANCE_PUBLISHABLE_KEY:?missing publishable key}"
: "${XUEQING_ACCEPTANCE_SERVICE_ROLE_KEY:?missing service-role key}"
: "${XUEQING_MANUAL_ACCEPTANCE_EMAIL:?missing manual acceptance email}"
: "${XUEQING_MANUAL_ACCEPTANCE_PASSWORD:?missing manual acceptance password}"

action="${1:-ensure}"
case "$action" in
  ensure|cleanup) ;;
  *) echo "usage: $0 [ensure|cleanup]" >&2; exit 2 ;;
esac

api="${XUEQING_ACCEPTANCE_API_URL%/}"
service="$XUEQING_ACCEPTANCE_SERVICE_ROLE_KEY"
publishable="$XUEQING_ACCEPTANCE_PUBLISHABLE_KEY"
email="$XUEQING_MANUAL_ACCEPTANCE_EMAIL"
password="$XUEQING_MANUAL_ACCEPTANCE_PASSWORD"

echo "::add-mask::$service"
echo "::add-mask::$email"
echo "::add-mask::$password"

app_user='91100000-0000-4000-8000-000000000001'
org='92100000-0000-4000-8000-000000000001'
student_a='93100000-0000-4000-8000-000000000001'
student_b='93100000-0000-4000-8000-000000000002'
profile_a='94100000-0000-4000-8000-000000000001'
profile_b='94100000-0000-4000-8000-000000000002'
assignment_a='95100000-0000-4000-8000-000000000001'
assignment_b='95100000-0000-4000-8000-000000000002'

admin_rest() {
  local method="$1" path="$2" body="${3:-}"
  if [[ -n "$body" ]]; then
    curl --fail-with-body --silent --show-error -X "$method" "$api/rest/v1/$path"       -H "apikey: $service"       -H "Authorization: Bearer $service"       -H 'Content-Type: application/json'       -H 'Prefer: resolution=merge-duplicates,return=minimal'       --data "$body"
  else
    curl --fail-with-body --silent --show-error -X "$method" "$api/rest/v1/$path"       -H "apikey: $service"       -H "Authorization: Bearer $service"
  fi
}

find_auth_user() {
  curl --fail-with-body --silent --show-error     "$api/auth/v1/admin/users?page=1&per_page=1000"     -H "apikey: $service"     -H "Authorization: Bearer $service" |
  EMAIL="$email" python3 -c '
import json,os,sys
payload=json.load(sys.stdin)
users=payload.get("users", payload if isinstance(payload,list) else [])
matches=[u for u in users if u.get("email","").casefold()==os.environ["EMAIL"].casefold()]
if len(matches)>1:
    raise SystemExit("manual acceptance email is not unique")
print(matches[0]["id"] if matches else "")
'
}

cleanup_business() {
  set +e
  admin_rest DELETE "identity_links?app_user_id=eq.$app_user" >/dev/null 2>&1
  admin_rest DELETE "student_teacher_assignments?id=in.($assignment_a,$assignment_b)" >/dev/null 2>&1
  admin_rest DELETE "student_subject_profiles?id=in.($profile_a,$profile_b)" >/dev/null 2>&1
  admin_rest DELETE "students?id=in.($student_a,$student_b)" >/dev/null 2>&1
  admin_rest DELETE "memberships?app_user_id=eq.$app_user" >/dev/null 2>&1
  admin_rest DELETE "organizations?id=eq.$org" >/dev/null 2>&1
  admin_rest DELETE "app_users?id=eq.$app_user" >/dev/null 2>&1
  set -e
}

auth_user="$(find_auth_user)"

if [[ "$action" == "cleanup" ]]; then
  cleanup_business
  if [[ -n "$auth_user" ]]; then
    curl --fail-with-body --silent --show-error -X DELETE       "$api/auth/v1/admin/users/$auth_user"       -H "apikey: $service"       -H "Authorization: Bearer $service" >/dev/null
  fi
  echo "Manual acceptance fixture removed."
  exit 0
fi

if [[ -z "$auth_user" ]]; then
  response="$(
    curl --fail-with-body --silent --show-error -X POST       "$api/auth/v1/admin/users"       -H "apikey: $service"       -H "Authorization: Bearer $service"       -H 'Content-Type: application/json'       --data "$(EMAIL="$email" PASSWORD="$password" python3 -c '
import json,os
print(json.dumps({
  "email": os.environ["EMAIL"],
  "password": os.environ["PASSWORD"],
  "email_confirm": True,
}, separators=(",",":")))
')"
  )"
  auth_user="$(printf '%s' "$response" | python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])')"
else
  curl --fail-with-body --silent --show-error -X PUT     "$api/auth/v1/admin/users/$auth_user"     -H "apikey: $service"     -H "Authorization: Bearer $service"     -H 'Content-Type: application/json'     --data "$(PASSWORD="$password" python3 -c '
import json,os
print(json.dumps({"password":os.environ["PASSWORD"],"email_confirm":True},separators=(",",":")))
')" >/dev/null
fi

session="$(
  curl --fail-with-body --silent --show-error -X POST     "$api/auth/v1/token?grant_type=password"     -H "apikey: $publishable"     -H 'Content-Type: application/json'     --data "$(EMAIL="$email" PASSWORD="$password" python3 -c '
import json,os
print(json.dumps({"email":os.environ["EMAIL"],"password":os.environ["PASSWORD"]},separators=(",",":")))
')"
)"
token="$(printf '%s' "$session" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')"
echo "::add-mask::$token"

read -r issuer subject < <(
  TOKEN="$token" python3 -c '
import base64,json,os
payload=os.environ["TOKEN"].split(".")[1]
payload += "=" * (-len(payload)%4)
claims=json.loads(base64.urlsafe_b64decode(payload.encode()).decode())
print(claims["iss"], claims["sub"])
'
)

cleanup_business

admin_rest POST app_users "[{\"id\":\"$app_user\",\"auth_subject\":\"$subject\",\"display_name\":\"虚构验收负责人\",\"enabled\":true}]"
admin_rest POST organizations "[{\"id\":\"$org\",\"name\":\"虚构验收机构\",\"time_zone\":\"Asia/Shanghai\"}]"
admin_rest POST memberships "[{\"organization_id\":\"$org\",\"app_user_id\":\"$app_user\",\"membership_role\":\"owner\",\"status\":\"active\",\"can_teach\":true}]"
admin_rest POST students "[
 {\"id\":\"$student_a\",\"organization_id\":\"$org\",\"display_name\":\"虚构验收学生甲\",\"active\":true},
 {\"id\":\"$student_b\",\"organization_id\":\"$org\",\"display_name\":\"虚构验收学生乙\",\"active\":true}
]"
admin_rest POST student_subject_profiles "[
 {\"id\":\"$profile_a\",\"organization_id\":\"$org\",\"student_id\":\"$student_a\",\"subject_key\":\"chinese\",\"active\":true},
 {\"id\":\"$profile_b\",\"organization_id\":\"$org\",\"student_id\":\"$student_b\",\"subject_key\":\"chinese\",\"active\":true}
]"
admin_rest POST student_teacher_assignments "[
 {\"id\":\"$assignment_a\",\"organization_id\":\"$org\",\"student_id\":\"$student_a\",\"subject_profile_id\":\"$profile_a\",\"teacher_app_user_id\":\"$app_user\",\"active\":true},
 {\"id\":\"$assignment_b\",\"organization_id\":\"$org\",\"student_id\":\"$student_b\",\"subject_profile_id\":\"$profile_b\",\"teacher_app_user_id\":\"$app_user\",\"active\":true}
]"
admin_rest POST identity_links "[{\"app_user_id\":\"$app_user\",\"provider_key\":\"supabase\",\"issuer\":\"$issuer\",\"external_subject\":\"$subject\",\"active\":true}]"

bootstrap="$(
  curl --fail-with-body --silent --show-error -X POST     "$api/rest/v1/rpc/get_personal_bootstrap_v1"     -H "apikey: $publishable"     -H "Authorization: Bearer $token"     -H 'Content-Type: application/json'     --data '{}'
)"
printf '%s' "$bootstrap" | ORG="$org" python3 -c '
import json,os,sys
value=json.load(sys.stdin)
assert value["actor"]["app_user_id"]=="91100000-0000-4000-8000-000000000001", value
assert any(x["organization_id"]==os.environ["ORG"] for x in value["organizations"]), value
assert len(value["teaching_contexts"]) >= 2, value
'

echo "Manual acceptance account and fictional teaching scope are ready."
