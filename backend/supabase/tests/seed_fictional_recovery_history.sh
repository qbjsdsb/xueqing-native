#!/usr/bin/env bash
set -euo pipefail

output_file="${1:?usage: seed_fictional_recovery_history.sh OUTPUT_JSON}"
: "${API_URL:?API_URL required}"
: "${ANON_KEY:?ANON_KEY required}"
: "${SERVICE_ROLE_KEY:?SERVICE_ROLE_KEY required}"
: "${ACTOR_TOKEN:?ACTOR_TOKEN required}"
: "${DB_CONTAINER:?DB_CONTAINER required}"

api_url="${API_URL%/}"

rpc() {
  local token="$1" fn="$2" payload="$3"
  local body status operation_id
  body="$(mktemp)"
  status="$(
    curl --silent --show-error --output "$body" --write-out '%{http_code}' \
      -X POST "$api_url/rest/v1/rpc/$fn" \
      -H "apikey: $ANON_KEY" \
      -H "Authorization: Bearer $token" \
      -H 'Content-Type: application/json' \
      --data "$payload" || true
  )"
  if [[ ! "$status" =~ ^2 ]]; then
    operation_id="$(
      printf '%s' "$payload" | python3 -c '
import json,sys
try:
    print(json.load(sys.stdin).get("p_operation_id", ""))
except Exception:
    print("")
'
    )"
    echo "Recovery-history RPC failed: fn=$fn status=$status operation_id=$operation_id" >&2
    cat "$body" >&2 || true
    rm -f "$body"
    return 1
  fi
  cat "$body"
  rm -f "$body"
}

service_rpc() {
  local fn="$1" payload="$2"
  local body status operation_id
  body="$(mktemp)"
  status="$(
    curl --silent --show-error --output "$body" --write-out '%{http_code}' \
      -X POST "$api_url/rest/v1/rpc/$fn" \
      -H "apikey: $SERVICE_ROLE_KEY" \
      -H "Authorization: Bearer $SERVICE_ROLE_KEY" \
      -H 'Content-Type: application/json' \
      --data "$payload" || true
  )"
  if [[ ! "$status" =~ ^2 ]]; then
    operation_id="$(
      printf '%s' "$payload" | python3 -c '
import json,sys
try:
    print(json.load(sys.stdin).get("p_operation_id", ""))
except Exception:
    print("")
'
    )"
    echo "Recovery-history service RPC failed: fn=$fn status=$status operation_id=$operation_id" >&2
    cat "$body" >&2 || true
    rm -f "$body"
    return 1
  fi
  cat "$body"
  rm -f "$body"
}

json_get() {
  local expression="$1"
  python3 -c '
import json,sys
expr=sys.argv[1]
value=json.load(sys.stdin)
for part in expr.split("."):
    value=value[int(part)] if isinstance(value,list) else value[part]
print("" if value is None else value)
' "$expression"
}

actor_a='10000000-0000-0000-0000-000000000001'
actor_b='10000000-0000-0000-0000-000000000002'

org_a='20000000-0000-0000-0000-000000000001'
student_a='30000000-0000-0000-0000-000000000001'
profile_a='40000000-0000-0000-0000-000000000001'
case_assignment='50000000-0000-0000-0000-000000000001'
handoff_student='30000000-0000-0000-0000-000000000003'
handoff_profile='40000000-0000-0000-0000-000000000003'
assignment_a_old='50000000-0000-0000-0000-000000000103'
assignment_a_new='50000000-0000-0000-0000-000000000104'

# Case A: Verification + Next Action.
case_a_create='76000000-0000-4000-8000-000000000301'
case_a_verify='76000000-0000-4000-8000-000000000302'
rpc "$ACTOR_TOKEN" create_learning_case   "{\"p_operation_id\":\"$case_a_create\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\",\"p_owner_assignment_id\":\"$case_assignment\",\"p_title\":\"Backup fixture verification case\",\"p_primary_action_text\":\"Complete first verification task\",\"p_primary_action_due_on\":\"2026-09-23\"}" >/dev/null

case_a_id="$(
  docker exec "$DB_CONTAINER" psql -U postgres -d postgres -Atc     "select id from public.learning_cases where title='Backup fixture verification case'"
)"
case_a_action_id="$(
  docker exec "$DB_CONTAINER" psql -U postgres -d postgres -Atc     "select id from public.learning_case_actions where case_id='$case_a_id'::uuid and status='pending' order by created_at_server limit 1"
)"

