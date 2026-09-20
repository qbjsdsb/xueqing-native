#!/usr/bin/env bash
set -euo pipefail

bucket='teaching-attachments-v1'
actor_a='10000000-0000-0000-0000-000000000001'
actor_b='10000000-0000-0000-0000-000000000002'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_a='50000000-0000-0000-0000-000000000001'
assignment_b='50000000-0000-0000-0000-00000000a002'
observation_operation='99300000-0000-0000-0000-000000000001'
attachment_id='99400000-0000-0000-0000-000000000001'
attachment_operation='99500000-0000-0000-0000-000000000001'
b_denied_attachment='99400000-0000-0000-0000-000000000002'
atomic_attachment='99400000-0000-0000-0000-000000000003'
atomic_operation='99500000-0000-0000-0000-000000000003'

status_env="$(mktemp)"
supabase status --workdir backend -o env > "$status_env"
set -a
# shellcheck disable=SC1090
source "$status_env"
set +a
rm -f "$status_env"

: "\${API_URL:?API_URL missing from local Supabase status}"
: "\${ANON_KEY:?ANON_KEY missing from local Supabase status}"
: "\${SERVICE_ROLE_KEY:?SERVICE_ROLE_KEY missing from local Supabase status}"
api_url="\${API_URL%/}"

echo "::add-mask::$ANON_KEY"
echo "::add-mask::$SERVICE_ROLE_KEY"

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
import json,sys
expression=sys.argv[1]
current=json.load(sys.stdin)
for part in expression.split("."):
    current=current[int(part)] if isinstance(current,list) else current[part]
if isinstance(current,(dict,list)):
    print(json.dumps(current,ensure_ascii=False,separators=(",",":")))
elif current is None:
    print("")
else:
    print(current)
' "$expression"
}

jwt_claim() {
  local token="$1" claim="$2"
  python3 -c '
import base64,json,sys
token,claim=sys.argv[1],sys.argv[2]
payload=token.split(".")[1]
payload += "=" * (-len(payload)%4)
claims=json.loads(base64.urlsafe_b64decode(payload.encode()).decode())
value=claims.get(claim)
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

create_auth_session() {
  local label="$1" actor_id="$2"
  local email="attachment-$label-\${GITHUB_RUN_ID:-local}-\${GITHUB_RUN_ATTEMPT:-0}@example.com"
  local password
  password="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+"".join(secrets.choice(a) for _ in range(24)))')"
  echo "::add-mask::$password" >&2

  local user_response user_id session token issuer subject
  user_response="$(curl -fsS -X POST "$api_url/auth/v1/admin/users" \
    -H "apikey: $SERVICE_ROLE_KEY" \
    -H "Authorization: Bearer $SERVICE_ROLE_KEY" \
    -H 'Content-Type: application/json' \
    --data "{\"email\":\"$email\",\"password\":\"$password\",\"email_confirm\":true}")"
  user_id="$(printf '%s' "$user_response" | json_get id)"
  session="$(curl -fsS -X POST "$api_url/auth/v1/token?grant_type=password" \
    -H "apikey: $ANON_KEY" \
    -H 'Content-Type: application/json' \
    --data "{\"email\":\"$email\",\"password\":\"$password\"}")"
  token="$(printf '%s' "$session" | json_get access_token)"
  issuer="$(jwt_claim "$token" iss)"
  subject="$(jwt_claim "$token" sub)"

  [[ -n "$user_id" && -n "$token" && -n "$issuer" && "$subject" == "$user_id" ]] || {
    echo "Failed to create provider session for $label." >&2
    exit 1
  }
  echo "::add-mask::$token" >&2

  psql_db >/dev/null <<SQL
insert into public.identity_links (
  app_user_id, provider_key, issuer, external_subject, active
) values (
  '$actor_id'::uuid, 'supabase', '$issuer', '$subject', true
)
on conflict (provider_key, issuer, external_subject)
do update set app_user_id = excluded.app_user_id, active = true;
SQL

  printf '%s|%s\n' "$user_id" "$token"
}

rpc_status=''
rpc_body=''
call_rpc() {
  local token="$1" function_name="$2" payload="$3" out
  out="$(mktemp)"
  rpc_status="$(curl -sS -o "$out" -w '%{http_code}' \
    -X POST "$api_url/rest/v1/rpc/$function_name" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $token" \
    -H 'Content-Type: application/json' \
    --data "$payload")"
  rpc_body="$(cat "$out")"
  rm -f "$out"
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
  assert_json "$rpc_body" message "$expected"
}

