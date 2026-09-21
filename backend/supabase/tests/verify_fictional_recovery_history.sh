#!/usr/bin/env bash
set -euo pipefail

history_file="${1:?usage: verify_fictional_recovery_history.sh HISTORY_JSON}"
: "${API_URL:?API_URL required}"
: "${ANON_KEY:?ANON_KEY required}"
: "${SERVICE_ROLE_KEY:?SERVICE_ROLE_KEY required}"
: "${DB_CONTAINER:?DB_CONTAINER required}"
: "${ACTOR_TOKEN_A:?ACTOR_TOKEN_A required}"
: "${ACTOR_TOKEN_B:?ACTOR_TOKEN_B required}"
: "${REVOKED_TOKEN:?REVOKED_TOKEN required}"

api_url="${API_URL%/}"

jget() {
  local key="$1"
  python3 - "$history_file" "$key" <<'PY'
import json
import pathlib
import sys
value=json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
print(value[sys.argv[2]])
PY
}

rpc_call() {
  local token="$1" fn="$2" payload="$3" out
  out="$(mktemp)"
  RPC_STATUS="$(
    curl --silent --show-error --output "$out" --write-out '%{http_code}' \
      -X POST "$api_url/rest/v1/rpc/$fn" \
      -H "apikey: $ANON_KEY" \
      -H "Authorization: Bearer $token" \
      -H 'Content-Type: application/json' \
      --data "$payload" || true
  )"
  RPC_BODY="$(cat "$out")"
  rm -f "$out"
}

json_value() {
  local key="$1"
  printf '%s' "$RPC_BODY" | python3 -c '
import json,sys
key=sys.argv[1]
value=json.load(sys.stdin).get(key)
if isinstance(value,bool):
    print("true" if value else "false")
elif value is None:
    print("")
else:
    print(value)
' "$key"
}

require_success() {
  local label="$1"
  if [[ ! "$RPC_STATUS" =~ ^2 ]]; then
    echo "$label failed: HTTP $RPC_STATUS $RPC_BODY" >&2
    exit 1
  fi
}

actor_a="$(jget actor_a)"
actor_b="$(jget actor_b)"
org_a="$(jget org_a)"
student_a="$(jget student_a)"
profile_a="$(jget profile_a)"
case_assignment="$(jget case_assignment)"
handoff_student="$(jget handoff_student)"
handoff_profile="$(jget handoff_profile)"
assignment_a_old="$(jget assignment_a_old)"
assignment_a_new="$(jget assignment_a_new)"

case_a_id="$(jget case_a_id)"
case_a_action_id="$(jget case_a_action_id)"
case_a_next_action_id="$(jget case_a_next_action_id)"
case_a_verify="$(jget case_a_verify)"

case_b_id="$(jget case_b_id)"
case_b_reopened_action_id="$(jget case_b_reopened_action_id)"
case_b_reopen="$(jget case_b_reopen)"

invitation_id="$(jget invitation_id)"
delivery_operation="$(jget delivery_operation)"
delivery_id="$(jget delivery_id)"

# Verification/next-action operation identity survived restore.
case_a_payload="{\"p_operation_id\":\"$case_a_verify\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\",\"p_owner_assignment_id\":\"$case_assignment\",\"p_case_id\":\"$case_a_id\",\"p_current_primary_action_id\":\"$case_a_action_id\",\"p_expected_case_version\":1,\"p_expected_action_version\":1,\"p_verification_outcome\":\"partially_met\",\"p_verification_summary\":\"Backup fixture verification preserved.\",\"p_next_action_text\":\"Backup fixture next action\",\"p_next_action_due_on\":\"2026-09-24\"}"
rpc_call "$ACTOR_TOKEN_A" record_verification_and_next_action "$case_a_payload"
require_success "Restored Verification receipt replay"
[[ "$(json_value next_primary_action_id)" == "$case_a_next_action_id" ]] || {
  echo "Verification replay returned a different next primary Action." >&2
  exit 1
}

