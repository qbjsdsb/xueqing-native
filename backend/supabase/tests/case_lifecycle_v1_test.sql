begin;
set local search_path = public, extensions;

select plan(60);

select ok(
    exists (
        select 1 from pg_catalog.pg_proc p
        join pg_catalog.pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public' and p.proname = 'transition_learning_case_state'
    ),
    'TransitionLearningCaseState function exists'
);

select ok(
    exists (
        select 1 from pg_catalog.pg_proc p
        join pg_catalog.pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public' and p.proname = 'close_learning_case'
    ),
    'CloseLearningCase function exists'
);

select ok(
    exists (
        select 1 from pg_catalog.pg_proc p
        join pg_catalog.pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public' and p.proname = 'reopen_learning_case'
    ),
    'ReopenLearningCase function exists'
);

select ok(
    (
        select p.prosecdef
        from pg_catalog.pg_proc p
        join pg_catalog.pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public' and p.proname = 'transition_learning_case_state'
        limit 1
    ),
    'TransitionLearningCaseState is SECURITY DEFINER'
);

select ok(
    (
        select p.prosecdef
        from pg_catalog.pg_proc p
        join pg_catalog.pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public' and p.proname = 'close_learning_case'
        limit 1
    ),
    'CloseLearningCase is SECURITY DEFINER'
);

select ok(
    (
        select p.prosecdef
        from pg_catalog.pg_proc p
        join pg_catalog.pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public' and p.proname = 'reopen_learning_case'
        limit 1
    ),
    'ReopenLearningCase is SECURITY DEFINER'
);

select ok(
    not pg_catalog.has_function_privilege(
        'anon',
        'public.transition_learning_case_state(uuid,uuid,uuid,uuid,uuid,uuid,bigint,text)',
        'EXECUTE'
    ),
    'anon cannot transition Case state'
);

select ok(
    pg_catalog.has_function_privilege(
        'authenticated',
        'public.transition_learning_case_state(uuid,uuid,uuid,uuid,uuid,uuid,bigint,text)',
        'EXECUTE'
    ),
    'authenticated may transition Case state'
);

select ok(
    not pg_catalog.has_function_privilege(
        'anon',
        'public.close_learning_case(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint)',
        'EXECUTE'
    ),
    'anon cannot close Case'
);

select ok(
    pg_catalog.has_function_privilege(
        'authenticated',
        'public.close_learning_case(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint)',
        'EXECUTE'
    ),
    'authenticated may close Case'
);

select ok(
    not pg_catalog.has_function_privilege(
        'anon',
        'public.reopen_learning_case(uuid,uuid,uuid,uuid,uuid,uuid,bigint,text,date)',
        'EXECUTE'
    ),
    'anon cannot reopen Case'
);

select ok(
    pg_catalog.has_function_privilege(
        'authenticated',
        'public.reopen_learning_case(uuid,uuid,uuid,uuid,uuid,uuid,bigint,text,date)',
        'EXECUTE'
    ),
    'authenticated may reopen Case'
);

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
        '97000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '生命周期回归：跨段概括',
        '先完成两篇跨段材料并记录限制条件',
        '2026-09-22'::date,
        null
    )
    $$,
    'fixture Case is created through authoritative command'
);

reset role;

select set_config(
    'xq.lifecycle.case_id',
    (
        select result_payload ->> 'case_id'
        from public.operation_receipts
        where operation_id = '97000000-0000-0000-0000-000000000001'::uuid
    ),
    true
);
select set_config(
    'xq.lifecycle.action_id',
    (
        select result_payload ->> 'primary_action_id'
        from public.operation_receipts
        where operation_id = '97000000-0000-0000-0000-000000000001'::uuid
    ),
    true
);

set local role authenticated;

select lives_ok(
    $$
    select public.transition_learning_case_state(
        '97000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        1,
        'confirmed'
    )
    $$,
    'new Case can be confirmed'
);

reset role;

select is(
    (select state from public.learning_cases where id = current_setting('xq.lifecycle.case_id')::uuid),
    'confirmed'::text,
    'confirm transition changes Case state'
);

select is(
    (select version from public.learning_cases where id = current_setting('xq.lifecycle.case_id')::uuid),
    2::bigint,
    'confirm transition increments Case version'
);

select is(
    (
        select count(*) from public.learning_case_actions
        where case_id = current_setting('xq.lifecycle.case_id')::uuid
          and action_role = 'primary'
          and status = 'pending'
    ),
    1::bigint,
    'open-state transition preserves exactly one pending primary Action'
);

select is(
    (
        select count(*) from public.learning_case_events
        where operation_id = '97000000-0000-0000-0000-000000000002'::uuid
          and event_type = 'case_state_transitioned'
    ),
    1::bigint,
    'confirm appends one lifecycle event'
);

