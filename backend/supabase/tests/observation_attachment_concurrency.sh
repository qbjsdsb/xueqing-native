#!/usr/bin/env bash
set -euo pipefail

bucket='teaching-attachments-v1'
observation_operation='99600000-0000-0000-0000-000000000001'
attachment_operation='99600000-0000-0000-0000-000000000002'
attachment_id='99600000-0000-0000-0000-000000000003'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_id='50000000-0000-0000-0000-000000000001'
actor_id='10000000-0000-0000-0000-000000000001'
auth_subject='a0000000-0000-0000-0000-000000000001'
auth_issuer='supabase-demo'
lock_key=42424255
holder_app='xueqing_attachment_lock_holder'
writer_app='xueqing_attachment_commit_writer'

status_env="$(mktemp)"
supabase status --workdir backend -o env > "$status_env"
set -a
# shellcheck disable=SC1090
source "$status_env"
set +a
rm -f "$status_env"

: "${API_URL:?API_URL missing from local Supabase status}"
: "${SERVICE_ROLE_KEY:?SERVICE_ROLE_KEY missing from local Supabase status}"
api_url="${API_URL%/}"
echo "::add-mask::$SERVICE_ROLE_KEY"

db_container="$(docker ps --filter 'name=supabase_db_' --format '{{.Names}}' | head -n 1)"
if [[ -z "$db_container" ]]; then
  echo 'Local Supabase database container was not found.' >&2
  exit 1
fi

psql_db() {
  docker exec -i "$db_container" psql -U postgres -d postgres -v ON_ERROR_STOP=1 "$@"
}

holder_pid=''
writer_pid=''
object_name=''
tmp_png="$(mktemp --suffix=.png)"

terminate_test_sessions() {
  psql_db -Atc "select pg_catalog.pg_terminate_backend(pid) from pg_catalog.pg_stat_activity where application_name in ('$holder_app', '$writer_app') and pid <> pg_catalog.pg_backend_pid()" >/dev/null 2>&1 || true
}

cleanup() {
  set +e
  terminate_test_sessions
  if [[ -n "$holder_pid" ]]; then kill "$holder_pid" 2>/dev/null || true; fi
  if [[ -n "$writer_pid" ]]; then kill "$writer_pid" 2>/dev/null || true; fi
  wait "$holder_pid" 2>/dev/null || true
  wait "$writer_pid" 2>/dev/null || true

  psql_db >/dev/null 2>&1 <<SQL
reset role;
drop trigger if exists test_hold_attachment_commit on public.observation_attachments;
drop function if exists public.test_hold_attachment_commit();
delete from public.observation_attachments where operation_id = '$attachment_operation'::uuid;
delete from public.operation_receipts
 where operation_id in ('$observation_operation'::uuid, '$attachment_operation'::uuid);
delete from public.observations where operation_id = '$observation_operation'::uuid;
update public.identity_links
   set active = true
 where provider_key = 'supabase'
   and issuer = '$auth_issuer'
   and external_subject = '$auth_subject';
update public.app_users set enabled = true where id = '$actor_id'::uuid;
update public.memberships
   set status = 'active', can_teach = true
 where organization_id = '$organization_id'::uuid
   and app_user_id = '$actor_id'::uuid;
update public.student_teacher_assignments
   set active = true
 where id = '$assignment_id'::uuid;
SQL

  if [[ -n "$object_name" ]]; then
    curl -sS -o /dev/null -X DELETE \
      "$api_url/storage/v1/object/$bucket/$object_name" \
      -H "apikey: $SERVICE_ROLE_KEY" \
      -H "Authorization: Bearer $SERVICE_ROLE_KEY" || true
  fi
  rm -f "$tmp_png"
}
trap cleanup EXIT

observation_receipt="$(psql_db -At <<SQL
select pg_catalog.set_config('request.jwt.claim.sub', '$auth_subject', false);
select pg_catalog.set_config('request.jwt.claim.role', 'authenticated', false);
select pg_catalog.set_config(
  'request.jwt.claims',
  '{"sub":"$auth_subject","role":"authenticated","iss":"$auth_issuer"}',
  false
);
set role authenticated;
select public.create_observation(
  '$observation_operation'::uuid,
  '$organization_id'::uuid,
  '$student_id'::uuid,
  '$profile_id'::uuid,
  '$assignment_id'::uuid,
  '附件并发回归：metadata commit 必须锁住实时教学权限。',
  '2026-09-20T03:30:00Z'::timestamptz,
  '{"fixture":true,"source":"attachment-concurrency-gate"}'::jsonb
)::text;
SQL
)"
observation_id="$(psql_db -Atc "select result_payload ->> 'observation_id' from public.operation_receipts where operation_id = '$observation_operation'::uuid")"
if [[ -z "$observation_receipt" || -z "$observation_id" ]]; then
  echo 'Failed to create attachment concurrency parent Observation.' >&2
  exit 1
fi

object_name="v1/org/$organization_id/student/$student_id/profile/$profile_id/observation/$observation_id/attachment/$attachment_id"
printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z0xkAAAAASUVORK5CYII=' \
  | base64 -d > "$tmp_png"

upload_status="$(curl -sS -o /tmp/xueqing-attachment-concurrency-upload.json -w '%{http_code}' \
  -X POST "$api_url/storage/v1/object/$bucket/$object_name" \
  -H "apikey: $SERVICE_ROLE_KEY" \
  -H "Authorization: Bearer $SERVICE_ROLE_KEY" \
  -H 'Content-Type: image/png' \
  --data-binary "@$tmp_png")"
if [[ ! "$upload_status" =~ ^2 ]]; then
  echo "Service-role fixture upload failed with HTTP $upload_status:" >&2
  cat /tmp/xueqing-attachment-concurrency-upload.json >&2 || true
  exit 1
