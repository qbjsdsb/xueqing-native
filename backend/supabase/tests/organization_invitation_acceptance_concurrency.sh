#!/usr/bin/env bash
set -euo pipefail

organization_id='20000000-0000-0000-0000-000000000001'
owner_app_user_id='10000000-0000-0000-0000-000000000001'
app_user_id='10000000-0000-0000-0000-00000000a200'
subject_one='b0000000-0000-0000-0000-00000000a201'
subject_two='b0000000-0000-0000-0000-00000000a202'
email_one='multi.one@example.com'
email_two='multi.two@example.com'
invitation_one='95000000-0000-0000-0000-00000000a201'
invitation_two='95000000-0000-0000-0000-00000000a202'
create_operation_one='95100000-0000-0000-0000-00000000a201'
create_operation_two='95100000-0000-0000-0000-00000000a202'
accept_operation_one='96000000-0000-0000-0000-00000000a201'
accept_operation_two='96000000-0000-0000-0000-00000000a202'
holder_app='xueqing_accept_membership_lock_holder'
writer_one_app='xueqing_accept_writer_one'
writer_two_app='xueqing_accept_writer_two'

db_container="$(docker ps --filter 'name=supabase_db_' --format '{{.Names}}' | head -n 1)"
if [[ -z "$db_container" ]]; then
  echo 'Local Supabase database container was not found.' >&2
  exit 1
fi

psql_db() {
  docker exec -i "$db_container" psql -U postgres -d postgres -v ON_ERROR_STOP=1 "$@"
}

holder_pid=''
writer_one_pid=''
writer_two_pid=''

terminate_test_sessions() {
  psql_db -Atc "select pg_catalog.pg_terminate_backend(pid) from pg_catalog.pg_stat_activity where application_name in ('$holder_app', '$writer_one_app', '$writer_two_app') and pid <> pg_catalog.pg_backend_pid()" >/dev/null 2>&1 || true
}

cleanup() {
  set +e
  terminate_test_sessions
  [[ -n "$holder_pid" ]] && kill "$holder_pid" 2>/dev/null || true
  [[ -n "$writer_one_pid" ]] && kill "$writer_one_pid" 2>/dev/null || true
  [[ -n "$writer_two_pid" ]] && kill "$writer_two_pid" 2>/dev/null || true
  wait "$holder_pid" 2>/dev/null || true
  wait "$writer_one_pid" 2>/dev/null || true
  wait "$writer_two_pid" 2>/dev/null || true
  psql_db >/dev/null 2>&1 <<SQL
delete from public.operation_receipts
 where operation_id in ('$accept_operation_one'::uuid, '$accept_operation_two'::uuid);
delete from public.memberships
 where organization_id = '$organization_id'::uuid
   and app_user_id = '$app_user_id'::uuid;
delete from public.organization_invitations
 where id in ('$invitation_one'::uuid, '$invitation_two'::uuid);
delete from public.identity_links
 where app_user_id = '$app_user_id'::uuid;
delete from public.app_users
 where id = '$app_user_id'::uuid;
SQL
}
trap cleanup EXIT

baseline_assignment_count="$(psql_db -Atc "select count(*) from public.student_teacher_assignments")"

psql_db <<SQL
insert into public.app_users (
    id,
    auth_subject,
    display_name,
    enabled
) values (
    '$app_user_id'::uuid,
    null,
    '多身份并发验收用户',
    true
);

insert into public.identity_links (
    app_user_id,
    provider_key,
    issuer,
    external_subject,
    active
) values
    ('$app_user_id'::uuid, 'supabase', 'supabase-demo', '$subject_one', true),
    ('$app_user_id'::uuid, 'supabase', 'supabase-demo', '$subject_two', true);

insert into public.organization_invitations (
    id,
    create_operation_id,
    organization_id,
    invited_email,
    target_role,
    target_can_teach,
    status,
    invited_by_app_user_id,
    expires_at
) values
    (
        '$invitation_one'::uuid,
        '$create_operation_one'::uuid,
        '$organization_id'::uuid,
        '$email_one',
        'teacher',
        true,
        'pending',
        '$owner_app_user_id'::uuid,
        pg_catalog.clock_timestamp() + interval '1 day'
    ),
    (
        '$invitation_two'::uuid,
        '$create_operation_two'::uuid,
        '$organization_id'::uuid,
        '$email_two',
        'teacher',
        false,
        'pending',
        '$owner_app_user_id'::uuid,
        pg_catalog.clock_timestamp() + interval '1 day'
    );
SQL

membership_lock_key="$(psql_db -Atc "select pg_catalog.hashtextextended('organization-membership:$organization_id:$app_user_id', 0)")"

psql_db > /tmp/xueqing-accept-membership-holder.log 2>&1 <<SQL &
set application_name = '$holder_app';
select pg_catalog.pg_advisory_lock($membership_lock_key);
select pg_catalog.pg_sleep(60);
SQL
holder_pid=$!

holder_granted=0
for _ in $(seq 1 50); do
  holder_granted="$(psql_db -Atc "select count(*) from pg_catalog.pg_locks as locks join pg_catalog.pg_stat_activity as activity on activity.pid = locks.pid where locks.locktype = 'advisory' and locks.granted and activity.application_name = '$holder_app'")"
  if [[ "$holder_granted" -ge 1 ]]; then
    break
  fi
  sleep 0.1