storage_status=''
storage_body=''
upload_object() {
  local token="$1" object_name="$2" file="$3" out
  out="$(mktemp)"
  storage_status="$(curl -sS -o "$out" -w '%{http_code}' \
    -X POST "$api_url/storage/v1/object/$bucket/$object_name" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $token" \
    -H 'Content-Type: image/png' \
    --data-binary "@$file")"
  storage_body="$(cat "$out")"
  rm -f "$out"
}

download_object() {
  local token="$1" object_name="$2" out
  out="$(mktemp)"
  storage_status="$(curl -sS -o "$out" -w '%{http_code}' \
    -X GET "$api_url/storage/v1/object/authenticated/$bucket/$object_name" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $token")"
  storage_body="$(cat "$out" 2>/dev/null || true)"
  rm -f "$out"
}

expect_storage_success() {
  local label="$1"
  if [[ ! "$storage_status" =~ ^2 ]]; then
    echo "$label expected Storage success, got HTTP $storage_status: $storage_body" >&2
    exit 1
  fi
}

expect_storage_denied() {
  local label="$1"
  if [[ "$storage_status" =~ ^2 ]]; then
    echo "$label unexpectedly succeeded: $storage_body" >&2
    exit 1
  fi
}

user_a=''
user_b=''
token_a=''
token_b=''
object_name=''
atomic_object_name=''
tmp_png="$(mktemp --suffix=.png)"

cleanup() {
  set +e
  psql_db >/dev/null 2>&1 <<SQL
reset role;
drop trigger if exists force_attachment_e2e_receipt_failure on public.operation_receipts;
drop function if exists public.test_fail_attachment_e2e_receipt();
delete from public.observation_attachments
 where operation_id in ('$attachment_operation'::uuid, '$atomic_operation'::uuid);
delete from public.operation_receipts
 where operation_id in (
   '$observation_operation'::uuid,
   '$attachment_operation'::uuid,
   '$atomic_operation'::uuid
 );
delete from public.observations where operation_id = '$observation_operation'::uuid;
delete from public.student_teacher_assignments where id = '$assignment_b'::uuid;
update public.student_teacher_assignments set active = true where id = '$assignment_a'::uuid;
delete from public.identity_links
 where provider_key = 'supabase'
   and external_subject in ('$user_a', '$user_b');
SQL

  for path in "$object_name" "$atomic_object_name"; do
    if [[ -n "$path" ]]; then
      curl -sS -o /dev/null -X DELETE \
        "$api_url/storage/v1/object/$bucket/$path" \
        -H "apikey: $SERVICE_ROLE_KEY" \
        -H "Authorization: Bearer $SERVICE_ROLE_KEY" || true
    fi
  done

  for id in "$user_a" "$user_b"; do
    if [[ -n "$id" ]]; then
      curl -sS -o /dev/null -X DELETE "$api_url/auth/v1/admin/users/$id" \
        -H "apikey: $SERVICE_ROLE_KEY" \
        -H "Authorization: Bearer $SERVICE_ROLE_KEY" || true
    fi
  done

  rm -f "$tmp_png"
}
trap cleanup EXIT

printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z0xkAAAAASUVORK5CYII=' \
  | base64 -d > "$tmp_png"

session_a="$(create_auth_session a "$actor_a")"
user_a="\${session_a%%|*}"
token_a="\${session_a#*|}"
session_b="$(create_auth_session b "$actor_b")"
user_b="\${session_b%%|*}"
token_b="\${session_b#*|}"

call_rpc "$token_a" create_observation \
  "{\"p_operation_id\":\"$observation_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_a\",\"p_raw_text\":\"Attachment Storage E2E fictional observation.\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"attachment-storage-e2e\"}}"
expect_success 'Create parent Observation'
observation_id="$(printf '%s' "$rpc_body" | json_get observation_id)"
[[ -n "$observation_id" ]] || { echo 'Observation receipt returned no id.' >&2; exit 1; }

object_name="v1/org/$organization_id/student/$student_id/profile/$profile_id/observation/$observation_id/attachment/$attachment_id"
b_denied_object="v1/org/$organization_id/student/$student_id/profile/$profile_id/observation/$observation_id/attachment/$b_denied_attachment"
atomic_object_name="v1/org/$organization_id/student/$student_id/profile/$profile_id/observation/$observation_id/attachment/$atomic_attachment"

upload_object "$token_a" "$object_name" "$tmp_png"
expect_storage_success 'Assigned Observation author upload'

upload_object "$token_a" "$object_name" "$tmp_png"
expect_storage_denied 'Immutable path overwrite'

