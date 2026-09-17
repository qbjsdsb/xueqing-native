begin;
set local search_path = public, extensions;

select plan(28);

select has_table('public', 'app_users', 'AppUser table exists');
select has_table('public', 'organizations', 'Organization table exists');
select has_table('public', 'memberships', 'Membership table exists');
select has_table('public', 'students', 'Student table exists');
select has_table('public', 'student_subject_profiles', 'StudentSubjectProfile table exists');
select has_table('public', 'student_teacher_assignments', 'StudentTeacherAssignment table exists');
select has_table('public', 'observations', 'Observation table exists');
select has_table('public', 'operation_receipts', 'Operation receipt table exists');

select is(
    (
        select count(*)
        from pg_catalog.pg_class as relation
        join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
        where namespace.nspname = 'public'
          and relation.relname in (
              'app_users',
              'organizations',
              'memberships',
              'students',
              'student_subject_profiles',
              'student_teacher_assignments',
              'observations',
              'operation_receipts'
          )
          and relation.relrowsecurity
    ),
    8::bigint,
    'every exposed application table has RLS enabled'
);

select is(
    (
        select count(*)
        from information_schema.role_table_grants
        where table_schema = 'public'
          and table_name in (
              'app_users',
              'organizations',
              'memberships',
              'students',
              'student_subject_profiles',
              'student_teacher_assignments',
              'observations',
              'operation_receipts'
          )
          and grantee in ('anon', 'authenticated', 'PUBLIC')
    ),
    0::bigint,
    'anon/authenticated/PUBLIC receive no direct table privileges'
);

select ok(
    command.prosecdef,
    'CreateObservation is the explicit SECURITY DEFINER command boundary'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public' and command.proname = 'create_observation';

select ok(
    command.proconfig @> array['search_path=""']::text[],
    'SECURITY DEFINER command pins an empty search_path'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public' and command.proname = 'create_observation';

select ok(
    not pg_catalog.has_function_privilege('anon', command.oid, 'EXECUTE'),
    'anon cannot execute CreateObservation'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public' and command.proname = 'create_observation';

select ok(
    pg_catalog.has_function_privilege('authenticated', command.oid, 'EXECUTE'),
    'authenticated may execute CreateObservation'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public' and command.proname = 'create_observation';

-- Teacher A: valid operation.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated"}', true);
set local role authenticated;
select lives_ok(
    $$
    select public.create_observation(
        '90000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '课堂观察：概括题仍有原句搬运。',
        '2026-09-17T12:00:00Z'::timestamptz,
        '{"source":"android-quick-capture","fixture":true}'::jsonb
    )
    $$,
    'valid assigned teacher can append Observation'
);
reset role;

select is(
    (select count(*) from public.observations where operation_id = '90000000-0000-0000-0000-000000000001'),
    1::bigint,
    'valid command appends exactly one Observation'
);
select is(
    (select count(*) from public.operation_receipts where operation_id = '90000000-0000-0000-0000-000000000001'),
    1::bigint,
    'valid command commits exactly one operation receipt'
);
select is(
    (select (receipt.result_payload ->> 'observation_id')::uuid from public.operation_receipts as receipt where receipt.operation_id = '90000000-0000-0000-0000-000000000001'),
    (select observation.id from public.observations as observation where observation.operation_id = '90000000-0000-0000-0000-000000000001'),
    'receipt returns the authoritative committed Observation id'
);

-- Same operation + same payload is a receipt replay, not another side effect.
set local role authenticated;
select lives_ok(
    $$
    select public.create_observation(
        '90000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '课堂观察：概括题仍有原句搬运。',
        '2026-09-17T12:00:00Z'::timestamptz,
        '{"fixture":true,"source":"android-quick-capture"}'::jsonb
    )
    $$,
    'same operation_id and semantically equal JSON payload replays committed receipt'
);
reset role;
select is(
    (select count(*) from public.observations where operation_id = '90000000-0000-0000-0000-000000000001'),
    1::bigint,
    'idempotent replay does not duplicate Observation'
);

set local role authenticated;
select throws_ok(
    $$
    select public.create_observation(
        '90000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '同一 operation_id 但不同正文必须拒绝。',
        '2026-09-17T12:00:00Z'::timestamptz,
        '{"source":"android-quick-capture","fixture":true}'::jsonb
    )
    $$,
    'P0001',
    'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD',
    'same operation_id with different payload is deterministically rejected'
);
reset role;

-- Teacher A is a valid member in both organizations. Mixing Org B with Org A
-- Student/Profile/Assignment must still be rejected by the authoritative gate.
set local role authenticated;
select throws_ok(
    $$
    select public.create_observation(
        '90000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000002',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '跨机构混用实体必须失败。', null, '{}'::jsonb
    )
    $$,
    'P0001',
    'XQ_STUDENT_NOT_IN_ORG',
    'cross-organization entity mixing is rejected'
);
reset role;

-- Teacher B is an active teaching-capable member but has no assignment.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated"}', true);
set local role authenticated;
select throws_ok(
    $$
    select public.create_observation(
        '90000000-0000-0000-0000-000000000003',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '未任课教师不能写教学事实。', null, '{}'::jsonb
    )
    $$,
    'P0001',
    'XQ_TEACHER_ASSIGNMENT_REQUIRED',
    'active member without assignment is rejected'
);
reset role;

-- Teacher A + a Student with no active subject profile.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated"}', true);
set local role authenticated;
select throws_ok(
    $$
    select public.create_observation(
        '90000000-0000-0000-0000-000000000004',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000003',
        '40000000-0000-0000-0000-000000000099',
        '50000000-0000-0000-0000-000000000099',
        '没有学科档案时不能写教学事实。', null, '{}'::jsonb
    )
    $$,
    'P0001',
    'XQ_SUBJECT_PROFILE_REQUIRED',
    'missing active StudentSubjectProfile is rejected'
);
reset role;

-- Disabled membership fails before assignment authority can be borrowed.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000003', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000003","role":"authenticated"}', true);
set local role authenticated;
select throws_ok(
    $$
    select public.create_observation(
        '90000000-0000-0000-0000-000000000006',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '停用成员不能继续写入。', null, '{}'::jsonb
    )
    $$,
    'P0001',
    'XQ_MEMBERSHIP_DISABLED',
    'disabled membership is rejected'
);
reset role;

-- Force a failure after the Observation insert, when the receipt is about to be
-- written. PostgreSQL must roll the whole command back with zero half-write.
create function pg_temp.fail_test_receipt()
returns trigger
language plpgsql
as $$
begin
    raise exception 'TEST_FORCED_RECEIPT_FAILURE';
end;
$$;

create trigger force_receipt_failure
before insert on public.operation_receipts
for each row
when (new.operation_id = '90000000-0000-0000-0000-000000000005'::uuid)
execute function pg_temp.fail_test_receipt();

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated"}', true);
set local role authenticated;
select throws_ok(
    $$
    select public.create_observation(
        '90000000-0000-0000-0000-000000000005',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '强制 receipt 失败必须回滚 Observation。', null, '{}'::jsonb
    )
    $$,
    'P0001',
    'TEST_FORCED_RECEIPT_FAILURE',
    'failure after Observation insert aborts the command transaction'
);
reset role;

select is(
    (select count(*) from public.observations where operation_id = '90000000-0000-0000-0000-000000000005'),
    0::bigint,
    'transaction failure leaves no half-written Observation'
);
select is(
    (select count(*) from public.operation_receipts where operation_id = '90000000-0000-0000-0000-000000000005'),
    0::bigint,
    'transaction failure leaves no half-written receipt'
);

select * from finish();
rollback;