done

if [[ "$holder_granted" -lt 1 ]]; then
  echo 'Membership-key holder never acquired the advisory lock.' >&2
  exit 1
fi

start_writer() {
  local app_name="$1"
  local subject="$2"
  local email="$3"
  local operation_id="$4"
  local invitation_id="$5"
  local logfile="$6"

  psql_db > "$logfile" 2>&1 <<SQL &
set application_name = '$app_name';
select pg_catalog.set_config('request.jwt.claim.sub', '$subject', false);
select pg_catalog.set_config('request.jwt.claim.role', 'authenticated', false);
select pg_catalog.set_config(
    'request.jwt.claims',
    '{"sub":"$subject","role":"authenticated","iss":"supabase-demo","email":"$email","is_anonymous":false}',
    false
);
set role authenticated;
select public.accept_organization_invitation_v1(
    '$operation_id'::uuid,
    '$invitation_id'::uuid,
    '多身份并发验收用户'
);
SQL
  echo $!
}

writer_one_pid="$(start_writer "$writer_one_app" "$subject_one" "$email_one" "$accept_operation_one" "$invitation_one" /tmp/xueqing-accept-writer-one.log)"
writer_two_pid="$(start_writer "$writer_two_app" "$subject_two" "$email_two" "$accept_operation_two" "$invitation_two" /tmp/xueqing-accept-writer-two.log)"

waiting=0
for _ in $(seq 1 100); do
  waiting="$(psql_db -Atc "select count(*) from pg_catalog.pg_locks as locks join pg_catalog.pg_stat_activity as activity on activity.pid = locks.pid where locks.locktype = 'advisory' and not locks.granted and activity.application_name in ('$writer_one_app', '$writer_two_app')")"
  if [[ "$waiting" -ge 2 ]]; then
    break
  fi
  sleep 0.1
done

if [[ "$waiting" -lt 2 ]]; then
  echo 'Both acceptance writers did not serialize on the absent Membership business key.' >&2
  cat /tmp/xueqing-accept-writer-one.log >&2 || true
  cat /tmp/xueqing-accept-writer-two.log >&2 || true
  exit 1
fi

psql_db -Atc "select pg_catalog.pg_terminate_backend(pid) from pg_catalog.pg_stat_activity where application_name = '$holder_app'" >/dev/null
wait "$holder_pid" 2>/dev/null || true
holder_pid=''

set +e
wait "$writer_one_pid"
writer_one_status=$?
wait "$writer_two_pid"
writer_two_status=$?
set -e
writer_one_pid=''
writer_two_pid=''

if ! {
  [[ "$writer_one_status" -eq 0 && "$writer_two_status" -ne 0 ]] ||
  [[ "$writer_one_status" -ne 0 && "$writer_two_status" -eq 0 ]]
}; then
  echo "Expected exactly one acceptance success; got writer_one=$writer_one_status writer_two=$writer_two_status" >&2
  cat /tmp/xueqing-accept-writer-one.log >&2 || true
  cat /tmp/xueqing-accept-writer-two.log >&2 || true
  exit 1
fi

if ! cat /tmp/xueqing-accept-writer-one.log /tmp/xueqing-accept-writer-two.log | grep -q 'XQ_MEMBERSHIP_ALREADY_EXISTS'; then
  echo 'Losing acceptance did not fail with XQ_MEMBERSHIP_ALREADY_EXISTS.' >&2
  cat /tmp/xueqing-accept-writer-one.log >&2 || true
  cat /tmp/xueqing-accept-writer-two.log >&2 || true
  exit 1
fi

membership_count="$(psql_db -Atc "select count(*) from public.memberships where organization_id = '$organization_id'::uuid and app_user_id = '$app_user_id'::uuid")"
accepted_count="$(psql_db -Atc "select count(*) from public.organization_invitations where id in ('$invitation_one'::uuid, '$invitation_two'::uuid) and status = 'accepted'")"
pending_count="$(psql_db -Atc "select count(*) from public.organization_invitations where id in ('$invitation_one'::uuid, '$invitation_two'::uuid) and status = 'pending'")"
receipt_count="$(psql_db -Atc "select count(*) from public.operation_receipts where operation_id in ('$accept_operation_one'::uuid, '$accept_operation_two'::uuid) and command_name = 'accept_organization_invitation_v1'")"
assignment_count="$(psql_db -Atc "select count(*) from public.student_teacher_assignments")"

if [[ "$membership_count" != '1' ||
      "$accepted_count" != '1' ||
      "$pending_count" != '1' ||
      "$receipt_count" != '1' ||
      "$assignment_count" != "$baseline_assignment_count" ]]; then
  echo "Acceptance concurrency invariant failed: membership=$membership_count accepted=$accepted_count pending=$pending_count receipts=$receipt_count assignments=$assignment_count baseline=$baseline_assignment_count" >&2
  exit 1
fi

echo 'Concurrent multi-identity Organization invitation acceptance regression passed.'