select is(
    (
        select count(*) from public.operation_receipts
        where operation_id = '97000000-0000-0000-0000-000000000002'::uuid
          and command_name = 'transition_learning_case_state_v1'
    ),
    1::bigint,
    'confirm commits one operation receipt'
);

set local role authenticated;

select lives_ok(
    $$
    select public.transition_learning_case_state(
        '97000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        1,
        'confirmed'
    )
    $$,
    'same transition operation replays committed receipt'
);

select throws_ok(
    $$
    select public.transition_learning_case_state(
        '97000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        1,
        'intervening'
    )
    $$,
    'P0001',
    'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD',
    'transition operation id cannot be reused for another target state'
);

select throws_ok(
    $$
    select public.transition_learning_case_state(
        '97000000-0000-0000-0000-000000000003',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        2,
        'pending_verification'
    )
    $$,
    'P0001',
    'XQ_INVALID_CASE_TRANSITION',
    'lifecycle cannot skip intervening state'
);

select throws_ok(
    $$
    select public.transition_learning_case_state(
        '97000000-0000-0000-0000-000000000004',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        1,
        'intervening'
    )
    $$,
    'P0001',
    'XQ_CASE_VERSION_CONFLICT',
    'stale Case version is rejected'
);

select lives_ok(
    $$
    select public.transition_learning_case_state(
        '97000000-0000-0000-0000-000000000005',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        2,
        'intervening'
    )
    $$,
    'confirmed Case can enter intervening'
);

select lives_ok(
    $$
    select public.transition_learning_case_state(
        '97000000-0000-0000-0000-000000000006',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        3,
        'pending_verification'
    )
    $$,
    'intervening Case can enter pending_verification'
);

select lives_ok(
    $$
    select public.transition_learning_case_state(
        '97000000-0000-0000-0000-000000000007',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        4,
        'stable'
    )
    $$,
    'pending_verification Case can become stable'
);

reset role;

select is(
    (select state from public.learning_cases where id = current_setting('xq.lifecycle.case_id')::uuid),
    'stable'::text,
    'forward lifecycle reaches stable'
);

select is(
    (select version from public.learning_cases where id = current_setting('xq.lifecycle.case_id')::uuid),
    5::bigint,
    'four forward transitions produce Case version 5'
);

set local role authenticated;

select throws_ok(
    $$
    select public.close_learning_case(
        '97000000-0000-0000-0000-000000000008',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        current_setting('xq.lifecycle.action_id')::uuid,
        5,
        99
    )
    $$,
    'P0001',
    'XQ_ACTION_VERSION_CONFLICT',
    'close rejects stale Action version'
);

select lives_ok(
    $$
    select public.close_learning_case(
        '97000000-0000-0000-0000-000000000009',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        current_setting('xq.lifecycle.action_id')::uuid,
        5,
        1
    )
    $$,
    'stable Case can close atomically'
);

reset role;

select is(
    (select state from public.learning_cases where id = current_setting('xq.lifecycle.case_id')::uuid),
    'closed'::text,
    'close changes Case state to closed'
);

select is(
    (select version from public.learning_cases where id = current_setting('xq.lifecycle.case_id')::uuid),
    6::bigint,
    'close increments Case version'
);

select is(
    (
        select status || ':' || version::text
        from public.learning_case_actions
        where id = current_setting('xq.lifecycle.action_id')::uuid
    ),
    'cancelled:2'::text,
    'close cancels and versions the pending primary Action'
);

select is(
    (
        select count(*) from public.learning_case_actions
        where case_id = current_setting('xq.lifecycle.case_id')::uuid
          and action_role = 'primary'
          and status = 'pending'
    ),
    0::bigint,
    'closed Case has zero pending primary Actions'
);

select is(
    (
        select count(*) from public.learning_case_events
        where operation_id = '97000000-0000-0000-0000-000000000009'::uuid
          and event_type = 'case_closed'
    ),
    1::bigint,
    'close appends one close event'
);

select is(
    (
        select result_payload ->> 'cancelled_action_version'
        from public.operation_receipts
        where operation_id = '97000000-0000-0000-0000-000000000009'::uuid
    ),
    '2'::text,
    'close receipt exposes cancelled Action version'
);

set local role authenticated;

select throws_ok(
    $$
    select public.reschedule_primary_action(
        '97000000-0000-0000-0000-000000000010',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        current_setting('xq.lifecycle.action_id')::uuid,
        6,
        2,
        '2026-09-25'::date
    )
    $$,
    'P0001',
    'XQ_CASE_CLOSED',
    'existing Action mutation command still fails closed after close'
);

