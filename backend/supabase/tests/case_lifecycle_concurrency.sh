#!/usr/bin/env bash
set -euo pipefail

fixture_operation_id='98000000-0000-0000-0000-000000000001'
confirm_operation_id='98000000-0000-0000-0000-000000000002'
intervene_operation_id='98000000-0000-0000-0000-000000000003'
pending_verification_operation_id='98000000-0000-0000-0000-000000000004'
stable_operation_id='98000000-0000-0000-0000-000000000005'
close_operation_id='98000000-0000-0000-0000-000000000006'
competing_operation_id='98000000-0000-0000-0000-000000000007'

organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_id='50000000-0000-0000-0000-000000000001'
actor_id='10000000-0000-0000-0000-000000000001'
auth_subject='a0000000-0000-0000-0000-000000000001'
auth_issuer='supabase-demo'
case_title='并发回归：Case 关闭'
lock_key=42424240
holder_app='xueqing_case_lifecycle_lock_holder'
writer_app='xueqing_case_lifecycle_writer'

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
drop trigger if exists test_hold_case_lifecycle on public.learning_case_events;
drop function if exists public.test_hold_case_lifecycle();

delete from public.learning_case_events
 where operation_id in (
   '$fixture_operation_id'::uuid,
   '$confirm_operation_id'::uuid,
   '$intervene_operation_id'::uuid,
   '$pending_verification_operation_id'::uuid,
   '$stable_operation_id'::uuid,
   '$close_operation_id'::uuid,
   '$competing_operation_id'::uuid
 );
delete from public.learning_case_actions
 where case_id in (
   select id from public.learning_cases where title = '$case_title'
 );
delete from public.learning_cases where title = '$case_title';
delete from public.operation_receipts
 where operation_id in (
   '$fixture_operation_id'::uuid,
   '$confirm_operation_id'::uuid,
   '$intervene_operation_id'::uuid,
   '$pending_verification_operation_id'::uuid,
   '$stable_operation_id'::uuid,
   '$close_operation_id'::uuid,
   '$competing_operation_id'::uuid
 );
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
SQL
}
trap cleanup EXIT

fixture_receipt="$(psql_db -At <<SQL
select pg_catalog.set_config('request.jwt.claim.sub', '$auth_subject', false);
select pg_catalog.set_config('request.jwt.claim.role', 'authenticated', false);
select pg_catalog.set_config(
  'request.jwt.claims',
  pg_catalog.jsonb_build_object(
    'sub', '$auth_subject',
    'role', 'authenticated',
    'iss', '$auth_issuer'
  )::text,
  false
);
set role authenticated;
select public.create_learning_case(
  '$fixture_operation_id'::uuid,
  '$organization_id'::uuid,
  '$student_id'::uuid,
  '$profile_id'::uuid,
  '$assignment_id'::uuid,
  '$case_title',
  '并发回归：关闭前保持唯一 pending primary Action',
  '2026-09-24'::date,
  null
)::text;
SQL
)"

case_id="$(psql_db -Atc "select result_payload ->> 'case_id' from public.operation_receipts where operation_id = '$fixture_operation_id'::uuid")"
action_id="$(psql_db -Atc "select result_payload ->> 'primary_action_id' from public.operation_receipts where operation_id = '$fixture_operation_id'::uuid")"
if [[ -z "$case_id" || -z "$action_id" || -z "$fixture_receipt" ]]; then
  echo 'Failed to create lifecycle concurrency fixture Case.' >&2
  exit 1
fi

transition_case() {
  local operation_id="$1"
  local expected_version="$2"
  local target_state="$3"
  psql_db >/dev/null <<SQL
select pg_catalog.set_config('request.jwt.claim.sub', '$auth_subject', false);
select pg_catalog.set_config('request.jwt.claim.role', 'authenticated', false);
select pg_catalog.set_config(
  'request.jwt.claims',
  pg_catalog.jsonb_build_object(
    'sub', '$auth_subject',
    'role', 'authenticated',
    'iss', '$auth_issuer'
  )::text,
  false
);
set role authenticated;
select public.transition_learning_case_state(
  '$operation_id'::uuid,
  '$organization_id'::uuid,
  '$student_id'::uuid,
  '$profile_id'::uuid,
  '$assignment_id'::uuid,
  '$case_id'::uuid,
  $expected_version,
  '$target_state'
);
SQL
}

transition_case "$confirm_operation_id" 1 confirmed
transition_case "$intervene_operation_id" 2 intervening
transition_case "$pending_verification_operation_id" 3 pending_verification
transition_case "$stable_operation_id" 4 stable

state_before="$(psql_db -Atc "select state || ':' || version::text from public.learning_cases where id = '$case_id'::uuid")"
if [[ "$state_before" != 'stable:5' ]]; then
  echo "Lifecycle fixture did not reach stable:5; got $state_before" >&2
  exit 1
fi

psql_db <<SQL
create or replace function public.test_hold_case_lifecycle()
returns trigger
language plpgsql
set search_path = ''
as \$\$
begin
  perform pg_catalog.pg_advisory_xact_lock($lock_key);
  return new;
