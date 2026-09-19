begin;
set local search_path = public, extensions;

select plan(41);

select has_table('public', 'learning_cases', 'Learning Case table exists');
select has_table('public', 'learning_case_actions', 'Learning Case Action table exists');
select has_table('public', 'learning_case_events', 'Learning Case Event table exists');
select has_table('public', 'learning_case_observation_links', 'Case/Observation link table exists');

select is(
    (
        select count(*)
        from pg_catalog.pg_class as relation
        join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
        where namespace.nspname = 'public'
          and relation.relname in (
              'learning_cases',
              'learning_case_actions',
              'learning_case_events',
              'learning_case_observation_links'
          )
          and relation.relrowsecurity
    ),
    4::bigint,
    'every new Case table has RLS enabled'
);

select is(
    (
        select count(*)
        from information_schema.role_table_grants
        where table_schema = 'public'
          and table_name in (
              'learning_cases',
              'learning_case_actions',
              'learning_case_events',
              'learning_case_observation_links'
          )
          and grantee in ('anon', 'authenticated', 'PUBLIC')
    ),
    0::bigint,
    'anon/authenticated/PUBLIC receive no direct Case table privileges'
);

select ok(
    command.prosecdef,
    'CreateLearningCase is an explicit SECURITY DEFINER command boundary'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public' and command.proname = 'create_learning_case';

select ok(
    command.proconfig @> array['search_path=""']::text[],
    'CreateLearningCase pins an empty search_path'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public' and command.proname = 'create_learning_case';

select ok(
    not pg_catalog.has_function_privilege('anon', command.oid, 'EXECUTE'),
    'anon cannot execute CreateLearningCase'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public' and command.proname = 'create_learning_case';

select ok(
    pg_catalog.has_function_privilege('authenticated', command.oid, 'EXECUTE'),
    'authenticated may execute CreateLearningCase'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public' and command.proname = 'create_learning_case';

select ok(
    exists (
        select 1
        from pg_catalog.pg_class as index_relation
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = index_relation.relnamespace
        join pg_catalog.pg_index as index_definition
          on index_definition.indexrelid = index_relation.oid
        where namespace.nspname = 'public'
          and index_relation.relname = 'learning_case_one_pending_primary_action'
          and index_definition.indisunique
          and index_definition.indpred is not null
    ),
    'database enforces at most one pending primary Action per Case'
);

-- Teacher A creates an authoritative Observation to use as the Case source.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo"}',
    true
);

set local role authenticated;
select lives_ok(
    $$
    select public.create_observation(
        '91000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '课堂观察：概括题仍然倾向直接搬运原句。',
        '2026-09-19T06:00:00Z'::timestamptz,
        '{"fixture":true,"source":"case-test"}'::jsonb
    )
    $$,
    'assigned teacher can create the source Observation'
);
reset role;

select set_config(
    'xq.test.source_observation_id',
    (
        select observation.id::text
        from public.observations as observation
        where observation.operation_id =
            '91000000-0000-0000-0000-000000000001'::uuid
    ),
    true
);

set local role authenticated;
select lives_ok(
    $$
    select public.create_learning_case(
        '92000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '信息提取与概括仍不稳定',
        '下节课用三道陌生材料重新检查概括压缩',
        '2026-09-22'::date,
        current_setting('xq.test.source_observation_id')::uuid
    )
    $$,
    'valid assigned teacher atomically creates Case + primary Action'
);
reset role;

select is(
    (select count(*) from public.learning_cases where title = '信息提取与概括仍不稳定'),
    1::bigint,
    'valid command creates exactly one Learning Case'
);

select is(
    (
        select count(*)
        from public.learning_case_actions as action
        join public.learning_cases as learning_case on learning_case.id = action.case_id
        where learning_case.title = '信息提取与概括仍不稳定'
    ),
    1::bigint,
    'valid command creates exactly one Action for the Case'
);

select is(
    (
        select count(*)
        from public.learning_case_events
        where operation_id = '92000000-0000-0000-0000-000000000001'::uuid
    ),
    1::bigint,
    'valid command creates exactly one provenance event'
);

select is(
    (
        select count(*)
        from public.learning_case_observation_links
        where operation_id = '92000000-0000-0000-0000-000000000001'::uuid
    ),
    1::bigint,
    'valid command links exactly one source Observation'
);

select is(
    (
        select count(*)
        from public.operation_receipts
        where operation_id = '92000000-0000-0000-0000-000000000001'::uuid
    ),
    1::bigint,
    'valid command commits exactly one operation receipt'
);

select is(
    (select state from public.learning_cases where title = '信息提取与概括仍不稳定'),
    'new'::text,
    'new Learning Case begins in new state'
);

select is(
    (select version from public.learning_cases where title = '信息提取与概括仍不稳定'),
    1::bigint,
    'new Learning Case begins at version 1'
);

select is(
    (
        select responsible_teacher_app_user_id
        from public.learning_cases
        where title = '信息提取与概括仍不稳定'
    ),
    '10000000-0000-0000-0000-000000000001'::uuid,
    'responsible teacher is derived from the live legal assignment'
);

select is(
    (
        select action.action_role || ':' || action.status
        from public.learning_case_actions as action
        join public.learning_cases as learning_case on learning_case.id = action.case_id
        where learning_case.title = '信息提取与概括仍不稳定'
    ),
    'primary:pending'::text,
    'new open Case has one pending primary Action'
);

select is(
    (
        select action.assignee_teacher_app_user_id
        from public.learning_case_actions as action
        join public.learning_cases as learning_case on learning_case.id = action.case_id
        where learning_case.title = '信息提取与概括仍不稳定'
    ),
    '10000000-0000-0000-0000-000000000001'::uuid,
    'primary Action assignee is the verified responsible teacher'
);

select is(
    (
        select action.due_on
        from public.learning_case_actions as action
        join public.learning_cases as learning_case on learning_case.id = action.case_id
        where learning_case.title = '信息提取与概括仍不稳定'
    ),
    '2026-09-22'::date,
    'primary Action preserves the selected business due date'
);

select is(
    (
        select (event.event_payload ->> 'primary_action_id')::uuid
        from public.learning_case_events as event
        where event.operation_id = '92000000-0000-0000-0000-000000000001'::uuid
    ),
    (
        select action.id
        from public.learning_case_actions as action
        join public.learning_cases as learning_case on learning_case.id = action.case_id
        where learning_case.title = '信息提取与概括仍不稳定'
    ),
    'case_created event points at the authoritative primary Action'
);

select is(
    (
        select link.observation_id
        from public.learning_case_observation_links as link
        where link.operation_id = '92000000-0000-0000-0000-000000000001'::uuid
    ),
    (
        select observation.id
        from public.observations as observation
        where observation.operation_id =
            '91000000-0000-0000-0000-000000000001'::uuid
    ),
    'source Observation link preserves authoritative Observation identity'
);

-- Same operation + normalized equivalent payload is a receipt replay.
set local role authenticated;
select lives_ok(
    $$
    select public.create_learning_case(
        '92000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '  信息提取与概括仍不稳定  ',
        '  下节课用三道陌生材料重新检查概括压缩  ',
        '2026-09-22'::date,
        current_setting('xq.test.source_observation_id')::uuid
    )
    $$,
    'same operation_id and normalized payload replays the committed receipt'
);
reset role;

select is(
    (select count(*) from public.learning_cases where title = '信息提取与概括仍不稳定'),
    1::bigint,
    'idempotent replay does not duplicate the Case'
);

select is(
    (
        select count(*)
        from public.learning_case_actions as action
        join public.learning_cases as learning_case on learning_case.id = action.case_id
        where learning_case.title = '信息提取与概括仍不稳定'
    ),
    1::bigint,
    'idempotent replay does not duplicate the primary Action'
);

select is(
    (
        select count(*)
        from public.learning_case_events
        where operation_id = '92000000-0000-0000-0000-000000000001'::uuid
    ),
    1::bigint,
    'idempotent replay does not duplicate the Case event'
);

select is(
    (
        select count(*)
        from public.learning_case_observation_links
        where operation_id = '92000000-0000-0000-0000-000000000001'::uuid
    ),
    1::bigint,
    'idempotent replay does not duplicate the Observation link'
);

set local role authenticated;
select throws_ok(
    $$
    select public.create_learning_case(
        '92000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '同一 operation_id 但改变标题',
        '下节课用三道陌生材料重新检查概括压缩',
        '2026-09-22'::date,
        null
    )
    $$,
    'P0001',
    'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD',
    'same operation_id with a different payload is rejected'
);
reset role;

-- Cross-organization entity mixing must fail even though Teacher A is a valid
-- active teaching member of both organizations.
set local role authenticated;
select throws_ok(
    $$
    select public.create_learning_case(
        '92000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000002',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '跨机构 Case',
        '不应创建的行动',
        null,
        null
    )
    $$,
    'P0001',
    'XQ_STUDENT_NOT_IN_ORG',
    'cross-organization entity mixing is rejected'
);
reset role;

-- Teacher B can teach in the organization but has no legal assignment.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated","iss":"supabase-demo"}',
    true
);

set local role authenticated;
select throws_ok(
    $$
    select public.create_learning_case(
        '92000000-0000-0000-0000-000000000003',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '未任课教师不能建 Case',
        '不应创建的行动',
        null,
        null
    )
    $$,
    'P0001',
    'XQ_TEACHER_ASSIGNMENT_REQUIRED',
    'active member without assignment cannot create a Case'
);
reset role;

-- Build one real Observation in Org B so the source-scope test proves a
-- mismatched existing fact is rejected, rather than merely a missing UUID.
insert into public.student_teacher_assignments (
    id,
    organization_id,
    student_id,
    subject_profile_id,
    teacher_app_user_id,
    active
) values (
    '50000000-0000-0000-0000-000000000002',
    '20000000-0000-0000-0000-000000000002',
    '30000000-0000-0000-0000-000000000002',
    '40000000-0000-0000-0000-000000000002',
    '10000000-0000-0000-0000-000000000001',
    true
);

insert into public.observations (
    id,
    operation_id,
    organization_id,
    student_id,
    subject_profile_id,
    assignment_id,
    actor_app_user_id,
    raw_text
) values (
    '60000000-0000-0000-0000-000000000002',
    '93000000-0000-0000-0000-000000000002',
    '20000000-0000-0000-0000-000000000002',
    '30000000-0000-0000-0000-000000000002',
    '40000000-0000-0000-0000-000000000002',
    '50000000-0000-0000-0000-000000000002',
    '10000000-0000-0000-0000-000000000001',
    '另一机构的虚构观察'
);

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo"}',
    true
);

set local role authenticated;
select throws_ok(
    $$
    select public.create_learning_case(
        '92000000-0000-0000-0000-000000000004',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        'Observation scope 必须匹配',
        '不应创建的行动',
        null,
        '60000000-0000-0000-0000-000000000002'
    )
    $$,
    'P0001',
    'XQ_SOURCE_OBSERVATION_SCOPE_MISMATCH',
    'existing Observation from another scope cannot seed the Case'
);
reset role;

-- Force a failure at the final receipt write. Every earlier Case side effect
-- must roll back with the same transaction.
create function pg_temp.fail_case_receipt()
returns trigger
language plpgsql
as $$
begin
    raise exception 'TEST_FORCED_CASE_RECEIPT_FAILURE';
end;
$$;

create trigger force_case_receipt_failure
before insert on public.operation_receipts
for each row
when (new.operation_id = '92000000-0000-0000-0000-000000000005'::uuid)
execute function pg_temp.fail_case_receipt();

set local role authenticated;
select throws_ok(
    $$
    select public.create_learning_case(
        '92000000-0000-0000-0000-000000000005',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '强制失败不能留下半个 Case',
        '强制失败不能留下半个行动',
        null,
        current_setting('xq.test.source_observation_id')::uuid
    )
    $$,
    'P0001',
    'TEST_FORCED_CASE_RECEIPT_FAILURE',
    'receipt failure aborts the entire Case command transaction'
);
reset role;

select is(
    (select count(*) from public.learning_cases where title = '强制失败不能留下半个 Case'),
    0::bigint,
    'atomic failure leaves no half-written Learning Case'
);

select is(
    (select count(*) from public.learning_case_actions where action_text = '强制失败不能留下半个行动'),
    0::bigint,
    'atomic failure leaves no half-written primary Action'
);

select is(
    (
        select count(*)
        from public.learning_case_events
        where operation_id = '92000000-0000-0000-0000-000000000005'::uuid
    ),
    0::bigint,
    'atomic failure leaves no half-written Case event'
);

select is(
    (
        select count(*)
        from public.learning_case_observation_links
        where operation_id = '92000000-0000-0000-0000-000000000005'::uuid
    ),
    0::bigint,
    'atomic failure leaves no half-written Observation link'
);

select is(
    (
        select count(*)
        from public.operation_receipts
        where operation_id = '92000000-0000-0000-0000-000000000005'::uuid
    ),
    0::bigint,
    'atomic failure leaves no half-written receipt'
);

select * from finish();
rollback;
