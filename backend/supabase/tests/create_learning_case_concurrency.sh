#!/usr/bin/env bash
set -euo pipefail

operation_id='92000000-0000-0000-0000-0000000000c1'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_id='50000000-0000-0000-0000-000000000001'
actor_id='10000000-0000-0000-0000-000000000001'
auth_subject='a0000000-0000-0000-0000-000000000001'
auth_issuer='supabase-demo'
case_title='并发撤权回归：正式 Case'
action_text='并发撤权回归：下一步行动'
lock_key=42424231
holder_app='xueqing_case_concurrency_lock_holder'
writer_app='xueqing_concurrency_create_learning_case'

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
delete from public.learning_case_observation_links where operation_id = '$operation_id'::uuid;
delete from public.learning_case_actions
 where case_id in (select id from public.learning_cases where title = '$case_title');
delete from public.learning_case_events where operation_id = '$operation_id'::uuid;
delete from public.learning_cases where title = '$case_title';
delete from public.operation_receipts where operation_id = '$operation_id'::uuid;
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
drop trigger if exists test_hold_create_learning_case on public.learning_case_events;
drop function if exists public.test_hold_create_learning_case();
SQL
}
trap cleanup EXIT

# Pause after authority rows have been locked and Case + Action have been staged,
# but before the provenance event/receipt can finish the transaction.
psql_db <<SQL
create or replace function public.test_hold_create_learning_case()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
  perform pg_catalog.pg_advisory_xact_lock($lock_key);
  return new;
end;
$$;

drop trigger if exists test_hold_create_learning_case on public.learning_case_events;
create trigger test_hold_create_learning_case
before insert on public.learning_case_events
for each row
when (new.operation_id = '$operation_id'::uuid)
execute function public.test_hold_create_learning_case();
SQL

psql_db > /tmp/xueqing-case-lock-holder.log 2>&1 <<SQL &
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
  echo 'Case test holder never acquired the advisory lock.' >&2
  exit 1
fi

psql_db > /tmp/xueqing-create-learning-case.log 2>&1 <<SQL &
set application_name = '$writer_app';
select pg_catalog.set_config('request.jwt.claim.sub', '$auth_subject', false);
select pg_catalog.set_config('request.jwt.claim.role', 'authenticated', false);
select pg_catalog.set_config('request.jwt.claims', '{"sub":"$auth_subject","role":"authenticated","iss":"$auth_issuer"}', false);
set role authenticated;
select public.create_learning_case(
  '$operation_id'::uuid,
  '$organization_id'::uuid,
  '$student_id'::uuid,
  '$profile_id'::uuid,
  '$assignment_id'::uuid,
  '$case_title',
  '$action_text',
  null,
  null
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
  echo 'CreateLearningCase never reached the post-authorization test barrier.' >&2
  cat /tmp/xueqing-create-learning-case.log >&2 || true
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
    echo "$label unexpectedly bypassed the in-flight Case authority lock." >&2
    exit 1
  fi
  if ! grep -qi 'lock timeout' <<<"$output"; then
    echo "$label failed for an unexpected reason:" >&2
    echo "$output" >&2
    exit 1
  fi
  echo "$label: revoke correctly blocked while CreateLearningCase was in flight."
}

expect_revoke_blocked   'AppUser disable'   "update public.app_users set enabled = false where id = '$actor_id'::uuid;"
expect_revoke_blocked   'Membership disable'   "update public.memberships set status = 'disabled' where organization_id = '$organization_id'::uuid and app_user_id = '$actor_id'::uuid;"
expect_revoke_blocked   'Assignment revoke'   "update public.student_teacher_assignments set active = false where id = '$assignment_id'::uuid;"

psql_db -Atc "select pg_catalog.pg_terminate_backend(pid) from pg_catalog.pg_stat_activity where application_name = '$holder_app'" >/dev/null
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

case_count="$(psql_db -Atc "select count(*) from public.learning_cases where title = '$case_title'")"
action_count="$(psql_db -Atc "select count(*) from public.learning_case_actions as action join public.learning_cases as learning_case on learning_case.id = action.case_id where learning_case.title = '$case_title' and action.action_role = 'primary' and action.status = 'pending'")"
event_count="$(psql_db -Atc "select count(*) from public.learning_case_events where operation_id = '$operation_id'::uuid")"
receipt_count="$(psql_db -Atc "select count(*) from public.operation_receipts where operation_id = '$operation_id'::uuid")"

if [[ "$case_count" != '1' || "$action_count" != '1' || "$event_count" != '1' || "$receipt_count" != '1' ]]; then
  echo "Expected one committed Case/Action/event/receipt; got case=$case_count action=$action_count event=$event_count receipt=$receipt_count" >&2
  cat /tmp/xueqing-create-learning-case.log >&2 || true
  exit 1
fi

echo 'Concurrent CreateLearningCase authority-revoke regression passed.'