rpc "$ACTOR_TOKEN" record_verification_and_next_action   "{\"p_operation_id\":\"$case_a_verify\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\",\"p_owner_assignment_id\":\"$case_assignment\",\"p_case_id\":\"$case_a_id\",\"p_current_primary_action_id\":\"$case_a_action_id\",\"p_expected_case_version\":1,\"p_expected_action_version\":1,\"p_verification_outcome\":\"partially_met\",\"p_verification_summary\":\"Backup fixture verification preserved.\",\"p_next_action_text\":\"Backup fixture next action\",\"p_next_action_due_on\":\"2026-09-24\"}" >/dev/null

case_a_next_action_id="$(
  docker exec "$DB_CONTAINER" psql -U postgres -d postgres -Atc     "select id from public.learning_case_actions where case_id='$case_a_id'::uuid and status='pending' order by created_at_server desc limit 1"
)"

# Case B: full lifecycle to closed then reopened.
case_b_create='76000000-0000-4000-8000-000000000401'
case_b_confirm='76000000-0000-4000-8000-000000000402'
case_b_intervene='76000000-0000-4000-8000-000000000403'
case_b_pending_verify='76000000-0000-4000-8000-000000000404'
case_b_stable='76000000-0000-4000-8000-000000000405'
case_b_close='76000000-0000-4000-8000-000000000406'
case_b_reopen='76000000-0000-4000-8000-000000000407'

rpc "$ACTOR_TOKEN" create_learning_case   "{\"p_operation_id\":\"$case_b_create\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\",\"p_owner_assignment_id\":\"$case_assignment\",\"p_title\":\"Backup fixture lifecycle case\",\"p_primary_action_text\":\"Lifecycle fixture primary action\",\"p_primary_action_due_on\":\"2026-09-23\"}" >/dev/null

case_b_id="$(
  docker exec "$DB_CONTAINER" psql -U postgres -d postgres -Atc     "select id from public.learning_cases where title='Backup fixture lifecycle case'"
)"
case_b_action_id="$(
  docker exec "$DB_CONTAINER" psql -U postgres -d postgres -Atc     "select id from public.learning_case_actions where case_id='$case_b_id'::uuid and status='pending' order by created_at_server limit 1"
)"

transition() {
  local operation="$1" expected="$2" target="$3"
  rpc "$ACTOR_TOKEN" transition_learning_case_state     "{\"p_operation_id\":\"$operation\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\",\"p_owner_assignment_id\":\"$case_assignment\",\"p_case_id\":\"$case_b_id\",\"p_expected_case_version\":$expected,\"p_target_state\":\"$target\"}" >/dev/null
}
transition "$case_b_confirm" 1 confirmed
transition "$case_b_intervene" 2 intervening
transition "$case_b_pending_verify" 3 pending_verification
transition "$case_b_stable" 4 stable

rpc "$ACTOR_TOKEN" close_learning_case   "{\"p_operation_id\":\"$case_b_close\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\",\"p_owner_assignment_id\":\"$case_assignment\",\"p_case_id\":\"$case_b_id\",\"p_primary_action_id\":\"$case_b_action_id\",\"p_expected_case_version\":5,\"p_expected_action_version\":1}" >/dev/null

rpc "$ACTOR_TOKEN" reopen_learning_case   "{\"p_operation_id\":\"$case_b_reopen\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\",\"p_owner_assignment_id\":\"$case_assignment\",\"p_case_id\":\"$case_b_id\",\"p_expected_case_version\":6,\"p_new_primary_action_text\":\"Recovered reopened primary action\",\"p_new_primary_action_due_on\":\"2026-09-27\"}" >/dev/null

case_b_reopened_action_id="$(
  docker exec "$DB_CONTAINER" psql -U postgres -d postgres -Atc     "select id from public.learning_case_actions where case_id='$case_b_id'::uuid and status='pending' order by created_at_server desc limit 1"
)"

# Org A handoff is isolated on Student C so the baseline Observation/Attachment
# replay for Student A remains independently valid.
docker exec "$DB_CONTAINER" psql -U postgres -d postgres -v ON_ERROR_STOP=1 >/dev/null <<SQL
begin;