end;
\$\$;

drop trigger if exists test_hold_case_lifecycle on public.learning_case_events;
create trigger test_hold_case_lifecycle
before insert on public.learning_case_events
for each row
when (
  new.operation_id = '$close_operation_id'::uuid
  and new.event_type = 'case_closed'
)
execute function public.test_hold_case_lifecycle();
SQL

psql_db > /tmp/xueqing-case-lifecycle-lock-holder.log 2>&1 <<SQL &
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
  echo 'Lifecycle holder never acquired advisory lock.' >&2
  exit 1
fi

psql_db > /tmp/xueqing-case-lifecycle-close.log 2>&1 <<SQL &
set application_name = '$writer_app';
select pg_catalog.set_config('request.jwt.claim.sub', '$auth_subject', false);
select pg_catalog.set_config('request.jwt.claim.role', 'authenticated', false);
select pg_catalog.set_config(
  'request.jwt.claims',
  pg_catalog.jsonb_build_object(
    'sub', '$auth_subject',
    'role', 'authenticated',
    'iss', '$auth_issuer'
  )::text,
  false
);
set role authenticated;
select public.close_learning_case(
  '$close_operation_id'::uuid,
  '$organization_id'::uuid,
  '$student_id'::uuid,
  '$profile_id'::uuid,
  '$assignment_id'::uuid,
  '$case_id'::uuid,
  '$action_id'::uuid,
  5,
  1
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
  echo 'Close command never reached the post-mutation lifecycle barrier.' >&2
  cat /tmp/xueqing-case-lifecycle-close.log >&2 || true
  exit 1
fi

expect_lock_blocked() {
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
    echo "$label unexpectedly bypassed the in-flight lifecycle lock." >&2
    exit 1
  fi
  if ! grep -qi 'lock timeout' <<<"$output"; then
    echo "$label failed for an unexpected reason:" >&2
    echo "$output" >&2
    exit 1
  fi
  echo "$label: correctly blocked while close was in flight."
}

expect_lock_blocked 'AppUser disable'   "update public.app_users set enabled = false where id = '$actor_id'::uuid;"
expect_lock_blocked 'Membership disable'   "update public.memberships set status = 'disabled' where organization_id = '$organization_id'::uuid and app_user_id = '$actor_id'::uuid;"
expect_lock_blocked 'Assignment revoke'   "update public.student_teacher_assignments set active = false where id = '$assignment_id'::uuid;"

expect_lock_blocked 'Competing reschedule'   "select pg_catalog.set_config('request.jwt.claim.sub', '$auth_subject', false);
   select pg_catalog.set_config('request.jwt.claim.role', 'authenticated', false);
   select pg_catalog.set_config(
     'request.jwt.claims',
     pg_catalog.jsonb_build_object(
       'sub', '$auth_subject',
       'role', 'authenticated',
       'iss', '$auth_issuer'
     )::text,
     false
   );
   set role authenticated;
   select public.reschedule_primary_action(
     '$competing_operation_id'::uuid,
     '$organization_id'::uuid,
     '$student_id'::uuid,
     '$profile_id'::uuid,
     '$assignment_id'::uuid,
     '$case_id'::uuid,
     '$action_id'::uuid,
     5,
     1,
     '2026-09-26'::date
   );"

psql_db -Atc "select pg_catalog.pg_terminate_backend(pid) from pg_catalog.pg_stat_activity where application_name = '$holder_app'" >/dev/null
wait "$holder_pid" 2>/dev/null || true
holder_pid=''
wait "$writer_pid"
writer_pid=''

case_state="$(psql_db -Atc "select state from public.learning_cases where id = '$case_id'::uuid")"
case_version="$(psql_db -Atc "select version from public.learning_cases where id = '$case_id'::uuid")"
action_state="$(psql_db -Atc "select status || ':' || version::text from public.learning_case_actions where id = '$action_id'::uuid")"
pending_count="$(psql_db -Atc "select count(*) from public.learning_case_actions where case_id = '$case_id'::uuid and action_role = 'primary' and status = 'pending'")"
event_count="$(psql_db -Atc "select count(*) from public.learning_case_events where operation_id = '$close_operation_id'::uuid and event_type = 'case_closed'")"
receipt_count="$(psql_db -Atc "select count(*) from public.operation_receipts where operation_id = '$close_operation_id'::uuid")"

if [[ "$case_state" != 'closed' || "$case_version" != '6' || "$action_state" != 'cancelled:2' || "$pending_count" != '0' || "$event_count" != '1' || "$receipt_count" != '1' ]]; then
  echo "Expected committed close state=closed case_version=6 action=cancelled:2 pending=0 event=1 receipt=1; got state=$case_state case_version=$case_version action=$action_state pending=$pending_count event=$event_count receipt=$receipt_count" >&2
  cat /tmp/xueqing-case-lifecycle-close.log >&2 || true
  exit 1
fi

echo 'Concurrent Case lifecycle authority/Action-lock regression passed.'
