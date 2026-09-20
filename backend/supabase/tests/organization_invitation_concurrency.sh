#!/usr/bin/env bash
set -euo pipefail

operation_id='91000000-0000-0000-0000-00000000cc01'
organization_id='20000000-0000-0000-0000-000000000001'
actor_id='10000000-0000-0000-0000-000000000001'
auth_subject='a0000000-0000-0000-0000-000000000001'
auth_issuer='supabase-demo'
invite_email='concurrency.invite@example.com'
lock_key=42424271
holder_app='xueqing_org_invite_lock_holder'
writer_app='xueqing_org_invite_writer'

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
delete from public.organization_invitations where create_operation_id = '$operation_id'::uuid;
delete from public.operation_receipts where operation_id = '$operation_id'::uuid;
update public.identity_links
   set active = true
 where provider_key = 'supabase'
   and issuer = '$auth_issuer'
   and external_subject = '$auth_subject'
   and app_user_id = '$actor_id'::uuid;
update public.app_users
   set enabled = true
 where id = '$actor_id'::uuid;
update public.memberships
   set status = 'active', membership_role = 'owner'
 where organization_id = '$organization_id'::uuid
   and app_user_id = '$actor_id'::uuid;
drop trigger if exists test_hold_create_org_invitation on public.organization_invitations;
drop function if exists public.test_hold_create_org_invitation();
SQL
}
trap cleanup EXIT

baseline_assignment_count="$(psql_db -Atc "select count(*) from public.student_teacher_assignments")"

# Pause immediately before invitation insertion. By this point the command has
# locked the live IdentityLink, AppUser and management Membership.
psql_db <<SQL
create or replace function public.test_hold_create_org_invitation()
returns trigger
language plpgsql
set search_path = ''
as \$\$
begin
  perform pg_catalog.pg_advisory_xact_lock($lock_key);
  return new;
end;
\$\$;

drop trigger if exists test_hold_create_org_invitation on public.organization_invitations;
create trigger test_hold_create_org_invitation
before insert on public.organization_invitations
for each row
when (new.create_operation_id = '$operation_id'::uuid)
execute function public.test_hold_create_org_invitation();
SQL

psql_db > /tmp/xueqing-org-invite-lock-holder.log 2>&1 <<SQL &
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
  echo 'Invitation test holder never acquired the advisory lock.' >&2
  exit 1
fi

psql_db > /tmp/xueqing-create-org-invitation.log 2>&1 <<SQL &
set application_name = '$writer_app';
select pg_catalog.set_config('request.jwt.claim.sub', '$auth_subject', false);
select pg_catalog.set_config('request.jwt.claim.role', 'authenticated', false);
select pg_catalog.set_config('request.jwt.claims', '{"sub":"$auth_subject","role":"authenticated","iss":"$auth_issuer"}', false);
set role authenticated;
select public.create_organization_invitation_v1(
  '$operation_id'::uuid,
  '$organization_id'::uuid,
  '$invite_email',
  'teacher',
  true
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
  echo 'CreateOrganizationInvitation never reached the post-authorization test barrier.' >&2
  cat /tmp/xueqing-create-org-invitation.log >&2 || true
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
    echo "$label unexpectedly bypassed the in-flight invitation authority lock." >&2
    exit 1
  fi
  if ! grep -qi 'lock timeout' <<<"$output"; then
    echo "$label failed for an unexpected reason:" >&2
    echo "$output" >&2
    exit 1
  fi
  echo "$label: revoke correctly blocked while CreateOrganizationInvitation was in flight."
}

expect_revoke_blocked   'IdentityLink revoke'   "update public.identity_links set active = false where provider_key = 'supabase' and issuer = '$auth_issuer' and external_subject = '$auth_subject';"
expect_revoke_blocked   'AppUser disable'   "update public.app_users set enabled = false where id = '$actor_id'::uuid;"
expect_revoke_blocked   'Membership disable'   "update public.memberships set status = 'disabled' where organization_id = '$organization_id'::uuid and app_user_id = '$actor_id'::uuid;"
expect_revoke_blocked   'Management role demotion'   "update public.memberships set membership_role = 'teacher' where organization_id = '$organization_id'::uuid and app_user_id = '$actor_id'::uuid;"

psql_db -Atc "select pg_catalog.pg_terminate_backend(pid) from pg_catalog.pg_stat_activity where application_name = '$holder_app'" >/dev/null
wait "$holder_pid" 2>/dev/null || true
holder_pid=''
wait "$writer_pid"
writer_pid=''

invitation_count="$(psql_db -Atc "select count(*) from public.organization_invitations where create_operation_id = '$operation_id'::uuid and status = 'pending'")"
receipt_count="$(psql_db -Atc "select count(*) from public.operation_receipts where operation_id = '$operation_id'::uuid and command_name = 'create_organization_invitation_v1'")"
assignment_count="$(psql_db -Atc "select count(*) from public.student_teacher_assignments")"

if [[ "$invitation_count" != '1' || "$receipt_count" != '1' || "$assignment_count" != "$baseline_assignment_count" ]]; then
  echo "Expected one invitation + receipt and unchanged assignments; got invitation=$invitation_count receipt=$receipt_count assignments=$assignment_count baseline=$baseline_assignment_count" >&2
  cat /tmp/xueqing-create-org-invitation.log >&2 || true
  exit 1
fi

echo 'Concurrent CreateOrganizationInvitation authority-revoke regression passed.'