upload_object "$token_b" "$b_denied_object" "$tmp_png"
expect_storage_denied 'Unassigned teacher upload'

call_rpc "$token_a" commit_observation_attachment \
  "{\"p_operation_id\":\"$attachment_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_a\",\"p_observation_id\":\"$observation_id\",\"p_attachment_id\":\"$attachment_id\"}"
expect_success 'Commit uploaded Observation Attachment'
assert_json "$rpc_body" command commit_observation_attachment_v1
assert_json "$rpc_body" attachment_id "$attachment_id"
assert_json "$rpc_body" observation_id "$observation_id"
assert_json "$rpc_body" bucket_id "$bucket"
assert_json "$rpc_body" object_name "$object_name"
assert_json "$rpc_body" content_type image/png

call_rpc "$token_a" commit_observation_attachment \
  "{\"p_operation_id\":\"$attachment_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_a\",\"p_observation_id\":\"$observation_id\",\"p_attachment_id\":\"$attachment_id\"}"
expect_success 'Same attachment commit operation receipt replay'

metadata_count="$(psql_db -Atc "select count(*) from public.observation_attachments where id = '$attachment_id'::uuid and operation_id = '$attachment_operation'::uuid")"
receipt_count="$(psql_db -Atc "select count(*) from public.operation_receipts where operation_id = '$attachment_operation'::uuid")"
if [[ "$metadata_count" != '1' || "$receipt_count" != '1' ]]; then
  echo "Attachment commit did not remain exactly-once: metadata=$metadata_count receipt=$receipt_count" >&2
  exit 1
fi

download_object "$token_a" "$object_name"
expect_storage_success 'Current assigned teacher can read committed attachment'

psql_db >/dev/null <<SQL
update public.student_teacher_assignments
   set active = false
 where id = '$assignment_a'::uuid;

insert into public.student_teacher_assignments (
  id,
  organization_id,
  student_id,
  subject_profile_id,
  teacher_app_user_id,
  active
) values (
  '$assignment_b'::uuid,
  '$organization_id'::uuid,
  '$student_id'::uuid,
  '$profile_id'::uuid,
  '$actor_b'::uuid,
  true
);
SQL

download_object "$token_b" "$object_name"
expect_storage_success 'New assigned teacher can read committed historical attachment'

download_object "$token_a" "$object_name"
expect_storage_denied 'Former teacher loses attachment read after assignment revoke'

upload_object "$token_b" "$b_denied_object" "$tmp_png"
expect_storage_denied 'New teacher cannot append media to previous teacher Observation'

psql_db >/dev/null <<SQL
delete from public.student_teacher_assignments where id = '$assignment_b'::uuid;
update public.student_teacher_assignments set active = true where id = '$assignment_a'::uuid;
SQL

upload_object "$token_a" "$atomic_object_name" "$tmp_png"
expect_storage_success 'Upload atomicity fixture object'

psql_db >/dev/null <<'SQL'
create or replace function public.test_fail_attachment_e2e_receipt()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
  if new.operation_id = '99500000-0000-0000-0000-000000000003'::uuid then
    raise exception 'TEST_FORCED_ATTACHMENT_E2E_RECEIPT_FAILURE';
  end if;
  return new;
end;
$$;

drop trigger if exists force_attachment_e2e_receipt_failure on public.operation_receipts;
create trigger force_attachment_e2e_receipt_failure
before insert on public.operation_receipts
for each row
execute function public.test_fail_attachment_e2e_receipt();
SQL

call_rpc "$token_a" commit_observation_attachment \
  "{\"p_operation_id\":\"$atomic_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_a\",\"p_observation_id\":\"$observation_id\",\"p_attachment_id\":\"$atomic_attachment\"}"
expect_error_message 'Forced attachment receipt failure' 'TEST_FORCED_ATTACHMENT_E2E_RECEIPT_FAILURE'

atomic_metadata="$(psql_db -Atc "select count(*) from public.observation_attachments where id = '$atomic_attachment'::uuid")"
atomic_receipt="$(psql_db -Atc "select count(*) from public.operation_receipts where operation_id = '$atomic_operation'::uuid")"
orphan_object="$(psql_db -Atc "select count(*) from storage.objects where bucket_id = '$bucket' and name = '$atomic_object_name'")"
if [[ "$atomic_metadata" != '0' || "$atomic_receipt" != '0' || "$orphan_object" != '1' ]]; then
  echo "Forced receipt failure violated boundary: metadata=$atomic_metadata receipt=$atomic_receipt object=$orphan_object" >&2
  exit 1
fi

echo 'Observation Attachment Storage conformance passed.'