insert into public.student_subject_profiles (
    id, organization_id, student_id, subject_key, active
) values (
    '$handoff_profile'::uuid,
    '$org_a'::uuid,
    '$handoff_student'::uuid,
    'chinese',
    true
);

insert into public.student_teacher_assignments (
    id, organization_id, student_id, subject_profile_id, teacher_app_user_id, active
) values (
    '$assignment_a_old'::uuid,
    '$org_a'::uuid,
    '$handoff_student'::uuid,
    '$handoff_profile'::uuid,
    '$actor_a'::uuid,
    false
);

insert into public.student_teacher_assignments (
    id, organization_id, student_id, subject_profile_id, teacher_app_user_id, active
) values (
    '$assignment_a_new'::uuid,
    '$org_a'::uuid,
    '$handoff_student'::uuid,
    '$handoff_profile'::uuid,
    '$actor_b'::uuid,
    true
);
commit;
SQL

# Preserve one explicitly revoked provider link independently of the recovered
# Teacher A/B links.
docker exec "$DB_CONTAINER" psql -U postgres -d postgres -v ON_ERROR_STOP=1 >/dev/null <<'SQL'
update public.identity_links
   set active = false
 where app_user_id = '10000000-0000-0000-0000-000000000003'::uuid
   and provider_key = 'supabase'
   and external_subject = 'a0000000-0000-0000-0000-000000000003';
SQL

# Sent invitation delivery: restore must never authorize a second dispatch.
invitation_create='76000000-0000-4000-8000-000000000501'
delivery_operation='76000000-0000-4000-8000-000000000502'
invitation_response="$(
  rpc "$ACTOR_TOKEN" create_organization_invitation_v1     "{\"p_operation_id\":\"$invitation_create\",\"p_organization_id\":\"$org_a\",\"p_invited_email\":\"backup.delivery@example.com\",\"p_target_role\":\"teacher\",\"p_target_can_teach\":true}"
)"
invitation_id="$(printf '%s' "$invitation_response" | json_get invitation_id)"

delivery_response="$(
  rpc "$ACTOR_TOKEN" begin_organization_invitation_delivery_v1     "{\"p_operation_id\":\"$delivery_operation\",\"p_invitation_id\":\"$invitation_id\"}"
)"
delivery_id="$(printf '%s' "$delivery_response" | json_get delivery_id)"
should_dispatch="$(printf '%s' "$delivery_response" | json_get should_dispatch)"
[[ "$should_dispatch" == "True" || "$should_dispatch" == "true" ]] || {
  echo "Initial invitation delivery did not authorize dispatch." >&2
  exit 1
}

service_rpc complete_organization_invitation_delivery_v1   "{\"p_operation_id\":\"$delivery_operation\",\"p_delivery_id\":\"$delivery_id\"}" >/dev/null

python3 - "$output_file" <<PY
import json, pathlib, sys
value = {
  "org_a": "$org_a",
  "student_a": "$student_a",
  "profile_a": "$profile_a",
  "handoff_student": "$handoff_student",
  "handoff_profile": "$handoff_profile",
  "assignment_a_old": "$assignment_a_old",
  "assignment_a_new": "$assignment_a_new",
  "actor_a": "$actor_a",
  "actor_b": "$actor_b",
  "case_assignment": "$case_assignment",
  "case_a_id": "$case_a_id",
  "case_a_action_id": "$case_a_action_id",
  "case_a_next_action_id": "$case_a_next_action_id",
  "case_a_create": "$case_a_create",
  "case_a_verify": "$case_a_verify",
  "case_b_id": "$case_b_id",
  "case_b_action_id": "$case_b_action_id",
  "case_b_reopened_action_id": "$case_b_reopened_action_id",
  "case_b_create": "$case_b_create",
  "case_b_confirm": "$case_b_confirm",
  "case_b_intervene": "$case_b_intervene",
  "case_b_pending_verify": "$case_b_pending_verify",
  "case_b_stable": "$case_b_stable",
  "case_b_close": "$case_b_close",
  "case_b_reopen": "$case_b_reopen",
  "invitation_create": "$invitation_create",
  "invitation_id": "$invitation_id",
  "delivery_operation": "$delivery_operation",
  "delivery_id": "$delivery_id"
}
pathlib.Path(sys.argv[1]).write_text(
    json.dumps(value, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
)
PY

echo "Fictional recovery history seeded: cases, lifecycle, handoff, revoked link and sent invitation delivery."