different_case_a_payload="${case_a_payload/Backup fixture next action/Different restored action must fail}"
rpc_call "$ACTOR_TOKEN_A" record_verification_and_next_action "$different_case_a_payload"
if [[ "$RPC_STATUS" =~ ^2 ]] || [[ "$RPC_BODY" != *"XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD"* ]]; then
  echo "Verification operation different-payload replay did not fail closed: $RPC_STATUS $RPC_BODY" >&2
  exit 1
fi

# Closed->reopen lifecycle receipt survived restore even though the Case is now
# already intervening with the reopened primary Action.
case_b_reopen_payload="{\"p_operation_id\":\"$case_b_reopen\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\",\"p_owner_assignment_id\":\"$case_assignment\",\"p_case_id\":\"$case_b_id\",\"p_expected_case_version\":6,\"p_new_primary_action_text\":\"Recovered reopened primary action\",\"p_new_primary_action_due_on\":\"2026-09-27\"}"
rpc_call "$ACTOR_TOKEN_A" reopen_learning_case "$case_b_reopen_payload"
require_success "Restored Case reopen receipt replay"
[[ "$(json_value new_primary_action_id)" == "$case_b_reopened_action_id" ]] || {
  echo "Reopen replay returned a different primary Action." >&2
  exit 1
}
[[ "$(json_value case_state)" == "intervening" ]] || {
  echo "Reopen replay did not preserve intervening state." >&2
  exit 1
}

# Handoff continuity: former Teacher A is no longer authoritative on the
# handed-off teaching scope. Runtime-random operation ids prevent a restored
# operation receipt from short-circuiting the authorization gate and producing
# a false-positive "success".
new_operation_id() {
  python3 -c 'import uuid; print(uuid.uuid4())'
}

former_operation="$(new_operation_id)"
current_operation="$(new_operation_id)"
revoked_operation="$(new_operation_id)"

docker exec "$DB_CONTAINER" psql -U postgres -d postgres -v ON_ERROR_STOP=1 \
  -v assignment_old="$assignment_a_old" \
  -v assignment_new="$assignment_a_new" \
  -v actor_a="$actor_a" \
  -v actor_b="$actor_b" \
  -v former_operation="$former_operation" \
  -v current_operation="$current_operation" \
  -v revoked_operation="$revoked_operation" >/dev/null <<'SQL'
do $xq$
begin
    if not exists (
        select 1
          from public.student_teacher_assignments
         where id = :'assignment_old'::uuid
           and teacher_app_user_id = :'actor_a'::uuid
           and not active
    ) then
        raise exception 'XQ_RESTORE_FORMER_ASSIGNMENT_NOT_INACTIVE_BEFORE_AUTH_TEST';
    end if;

    if not exists (
        select 1
          from public.student_teacher_assignments
         where id = :'assignment_new'::uuid
           and teacher_app_user_id = :'actor_b'::uuid
           and active
    ) then
        raise exception 'XQ_RESTORE_CURRENT_ASSIGNMENT_NOT_ACTIVE_BEFORE_AUTH_TEST';
    end if;

    if exists (
        select 1
          from public.operation_receipts
         where operation_id in (
             :'former_operation'::uuid,
             :'current_operation'::uuid,
             :'revoked_operation'::uuid
         )
    ) then
        raise exception 'XQ_RESTORE_AUTH_TEST_OPERATION_ID_COLLISION';
    end if;
end;
$xq$;
SQL

api_assignment_snapshot="$(
  curl --fail-with-body --silent --show-error \
    "$api_url/rest/v1/student_teacher_assignments?id=in.($assignment_a_old,$assignment_a_new)&select=id,teacher_app_user_id,active&order=id.asc" \
    -H "apikey: $SERVICE_ROLE_KEY" \
    -H "Authorization: Bearer $SERVICE_ROLE_KEY"
)"

API_ASSIGNMENT_SNAPSHOT="$api_assignment_snapshot" \
ASSIGNMENT_OLD="$assignment_a_old" ASSIGNMENT_NEW="$assignment_a_new" \
ACTOR_A="$actor_a" ACTOR_B="$actor_b" \
python3 - <<'PY'
import json
import os