select throws_ok(
    $$
    select public.reopen_learning_case(
        '97000000-0000-0000-0000-000000000011',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        5,
        '重新进入干预：检查新材料中的限制条件',
        '2026-09-27'::date
    )
    $$,
    'P0001',
    'XQ_CASE_VERSION_CONFLICT',
    'reopen rejects stale Case version'
);

select lives_ok(
    $$
    select public.reopen_learning_case(
        '97000000-0000-0000-0000-000000000012',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        6,
        '  重新进入干预：检查新材料中的限制条件  ',
        '2026-09-27'::date
    )
    $$,
    'closed Case can reopen with a new primary Action'
);

reset role;

select set_config(
    'xq.lifecycle.reopened_action_id',
    (
        select result_payload ->> 'new_primary_action_id'
        from public.operation_receipts
        where operation_id = '97000000-0000-0000-0000-000000000012'::uuid
    ),
    true
);

select is(
    (select state from public.learning_cases where id = current_setting('xq.lifecycle.case_id')::uuid),
    'intervening'::text,
    'reopen returns Case to intervening'
);

select is(
    (select version from public.learning_cases where id = current_setting('xq.lifecycle.case_id')::uuid),
    7::bigint,
    'reopen increments Case version'
);

select is(
    (
        select count(*) from public.learning_case_actions
        where case_id = current_setting('xq.lifecycle.case_id')::uuid
          and action_role = 'primary'
          and status = 'pending'
    ),
    1::bigint,
    'reopened Case has exactly one pending primary Action'
);

select is(
    (
        select action_text
        from public.learning_case_actions
        where id = current_setting('xq.lifecycle.reopened_action_id')::uuid
    ),
    '重新进入干预：检查新材料中的限制条件'::text,
    'reopen stores normalized new Action text'
);

select is(
    (
        select version
        from public.learning_case_actions
        where id = current_setting('xq.lifecycle.reopened_action_id')::uuid
    ),
    1::bigint,
    'reopened primary Action starts at version 1'
);

select is(
    (
        select count(*) from public.learning_case_events
        where operation_id = '97000000-0000-0000-0000-000000000012'::uuid
          and event_type = 'case_reopened'
    ),
    1::bigint,
    'reopen appends one explicit reopen event'
);

set local role authenticated;

select lives_ok(
    $$
    select public.reopen_learning_case(
        '97000000-0000-0000-0000-000000000012',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        6,
        '重新进入干预：检查新材料中的限制条件',
        '2026-09-27'::date
    )
    $$,
    'same reopen operation replays the committed receipt'
);

reset role;

select is(
    (
        select count(*) from public.learning_case_actions
        where case_id = current_setting('xq.lifecycle.case_id')::uuid
          and action_role = 'primary'
          and status = 'pending'
    ),
    1::bigint,
    'reopen replay does not duplicate the pending Action'
);

set local role authenticated;

select throws_ok(
    $$
    select public.reopen_learning_case(
        '97000000-0000-0000-0000-000000000012',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        6,
        '换成不同的下一步',
        '2026-09-27'::date
    )
    $$,
    'P0001',
    'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD',
    'reopen operation id cannot be reused for different Action text'
);

select throws_ok(
    $$
    select public.close_learning_case(
        '97000000-0000-0000-0000-000000000013',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        current_setting('xq.lifecycle.reopened_action_id')::uuid,
        7,
        1
    )
    $$,
    'P0001',
    'XQ_CASE_NOT_STABLE',
    'intervening Case cannot close directly'
);

reset role;

-- Teacher B is an active organization member but has no legal assignment.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated","iss":"supabase-demo"}',
    true
);

set local role authenticated;

select throws_ok(
    $$
    select public.transition_learning_case_state(
        '97000000-0000-0000-0000-000000000014',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.case_id')::uuid,
        7,
        'pending_verification'
    )
    $$,
    'P0001',
    'XQ_TEACHER_ASSIGNMENT_REQUIRED',
    'organization member without assignment cannot transition another teacher Case'
);

reset role;

-- Restore Teacher A for atomic rollback fixtures.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo"}',
    true
);

set local role authenticated;

select lives_ok(
    $$
    select public.create_learning_case(
        '97000000-0000-0000-0000-000000000020',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '生命周期原子回滚：关闭',
        '保持待办直到关闭事务真正提交',
        null,
        null
    )
    $$,
    'close rollback fixture is created'
);

reset role;

