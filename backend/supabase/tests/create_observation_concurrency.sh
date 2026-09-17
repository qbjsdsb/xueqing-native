#!/usr/bin/env bash
set -euo pipefail

operation_id='90000000-0000-0000-0000-0000000000c1'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_id='50000000-0000-0000-0000-000000000001'
actor_id='10000000-0000-0000-0000-000000000001'
auth_subject='a0000000-0000-0000-0000-000000000001'
lock_key=42424217

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
cleanup() {
  set +e
  if [[ -n "$holder_pid" ]]; then kill "$holder_pid" 2>/dev/null || true; fi
  if [[ -n "$writer_pid" ]]; then kill "$writer_pid" 2>/dev/null || true; fi
  wait "$holder_pid" 2>/dev/null || true
  wait "$writer_pid" 2>/dev/null || true
  psql_db >/dev/null 2>&1 <<SQL
reset role;
delete from public.operation_receipts where operation_id = '$operation_id'::uuid;
delete from public.observations where operation_id = '$operation_id'::uuid;
update public.app_users
   set enabled = true
 where id = '$actor_id'::uuid;
update public.memberships
   set status = 'active', can_teach = true
 where organization_id = '$organization_id'::uuid
   and app_user_id = '$actor_id'::uuid;
update public.student_teacher_assignments
   set active = true
 where id = '$assignment_id'::uuid;
drop trigger if exists test_hold_create_observation on public.observations;
drop function if exists public.test_hold_create_observation();
SQL
}
trap cleanup EXIT

# The test-only trigger pauses after CreateObservation has passed and locked its
# live Teaching Fact rows, but before the Observation insert can finish.
psql_db <<SQL
create or replace function public.test_hold_create_observation()
returns trigger
language plpgsql
set search_path = ''
as \$\$
begin
  perform pg_catalog.pg_advisory_xact_lock($lock_key);
  return new;
end;
\$\$;

drop trigger if exists test_hold_create_observation on public.observations;
create trigger test_hold_create_observation
before insert on public.observations
for each row
when (new.operation_id = '$operation_id'::uuid)
execute function public.test_hold_create_observation();
SQL

# A separate session owns the advisory lock. When the command reaches the
# trigger it blocks there while retaining every Teaching Fact FOR SHARE lock.
psql_db > /tmp/xueqing-lock-holder.log 2>&1 <<SQL &
select pg_catalog.pg_advisory_lock($lock_key);
select pg_catalog.pg_sleep(30);
SQL
holder_pid=$!

for _ in $(seq 1 40); do
  granted="$(psql_db -Atc "select count(*) from pg_catalog.pg_locks where locktype = 'advisory' and granted")"
  if [[ "$granted" -ge 1 ]]; then break; fi
  sleep 0.1
done
if [[ "${granted:-0}" -lt 1 ]]; then
  echo 'Test holder never acquired the advisory lock.' >&2
  exit 1
fi

psql_db > /tmp/xueqing-create-observation.log 2>&1 <<SQL &
select pg_catalog.set_config('request.jwt.claim.sub', '$auth_subject', false);
select pg_catalog.set_config('request.jwt.claim.role', 'authenticated', false);
select pg_catalog.set_config('request.jwt.claims', '{"sub":"$auth_subject","role":"authenticated"}', false);
set role authenticated;
select public.create_observation(
  '$operation_id'::uuid,
  '$organization_id'::uuid,
  '$student_id'::uuid,
  '$profile_id'::uuid,
  '$assignment_id'::uuid,
  '并发撤权回归：命令锁住实时教学权限后才能提交。',
  '2026-09-17T12:30:00Z'::timestamptz,
  '{"source":"backend-concurrency-gate","fixture":true}'::jsonb
);
SQL
writer_pid=$!

# Do not guess that the writer reached the trigger: wait until PostgreSQL shows
# an ungranted advisory lock request from the CreateObservation transaction.
waiting=0
for _ in $(seq 1 80); do
  waiting="$(psql_db -Atc "select count(*) from pg_catalog.pg_locks where locktype = 'advisory' and not granted")"
  if [[ "$waiting" -ge 1 ]]; then break; fi
  sleep 0.1
done
if [[ "$waiting" -lt 1 ]]; then
  echo 'CreateObservation never reached the post-authorization test barrier.' >&2
  cat /tmp/xueqing-create-observation.log >&2 || true
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
    echo "$label unexpectedly bypassed the in-flight Teaching Fact lock." >&2
    exit 1
  fi
  if ! grep -qi 'lock timeout' <<<"$output"; then
    echo "$label failed for an unexpected reason:" >&2
    echo "$output" >&2
    exit 1
  fi
  echo "$label: revoke correctly blocked while CreateObservation was in flight."
}

expect_revoke_blocked \
  'AppUser disable' \
  "update public.app_users set enabled = false where id = '$actor_id'::uuid;"
expect_revoke_blocked \
  'Membership disable' \
  "update public.memberships set status = 'disabled' where organization_id = '$organization_id'::uuid and app_user_id = '$actor_id'::uuid;"
expect_revoke_blocked \
  'Assignment revoke' \
  "update public.student_teacher_assignments set active = false where id = '$assignment_id'::uuid;"

# Release the test barrier. The command may now commit; the locks must then be
# released and the same authority changes must become immediately writable.
kill "$holder_pid"
wait "$holder_pid" 2>/dev/null || true
holder_pid=''
wait "$writer_pid"
writer_pid=''

psql_db <<SQL
begin;
set local lock_timeout = '2s';
update public.app_users set enabled = false where id = '$actor_id'::uuid;
update public.memberships set status = 'disabled' where organization_id = '$organization_id'::uuid and app_user_id = '$actor_id'::uuid;
update public.student_teacher_assignments set active = false where id = '$assignment_id'::uuid;
rollback;
SQL

observation_count="$(psql_db -Atc "select count(*) from public.observations where operation_id = '$operation_id'::uuid")"
receipt_count="$(psql_db -Atc "select count(*) from public.operation_receipts where operation_id = '$operation_id'::uuid")"
if [[ "$observation_count" != '1' || "$receipt_count" != '1' ]]; then
  echo "Expected one committed Observation and one receipt; got observation=$observation_count receipt=$receipt_count" >&2
  cat /tmp/xueqing-create-observation.log >&2 || true
  exit 1
fi

echo 'Concurrent authority-revoke regression passed.'