rows = {
    row["id"]: row
    for row in json.loads(os.environ["API_ASSIGNMENT_SNAPSHOT"])
}
old = rows.get(os.environ["ASSIGNMENT_OLD"])
new = rows.get(os.environ["ASSIGNMENT_NEW"])
if old is None or new is None:
    raise SystemExit(f"Data API assignment snapshot incomplete: {rows}")
if old["teacher_app_user_id"] != os.environ["ACTOR_A"] or old["active"] is not False:
    raise SystemExit(f"Data API former assignment state mismatch: {old}")
if new["teacher_app_user_id"] != os.environ["ACTOR_B"] or new["active"] is not True:
    raise SystemExit(f"Data API current assignment state mismatch: {new}")
PY

rpc_call "$ACTOR_TOKEN_A" get_personal_bootstrap_v1 '{}'
require_success "Former teacher restored personal bootstrap"
ACTOR_A_BOOTSTRAP="$RPC_BODY" ASSIGNMENT_OLD="$assignment_a_old" python3 - <<'PY'
import json
import os

value = json.loads(os.environ["ACTOR_A_BOOTSTRAP"])
contexts = value.get("teaching_contexts", [])
if any(item.get("assignment_id") == os.environ["ASSIGNMENT_OLD"] for item in contexts):
    raise SystemExit("Former teacher bootstrap still exposes inactive assignment")
PY

rpc_call "$ACTOR_TOKEN_B" get_personal_bootstrap_v1 '{}'
require_success "Current teacher restored personal bootstrap"
ACTOR_B_BOOTSTRAP="$RPC_BODY" ASSIGNMENT_NEW="$assignment_a_new" python3 - <<'PY'
import json
import os

value = json.loads(os.environ["ACTOR_B_BOOTSTRAP"])
contexts = value.get("teaching_contexts", [])
if not any(item.get("assignment_id") == os.environ["ASSIGNMENT_NEW"] for item in contexts):
    raise SystemExit("Current teacher bootstrap is missing active handoff assignment")
PY

former_payload="{\"p_operation_id\":\"$former_operation\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$handoff_student\",\"p_subject_profile_id\":\"$handoff_profile\",\"p_assignment_id\":\"$assignment_a_old\",\"p_raw_text\":\"Former teacher must not append after restored handoff.\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"restore-handoff-negative\"}}"
rpc_call "$ACTOR_TOKEN_A" create_observation "$former_payload"
if [[ "$RPC_STATUS" =~ ^2 ]] || [[ "$RPC_BODY" != *"XQ_TEACHER_ASSIGNMENT_REQUIRED"* ]]; then
  echo "Former teacher unexpectedly retained Teaching Fact authority: $RPC_STATUS $RPC_BODY" >&2
  exit 1
fi

# Current Teacher B can continue after explicit fresh-provider relink.
current_payload="{\"p_operation_id\":\"$current_operation\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$handoff_student\",\"p_subject_profile_id\":\"$handoff_profile\",\"p_assignment_id\":\"$assignment_a_new\",\"p_raw_text\":\"Current teacher continues after restored handoff.\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"restore-handoff-positive\"}}"
rpc_call "$ACTOR_TOKEN_B" create_observation "$current_payload"
require_success "Current teacher Teaching Fact after restored handoff"

handoff_observation_id="$(json_value observation_id)"
[[ -n "$handoff_observation_id" ]] || {
  echo "Current teacher restore check returned no Observation id." >&2
  exit 1
}

# Explicitly revoked legacy link must not become authoritative simply because a
# cryptographically valid token presents the old tuple.
revoked_payload="{\"p_operation_id\":\"$revoked_operation\",\"p_organization_id\":\"$org_a\",\"p_student_id\":\"$student_a\",\"p_subject_profile_id\":\"$profile_a\",\"p_assignment_id\":\"$case_assignment\",\"p_raw_text\":\"Revoked identity must remain denied.\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"restore-revoked-negative\"}}"
rpc_call "$REVOKED_TOKEN" create_observation "$revoked_payload"
if [[ "$RPC_STATUS" =~ ^2 ]] || [[ "$RPC_BODY" != *"XQ_ACTOR_NOT_FOUND"* ]]; then
  echo "Revoked provider identity unexpectedly regained authority: $RPC_STATUS $RPC_BODY" >&2
  exit 1