select set_config(
    'xq.lifecycle.rollback_close_case_id',
    (
        select result_payload ->> 'case_id'
        from public.operation_receipts
        where operation_id = '97000000-0000-0000-0000-000000000020'::uuid
    ),
    true
);
select set_config(
    'xq.lifecycle.rollback_close_action_id',
    (
        select result_payload ->> 'primary_action_id'
        from public.operation_receipts
        where operation_id = '97000000-0000-0000-0000-000000000020'::uuid
    ),
    true
);

update public.learning_cases
   set state = 'stable', version = 5
 where id = current_setting('xq.lifecycle.rollback_close_case_id')::uuid;

create function pg_temp.fail_case_lifecycle_receipt()
returns trigger
language plpgsql
as $$
begin
    raise exception 'TEST_FORCED_CASE_LIFECYCLE_RECEIPT_FAILURE';
end;
$$;

create trigger force_close_lifecycle_receipt_failure
before insert on public.operation_receipts
for each row
when (new.operation_id = '97000000-0000-0000-0000-000000000021'::uuid)
execute function pg_temp.fail_case_lifecycle_receipt();

set local role authenticated;

select throws_ok(
    $$
    select public.close_learning_case(
        '97000000-0000-0000-0000-000000000021',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.rollback_close_case_id')::uuid,
        current_setting('xq.lifecycle.rollback_close_action_id')::uuid,
        5,
        1
    )
    $$,
    'P0001',
    'TEST_FORCED_CASE_LIFECYCLE_RECEIPT_FAILURE',
    'receipt failure aborts the entire close transaction'
);

reset role;

select is(
    (
        select state || ':' || version::text
        from public.learning_cases
        where id = current_setting('xq.lifecycle.rollback_close_case_id')::uuid
    ),
    'stable:5'::text,
    'failed close leaves Case stable with original version'
);

select is(
    (
        select status || ':' || version::text
        from public.learning_case_actions
        where id = current_setting('xq.lifecycle.rollback_close_action_id')::uuid
    ),
    'pending:1'::text,
    'failed close leaves primary Action pending with original version'
);

select is(
    (
        select count(*) from public.learning_case_events
        where operation_id = '97000000-0000-0000-0000-000000000021'::uuid
    ),
    0::bigint,
    'failed close leaves no half-written lifecycle event'
);

select lives_ok(
    $$
    select public.create_learning_case(
        '97000000-0000-0000-0000-000000000030',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '生命周期原子回滚：重开',
        '关闭前的最后一条行动',
        null,
        null
    )
    $$,
    'reopen rollback fixture is created'
);

reset role;

select set_config(
    'xq.lifecycle.rollback_reopen_case_id',
    (
        select result_payload ->> 'case_id'
        from public.operation_receipts
        where operation_id = '97000000-0000-0000-0000-000000000030'::uuid
    ),
    true
);
select set_config(
    'xq.lifecycle.rollback_reopen_action_id',
    (
        select result_payload ->> 'primary_action_id'
        from public.operation_receipts
        where operation_id = '97000000-0000-0000-0000-000000000030'::uuid
    ),
    true
);

update public.learning_case_actions
   set status = 'cancelled', version = 2
 where id = current_setting('xq.lifecycle.rollback_reopen_action_id')::uuid;
update public.learning_cases
   set state = 'closed', version = 6
 where id = current_setting('xq.lifecycle.rollback_reopen_case_id')::uuid;

create trigger force_reopen_lifecycle_receipt_failure
before insert on public.operation_receipts
for each row
when (new.operation_id = '97000000-0000-0000-0000-000000000031'::uuid)
execute function pg_temp.fail_case_lifecycle_receipt();

set local role authenticated;

select throws_ok(
    $$
    select public.reopen_learning_case(
        '97000000-0000-0000-0000-000000000031',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.lifecycle.rollback_reopen_case_id')::uuid,
        6,
        '这条行动不应在失败事务后留下',
        null
    )
    $$,
    'P0001',
    'TEST_FORCED_CASE_LIFECYCLE_RECEIPT_FAILURE',
    'receipt failure aborts the entire reopen transaction'
);

reset role;

select is(
    (
        select state || ':' || version::text
        from public.learning_cases
        where id = current_setting('xq.lifecycle.rollback_reopen_case_id')::uuid
    ),
    'closed:6'::text,
    'failed reopen leaves Case closed with original version'
);

select is(
    (
        select count(*) from public.learning_case_actions
        where case_id = current_setting('xq.lifecycle.rollback_reopen_case_id')::uuid
          and status = 'pending'
    ),
    0::bigint,
    'failed reopen leaves no half-written pending Action'
);

select is(
    (
        select count(*) from public.learning_case_events
        where operation_id = '97000000-0000-0000-0000-000000000031'::uuid
    ),
    0::bigint,
    'failed reopen leaves no half-written reopen event'
);

select * from finish();
rollback;
