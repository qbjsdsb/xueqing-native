begin;
set local search_path = public, extensions;

select plan(59);

select has_table(
    'public',
    'learning_case_verifications',
    'Verification fact table exists'
);

select ok(
    (
        select relation.relrowsecurity
        from pg_catalog.pg_class as relation
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = relation.relnamespace
        where namespace.nspname = 'public'
          and relation.relname = 'learning_case_verifications'
    ),
    'Verification table has RLS enabled'
);

select is(
    (
        select count(*)
        from information_schema.role_table_grants
        where table_schema = 'public'
          and table_name = 'learning_case_verifications'
          and grantee in ('anon', 'authenticated', 'PUBLIC')
    ),
    0::bigint,
    'Verification table exposes no direct app-role privileges'
);

select ok(
    command.prosecdef,
    'ReschedulePrimaryAction is SECURITY DEFINER'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'reschedule_primary_action';

select ok(
    command.proconfig @> array['search_path=""']::text[],
    'ReschedulePrimaryAction pins empty search_path'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'reschedule_primary_action';

select ok(
    not pg_catalog.has_function_privilege('anon', command.oid, 'EXECUTE'),
    'anon cannot execute ReschedulePrimaryAction'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'reschedule_primary_action';

select ok(
    pg_catalog.has_function_privilege('authenticated', command.oid, 'EXECUTE'),
    'authenticated may execute ReschedulePrimaryAction'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'reschedule_primary_action';

select ok(
    command.prosecdef,
    'RecordVerificationAndNextAction is SECURITY DEFINER'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'record_verification_and_next_action';

select ok(
    command.proconfig @> array['search_path=""']::text[],
    'RecordVerificationAndNextAction pins empty search_path'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'record_verification_and_next_action';

select ok(
    not pg_catalog.has_function_privilege('anon', command.oid, 'EXECUTE'),
    'anon cannot execute RecordVerificationAndNextAction'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'record_verification_and_next_action';

select ok(
    pg_catalog.has_function_privilege('authenticated', command.oid, 'EXECUTE'),
    'authenticated may execute RecordVerificationAndNextAction'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'record_verification_and_next_action';

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
    select public.create_learning_case(
        '95000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '行动推进回归：概括题',
        '先完成第一轮三道题复核',
        '2026-09-20'::date,
        null
    )
    $$,
    'fixture Case is created through the authoritative command'
);

reset role;

select set_config(
    'xq.test.case_id',
    (
        select (result_payload ->> 'case_id')
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000001'::uuid
    ),
    true
);
select set_config(
    'xq.test.action_id',
    (
        select (result_payload ->> 'primary_action_id')
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000001'::uuid
    ),
    true
);

set local role authenticated;

select lives_ok(
    $$
    select public.reschedule_primary_action(
        '95000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.test.case_id')::uuid,
        current_setting('xq.test.action_id')::uuid,
        1,
        1,
        '2026-09-22'::date
    )
    $$,
    'current primary Action can be rescheduled with matching versions'
);

reset role;

select is(
    (
        select version
        from public.learning_cases
        where id = current_setting('xq.test.case_id')::uuid
    ),
    2::bigint,
    'reschedule increments Case version'
);

select is(
    (
        select version
        from public.learning_case_actions
        where id = current_setting('xq.test.action_id')::uuid
    ),
    2::bigint,
    'reschedule increments Action version'
);

select is(
    (
        select due_on
        from public.learning_case_actions
        where id = current_setting('xq.test.action_id')::uuid
    ),
    '2026-09-22'::date,
    'reschedule changes authoritative due date'
);

select is(
    (
        select state
        from public.learning_cases
        where id = current_setting('xq.test.case_id')::uuid
    ),
    'new'::text,
    'reschedule does not change Case lifecycle state'
);

select is(
    (
        select count(*)
        from public.learning_case_actions
        where case_id = current_setting('xq.test.case_id')::uuid
          and action_role = 'primary'
          and status = 'pending'
    ),
    1::bigint,
    'reschedule preserves exactly one pending primary Action'
);

select is(
    (
        select count(*)
        from public.learning_case_events
        where operation_id = '95000000-0000-0000-0000-000000000002'::uuid
          and event_type = 'primary_action_rescheduled'
    ),
    1::bigint,
    'reschedule appends one provenance event'
);

select is(
    (
        select result_payload ->> 'responsible_teacher_app_user_id'
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000002'::uuid
    ),
    '10000000-0000-0000-0000-000000000001'::text,
    'reschedule receipt exposes the server-resolved responsible teacher'
);

select is(
    (
        select result_payload ->> 'owner_assignment_id'
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000002'::uuid
    ),
    '50000000-0000-0000-0000-000000000001'::text,
    'reschedule receipt exposes the authoritative owner assignment'
);

set local role authenticated;

select lives_ok(
    $$
    select public.reschedule_primary_action(
        '95000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.test.case_id')::uuid,
        current_setting('xq.test.action_id')::uuid,
        1,
        1,
        '2026-09-22'::date
    )
    $$,
    'same reschedule operation replays committed receipt despite current versions moving'
);

select throws_ok(
    $$
    select public.reschedule_primary_action(
        '95000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.test.case_id')::uuid,
        current_setting('xq.test.action_id')::uuid,
        1,
        1,
        '2026-09-23'::date
    )
    $$,
    'P0001',
    'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD',
    'reschedule operation id cannot be reused for a different due date'
);

select throws_ok(
    $$
    select public.reschedule_primary_action(
        '95000000-0000-0000-0000-000000000003',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.test.case_id')::uuid,
        current_setting('xq.test.action_id')::uuid,
        1,
        1,
        '2026-09-23'::date
    )
    $$,
    'P0001',
    'XQ_CASE_VERSION_CONFLICT',
    'stale Case version is rejected instead of last-write-wins'
);

select throws_ok(
    $$
    select public.reschedule_primary_action(
        '95000000-0000-0000-0000-000000000004',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.test.case_id')::uuid,
        current_setting('xq.test.action_id')::uuid,
        2,
        1,
        '2026-09-23'::date
    )
    $$,
    'P0001',
    'XQ_ACTION_VERSION_CONFLICT',
    'stale Action version is rejected'
);

select throws_ok(
    $$
    select public.reschedule_primary_action(
        '95000000-0000-0000-0000-000000000005',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.test.case_id')::uuid,
        current_setting('xq.test.action_id')::uuid,
        2,
        2,
        '2026-09-22'::date
    )
    $$,
    'P0001',
    'XQ_ACTION_DUE_UNCHANGED',
    'no-op reschedule is rejected'
);

select lives_ok(
    $$
    select public.record_verification_and_next_action(
        '95000000-0000-0000-0000-000000000006',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.test.case_id')::uuid,
        current_setting('xq.test.action_id')::uuid,
        2,
        2,
        'partially_met',
        '能压缩主要信息，但跨段概括仍会漏掉限制条件。',
        '下一轮改用两篇跨段材料，只检查限制条件是否保留。',
        '2026-09-24'::date
    )
    $$,
    'verification atomically completes current Action and creates next Action'
);

reset role;

select is(
    (
        select version
        from public.learning_cases
        where id = current_setting('xq.test.case_id')::uuid
    ),
    3::bigint,
    'verification progression increments Case version once'
);

select is(
    (
        select state
        from public.learning_cases
        where id = current_setting('xq.test.case_id')::uuid
    ),
    'new'::text,
    'Verification does not implicitly transition Case lifecycle'
);

select is(
    (
        select status || ':' || version::text
        from public.learning_case_actions
        where id = current_setting('xq.test.action_id')::uuid
    ),
    'completed:3'::text,
    'old primary Action is completed and versioned'
);

select ok(
    (
        select completed_at_server is not null
        from public.learning_case_actions
        where id = current_setting('xq.test.action_id')::uuid
    ),
    'completed Action records authoritative server completion time'
);

select is(
    (
        select count(*)
        from public.learning_case_verifications
        where operation_id = '95000000-0000-0000-0000-000000000006'::uuid
    ),
    1::bigint,
    'one immutable Verification fact is appended'
);

select is(
    (
        select outcome
        from public.learning_case_verifications
        where operation_id = '95000000-0000-0000-0000-000000000006'::uuid
    ),
    'partially_met'::text,
    'Verification preserves descriptive outcome'
);

select is(
    (
        select summary
        from public.learning_case_verifications
        where operation_id = '95000000-0000-0000-0000-000000000006'::uuid
    ),
    '能压缩主要信息，但跨段概括仍会漏掉限制条件。'::text,
    'Verification preserves teacher summary'
);

select is(
    (
        select count(*)
        from public.learning_case_actions
        where case_id = current_setting('xq.test.case_id')::uuid
          and action_role = 'primary'
          and status = 'pending'
    ),
    1::bigint,
    'open Case still has exactly one pending primary Action after verification'
);

select set_config(
    'xq.test.next_action_id',
    (
        select result_payload ->> 'next_primary_action_id'
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000006'::uuid
    ),
    true
);

select is(
    (
        select action_text
        from public.learning_case_actions
        where id = current_setting('xq.test.next_action_id')::uuid
    ),
    '下一轮改用两篇跨段材料，只检查限制条件是否保留。'::text,
    'new pending primary Action preserves explicit next step'
);

select is(
    (
        select due_on
        from public.learning_case_actions
        where id = current_setting('xq.test.next_action_id')::uuid
    ),
    '2026-09-24'::date,
    'new pending primary Action preserves explicit due date'
);

select is(
    (
        select version
        from public.learning_case_actions
        where id = current_setting('xq.test.next_action_id')::uuid
    ),
    1::bigint,
    'new pending primary Action starts at version 1'
);

select is(
    (
        select count(*)
        from public.learning_case_events
        where operation_id = '95000000-0000-0000-0000-000000000006'::uuid
          and event_type = 'verification_recorded_next_action_created'
    ),
    1::bigint,
    'verification progression appends one provenance event'
);

select is(
    (
        select count(*)
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000006'::uuid
    ),
    1::bigint,
    'verification progression commits one operation receipt'
);

select is(
    (
        select result_payload ->> 'responsible_teacher_app_user_id'
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000006'::uuid
    ),
    '10000000-0000-0000-0000-000000000001'::text,
    'verification receipt exposes the server-resolved responsible teacher'
);

select is(
    (
        select result_payload ->> 'owner_assignment_id'
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000006'::uuid
    ),
    '50000000-0000-0000-0000-000000000001'::text,
    'verification receipt exposes the authoritative owner assignment'
);

select is(
    (
        select result_payload ->> 'verification_summary'
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000006'::uuid
    ),
    '能压缩主要信息，但跨段概括仍会漏掉限制条件。'::text,
    'verification receipt echoes normalized teacher verification text'
);

select is(
    (
        select result_payload ->> 'next_action_text'
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000006'::uuid
    ),
    '下一轮改用两篇跨段材料，只检查限制条件是否保留。'::text,
    'verification receipt echoes normalized next Action text'
);

select is(
    (
        select result_payload ->> 'next_action_due_on'
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000006'::uuid
    ),
    '2026-09-24'::text,
    'verification receipt echoes the committed next Action due date'
);

set local role authenticated;

select lives_ok(
    $$
    select public.record_verification_and_next_action(
        '95000000-0000-0000-0000-000000000006',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.test.case_id')::uuid,
        current_setting('xq.test.action_id')::uuid,
        2,
        2,
        'partially_met',
        '  能压缩主要信息，但跨段概括仍会漏掉限制条件。  ',
        '  下一轮改用两篇跨段材料，只检查限制条件是否保留。  ',
        '2026-09-24'::date
    )
    $$,
    'same verification operation replays receipt even after Action has been replaced'
);

select throws_ok(
    $$
    select public.record_verification_and_next_action(
        '95000000-0000-0000-0000-000000000006',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.test.case_id')::uuid,
        current_setting('xq.test.action_id')::uuid,
        2,
        2,
        'partially_met',
        '能压缩主要信息，但跨段概括仍会漏掉限制条件。',
        '改成另一条下一步行动。',
        '2026-09-24'::date
    )
    $$,
    'P0001',
    'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD',
    'verification operation id cannot be reused for a different next Action'
);

reset role;

select is(
    (
        select count(*)
        from public.learning_case_verifications
        where case_id = current_setting('xq.test.case_id')::uuid
    ),
    1::bigint,
    'idempotent replay does not duplicate Verification'
);

select is(
    (
        select count(*)
        from public.learning_case_actions
        where case_id = current_setting('xq.test.case_id')::uuid
    ),
    2::bigint,
    'idempotent replay does not duplicate next Action'
);

-- A second legal Case is used to prove full rollback when the final receipt
-- write fails after Verification, completion and next-Action staging.
set local role authenticated;

select lives_ok(
    $$
    select public.create_learning_case(
        '95000000-0000-0000-0000-000000000010',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '行动推进回滚回归',
        '原始待验证行动',
        null,
        null
    )
    $$,
    'second fixture Case is created for atomic rollback proof'
);

reset role;

select set_config(
    'xq.test.rollback_case_id',
    (
        select result_payload ->> 'case_id'
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000010'::uuid
    ),
    true
);
select set_config(
    'xq.test.rollback_action_id',
    (
        select result_payload ->> 'primary_action_id'
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000010'::uuid
    ),
    true
);

create function pg_temp.fail_progression_receipt()
returns trigger
language plpgsql
as $$
begin
    raise exception 'TEST_FORCED_PROGRESSION_RECEIPT_FAILURE';
end;
$$;

create trigger force_progression_receipt_failure
before insert on public.operation_receipts
for each row
when (new.operation_id = '95000000-0000-0000-0000-000000000011'::uuid)
execute function pg_temp.fail_progression_receipt();

set local role authenticated;

select throws_ok(
    $$
    select public.record_verification_and_next_action(
        '95000000-0000-0000-0000-000000000011',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.test.rollback_case_id')::uuid,
        current_setting('xq.test.rollback_action_id')::uuid,
        1,
        1,
        'met',
        '这条 Verification 应整体回滚。',
        '这条新行动也不应留下。',
        null
    )
    $$,
    'P0001',
    'TEST_FORCED_PROGRESSION_RECEIPT_FAILURE',
    'receipt failure aborts the entire progression transaction'
);

reset role;

select is(
    (
        select status
        from public.learning_case_actions
        where id = current_setting('xq.test.rollback_action_id')::uuid
    ),
    'pending'::text,
    'rollback leaves original primary Action pending'
);

select is(
    (
        select version
        from public.learning_case_actions
        where id = current_setting('xq.test.rollback_action_id')::uuid
    ),
    1::bigint,
    'rollback leaves original Action version unchanged'
);

select is(
    (
        select version
        from public.learning_cases
        where id = current_setting('xq.test.rollback_case_id')::uuid
    ),
    1::bigint,
    'rollback leaves Case version unchanged'
);

select is(
    (
        select count(*)
        from public.learning_case_verifications
        where operation_id = '95000000-0000-0000-0000-000000000011'::uuid
    ),
    0::bigint,
    'rollback leaves no half-written Verification'
);

select is(
    (
        select count(*)
        from public.learning_case_events
        where operation_id = '95000000-0000-0000-0000-000000000011'::uuid
    ),
    0::bigint,
    'rollback leaves no half-written Case event'
);

select is(
    (
        select count(*)
        from public.learning_case_actions
        where case_id = current_setting('xq.test.rollback_case_id')::uuid
          and id <> current_setting('xq.test.rollback_action_id')::uuid
    ),
    0::bigint,
    'rollback leaves no half-written next Action'
);

-- Management membership never substitutes for teaching assignment.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated","iss":"supabase-demo"}',
    true
);

set local role authenticated;

select throws_ok(
    $$
    select public.reschedule_primary_action(
        '95000000-0000-0000-0000-000000000020',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.test.rollback_case_id')::uuid,
        current_setting('xq.test.rollback_action_id')::uuid,
        1,
        1,
        '2026-09-25'::date
    )
    $$,
    'P0001',
    'XQ_TEACHER_ASSIGNMENT_REQUIRED',
    'member without the legal assignment cannot reschedule another teacher Action'
);

select throws_ok(
    $$
    select public.record_verification_and_next_action(
        '95000000-0000-0000-0000-000000000021',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.test.rollback_case_id')::uuid,
        current_setting('xq.test.rollback_action_id')::uuid,
        1,
        1,
        'uncertain',
        '另一个老师不能写入这条 Verification。',
        '另一个老师也不能建立下一步。',
        null
    )
    $$,
    'P0001',
    'XQ_TEACHER_ASSIGNMENT_REQUIRED',
    'member without the legal assignment cannot verify another teacher Action'
);

reset role;

select * from finish();
rollback;