fi

# A sent delivery must replay as sent and never authorize another dispatch.
delivery_payload="{\"p_operation_id\":\"$delivery_operation\",\"p_invitation_id\":\"$invitation_id\"}"
rpc_call "$ACTOR_TOKEN_A" begin_organization_invitation_delivery_v1 "$delivery_payload"
require_success "Restored sent Invitation Delivery replay"
[[ "$(json_value delivery_id)" == "$delivery_id" ]] || {
  echo "Invitation Delivery replay returned a different delivery id." >&2
  exit 1
}
[[ "$(json_value state)" == "sent" ]] || {
  echo "Invitation Delivery replay did not preserve sent state." >&2
  exit 1
}
[[ "$(json_value should_dispatch)" == "false" ]] || {
  echo "Restored sent Invitation unexpectedly authorized another dispatch." >&2
  exit 1
}

different_delivery_payload="{\"p_operation_id\":\"$delivery_operation\",\"p_invitation_id\":\"00000000-0000-0000-0000-000000000001\"}"
rpc_call "$ACTOR_TOKEN_A" begin_organization_invitation_delivery_v1 "$different_delivery_payload"
if [[ "$RPC_STATUS" =~ ^2 ]] || [[ "$RPC_BODY" != *"XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD"* ]]; then
  echo "Invitation Delivery different-payload replay did not fail closed: $RPC_STATUS $RPC_BODY" >&2
  exit 1
fi

# Direct state assertions keep the recovery semantics explicit and auditable.
docker exec "$DB_CONTAINER" psql -U postgres -d postgres -v ON_ERROR_STOP=1 \
  -v actor_a="$actor_a" \
  -v actor_b="$actor_b" \
  -v assignment_old="$assignment_a_old" \
  -v assignment_new="$assignment_a_new" \
  -v case_a="$case_a_id" \
  -v case_b="$case_b_id" \
  -v delivery="$delivery_id" \
  -v handoff_observation="$handoff_observation_id" >/dev/null <<'SQL'
do $xq$
begin
    if exists (
        select 1 from public.student_teacher_assignments
         where id = :'assignment_old'::uuid and active
    ) then
        raise exception 'XQ_RESTORE_FORMER_ASSIGNMENT_REACTIVATED';
    end if;
    if not exists (
        select 1 from public.student_teacher_assignments
         where id = :'assignment_new'::uuid
           and teacher_app_user_id = :'actor_b'::uuid
           and active
    ) then
        raise exception 'XQ_RESTORE_CURRENT_ASSIGNMENT_MISSING';
    end if;
    if not exists (
        select 1 from public.learning_cases
         where id = :'case_a'::uuid and version = 2
    ) then
        raise exception 'XQ_RESTORE_VERIFICATION_CASE_VERSION_LOST';
    end if;
    if not exists (
        select 1 from public.learning_cases
         where id = :'case_b'::uuid and state = 'intervening' and version = 7
    ) then
        raise exception 'XQ_RESTORE_LIFECYCLE_STATE_LOST';
    end if;
    if not exists (
        select 1 from public.identity_links
         where app_user_id = '10000000-0000-0000-0000-000000000003'::uuid
           and provider_key = 'supabase'
           and issuer = 'supabase-demo'
           and external_subject = 'a0000000-0000-0000-0000-000000000003'
           and not active
    ) then
        raise exception 'XQ_RESTORE_REVOKED_IDENTITY_LOST';
    end if;
    if not exists (
        select 1 from public.organization_invitation_deliveries
         where id = :'delivery'::uuid and status = 'sent'
    ) then
        raise exception 'XQ_RESTORE_SENT_DELIVERY_LOST';
    end if;
    if not exists (
        select 1 from public.observations
         where id = :'handoff_observation'::uuid
           and actor_app_user_id = :'actor_b'::uuid
    ) then
        raise exception 'XQ_RESTORE_CURRENT_TEACHER_APPEND_NOT_RECORDED';
    end if;
end;
$xq$;
SQL

echo "Restored business history semantics passed: Case/Action replay, lifecycle, handoff, revoked identity and sent Invitation Delivery remain coherent."