fi

psql_db <<SQL
create or replace function public.test_hold_attachment_commit()
returns trigger
language plpgsql
set search_path = ''
as \$\$
begin
  perform pg_catalog.pg_advisory_xact_lock($lock_key);
  return new;
end;
\$\$;

drop trigger if exists test_hold_attachment_commit on public.observation_attachments;
create trigger test_hold_attachment_commit
before insert on public.observation_attachments
for each row
when (new.operation_id = '$attachment_operation'::uuid)
execute function public.test_hold_attachment_commit();
SQL

psql_db > /tmp/xueqing-attachment-lock-holder.log 2>&1 <<SQL &
set application_name = '$holder_app';
select pg_catalog.pg_advisory_lock($lock_key);
select pg_catalog.pg_sleep(30);
SQL
holder_pid=$!

granted=0
for _ in $(seq 1 40); do
  granted="$(psql_db -Atc "select count(*) from pg_catalog.pg_locks as locks join pg_catalog.pg_stat_activity as activity on activity.pid = locks.pid where locks.locktype = 'advisory' and locks.granted and activity.application_name = '$holder_app'")"
  if [[ "$granted" -ge 1 ]]; then break; fi
  sleep 0.1
done
if [[ "$granted" -lt 1 ]]; then
  echo 'Attachment holder never acquired advisory lock.' >&2
  exit 1
fi

psql_db > /tmp/xueqing-attachment-commit.log 2>&1 <<SQL &
set application_name = '$writer_app';
select pg_catalog.set_config('request.jwt.claim.sub', '$auth_subject', false);
select pg_catalog.set_config('request.jwt.claim.role', 'authenticated', false);
select pg_catalog.set_config(
  'request.jwt.claims',
  '{"sub":"$auth_subject","role":"authenticated","iss":"$auth_issuer"}',
  false
);
set role authenticated;
select public.commit_observation_attachment(
  '$attachment_operation'::uuid,
  '$organization_id'::uuid,
  '$student_id'::uuid,
  '$profile_id'::uuid,
  '$assignment_id'::uuid,
  '$observation_id'::uuid,
  '$attachment_id'::uuid
);
SQL
writer_pid=$!

waiting=0
for _ in $(seq 1 80); do
  waiting="$(psql_db -Atc "select count(*) from pg_catalog.pg_locks as locks join pg_catalog.pg_stat_activity as activity on activity.pid = locks.pid where locks.locktype = 'advisory' and not locks.granted and activity.application_name = '$writer_app'")"
  if [[ "$waiting" -ge 1 ]]; then break; fi
  sleep 0.1
done
if [[ "$waiting" -lt 1 ]]; then
  echo 'CommitObservationAttachment never reached the post-authorization test barrier.' >&2
  cat /tmp/xueqing-attachment-commit.log >&2 || true
  exit 1
fi

expect_revoke_blocked() {
  local label="$1"
  local sql="$2"
  local output status
  set +e
  output="$(psql_db 2>&1 <<SQL
set lock_timeout = '800ms';
$sql
SQL
)"
  status=$?
  set -e

  if [[ "$status" -eq 0 ]]; then
    echo "$label unexpectedly bypassed the in-flight attachment commit lock." >&2
    exit 1
  fi
  if ! grep -qi 'lock timeout' <<<"$output"; then
    echo "$label failed for an unexpected reason:" >&2
    echo "$output" >&2
    exit 1
  fi
  echo "$label: correctly blocked while CommitObservationAttachment was in flight."
}

expect_revoke_blocked \
  'IdentityLink disable' \
  "update public.identity_links set active = false where provider_key = 'supabase' and issuer = '$auth_issuer' and external_subject = '$auth_subject';"
expect_revoke_blocked \
  'AppUser disable' \
  "update public.app_users set enabled = false where id = '$actor_id'::uuid;"
expect_revoke_blocked \
  'Membership disable' \
  "update public.memberships set status = 'disabled' where organization_id = '$organization_id'::uuid and app_user_id = '$actor_id'::uuid;"
expect_revoke_blocked \
  'Assignment revoke' \
  "update public.student_teacher_assignments set active = false where id = '$assignment_id'::uuid;"

psql_db -Atc "select pg_catalog.pg_terminate_backend(pid) from pg_catalog.pg_stat_activity where application_name = '$holder_app'" >/dev/null
wait "$holder_pid" 2>/dev/null || true
holder_pid=''
wait "$writer_pid"
writer_pid=''

psql_db <<SQL
begin;
set local lock_timeout = '2s';
update public.identity_links
   set active = false
 where provider_key = 'supabase'
   and issuer = '$auth_issuer'
   and external_subject = '$auth_subject';
update public.app_users set enabled = false where id = '$actor_id'::uuid;
update public.memberships
   set status = 'disabled'
 where organization_id = '$organization_id'::uuid
   and app_user_id = '$actor_id'::uuid;
update public.student_teacher_assignments
   set active = false
 where id = '$assignment_id'::uuid;
rollback;
SQL

attachment_count="$(psql_db -Atc "select count(*) from public.observation_attachments where operation_id = '$attachment_operation'::uuid")"
receipt_count="$(psql_db -Atc "select count(*) from public.operation_receipts where operation_id = '$attachment_operation'::uuid")"
if [[ "$attachment_count" != '1' || "$receipt_count" != '1' ]]; then
  echo "Expected one committed attachment and one receipt; got attachment=$attachment_count receipt=$receipt_count" >&2
  cat /tmp/xueqing-attachment-commit.log >&2 || true
  exit 1
fi

echo 'Concurrent Observation Attachment authority-revoke regression passed.'
