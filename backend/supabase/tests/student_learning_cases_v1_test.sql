begin;
set local search_path = public, extensions;

select plan(24);

select ok(
    exists (
        select 1
          from pg_catalog.pg_proc p
          join pg_catalog.pg_namespace n on n.oid = p.pronamespace
         where n.nspname = 'public'
           and p.proname = 'get_student_learning_cases_v1'
    ),
    'Student Learning Cases v1 function exists'
);

select ok(
    (
        select p.prosecdef
          from pg_catalog.pg_proc p
          join pg_catalog.pg_namespace n on n.oid = p.pronamespace
         where n.nspname = 'public'
           and p.proname = 'get_student_learning_cases_v1'
         limit 1
    ),
    'Student Learning Cases v1 is SECURITY DEFINER'
);

select ok(
    not pg_catalog.has_function_privilege(
        'anon',
        'public.get_student_learning_cases_v1(uuid,uuid,uuid)',
        'EXECUTE'
    ),
    'anon cannot execute Student Learning Cases v1'
);

select ok(
    pg_catalog.has_function_privilege(
        'authenticated',
        'public.get_student_learning_cases_v1(uuid,uuid,uuid)',
        'EXECUTE'
    ),
    'authenticated may execute Student Learning Cases v1'
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
        '99000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '当前教师开放 Case',
        '继续检查限制条件',
        '2026-09-22'::date,
        null
    )
    $$,
    'current teacher open Case fixture is created through authoritative command'
);

reset role;

select set_config(
    'xq.case_history.open_case_id',
    (
        select result_payload ->> 'case_id'
          from public.operation_receipts
         where operation_id = '99000000-0000-0000-0000-000000000001'::uuid
    ),
    true
);

insert into public.student_teacher_assignments (
    id,
    organization_id,
    student_id,
    subject_profile_id,
    teacher_app_user_id,
    active
) values (
    '50000000-0000-0000-0000-000000000002',
    '20000000-0000-0000-0000-000000000001',
    '30000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000001',
    '10000000-0000-0000-0000-000000000002',
    false
);

insert into public.learning_cases (
    id,
    organization_id,
    student_id,
    subject_profile_id,
    owner_assignment_id,
    responsible_teacher_app_user_id,
    title,
    state,
    version,
    created_by_actor_app_user_id,
    created_at_server,
    updated_at_server
) values (
    '60000000-0000-0000-0000-000000000090',
    '20000000-0000-0000-0000-000000000001',
    '30000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000001',
    '50000000-0000-0000-0000-000000000002',
    '10000000-0000-0000-0000-000000000002',
    '历史已关闭 Case',
    'closed',
    6,
    '10000000-0000-0000-0000-000000000002',
    '2026-08-01T01:00:00Z',
    '2026-09-19T12:00:00Z'
);

insert into public.learning_case_actions (
    id,
    organization_id,
    student_id,
    subject_profile_id,
    case_id,
    assignee_assignment_id,
    assignee_teacher_app_user_id,
    action_role,
    status,
    action_text,
    due_on,
    version,
    created_by_actor_app_user_id,
    created_at_server,
    updated_at_server
) values (
    '61000000-0000-0000-0000-000000000090',
    '20000000-0000-0000-0000-000000000001',
    '30000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000001',
    '60000000-0000-0000-0000-000000000090',
    '50000000-0000-0000-0000-000000000002',
    '10000000-0000-0000-0000-000000000002',
    'primary',
    'cancelled',
    '旧责任人的历史行动',
    null,
    2,
    '10000000-0000-0000-0000-000000000002',
    '2026-08-01T01:00:00Z',
    '2026-09-19T12:00:00Z'
);

update public.learning_cases
   set updated_at_server = '2026-09-19T13:00:00Z'
 where id = current_setting('xq.case_history.open_case_id')::uuid;

set local role authenticated;

select lives_ok(
    $$
    select public.get_student_learning_cases_v1(
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    )
    $$,
    'current assigned teacher can read bounded Case history'
);

select set_config(
    'xq.case_history.response',
    public.get_student_learning_cases_v1(
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    )::text,
    true
);

reset role;

select is(
    current_setting('xq.case_history.response')::jsonb ->> 'contract',
    'student_learning_cases_v1'::text,
    'projection identifies its contract'
);

select is(
    current_setting('xq.case_history.response')::jsonb ->> 'actor_app_user_id',
    '10000000-0000-0000-0000-000000000001'::text,
    'projection resolves application-owned reader identity'
);

select is(
    current_setting('xq.case_history.response')::jsonb ->> 'assignment_id',
    '50000000-0000-0000-0000-000000000001'::text,
    'projection exposes current reader assignment'
);

select is(
    pg_catalog.jsonb_array_length(
        current_setting('xq.case_history.response')::jsonb -> 'cases'
    ),
    2,
    'projection contains both current and historical Case'
);

select is(
    current_setting('xq.case_history.response')::jsonb
        -> 'cases' -> 0 ->> 'case_id',
    current_setting('xq.case_history.open_case_id'),
    'Cases are ordered by authoritative updated_at_server descending'
);

select is(
    current_setting('xq.case_history.response')::jsonb
        -> 'cases' -> 0 ->> 'is_current_actor_responsibility',
    'true'::text,
    'current teacher Case is marked as current responsibility'
);

select is(
    current_setting('xq.case_history.response')::jsonb
        -> 'cases' -> 0 -> 'primary_action' ->> 'action_text',
    '继续检查限制条件'::text,
    'open Case exposes its exact pending primary Action'
);

select is(
    current_setting('xq.case_history.response')::jsonb
        -> 'cases' -> 1 ->> 'case_id',
    '60000000-0000-0000-0000-000000000090'::text,
    'history includes Case owned by retired earlier assignment'
);

select is(
    current_setting('xq.case_history.response')::jsonb
        -> 'cases' -> 1 ->> 'responsible_teacher_app_user_id',
    '10000000-0000-0000-0000-000000000002'::text,
    'historical responsibility is preserved'
);

select is(
    current_setting('xq.case_history.response')::jsonb
        -> 'cases' -> 1 ->> 'is_current_actor_responsibility',
    'false'::text,
    'historical Case is not mislabeled as current responsibility'
);

select ok(
    (
        current_setting('xq.case_history.response')::jsonb
            -> 'cases' -> 1 -> 'primary_action'
    ) = 'null'::jsonb,
    'closed Case has null current primary Action'
);

select is(
    current_setting('xq.case_history.response')::jsonb ->> 'has_more',
    'false'::text,
    'small history is not falsely marked truncated'
);

-- Break the open invariant and prove the projection fails closed.
update public.learning_case_actions
   set status = 'completed',
       completed_at_server = pg_catalog.clock_timestamp()
 where case_id = current_setting('xq.case_history.open_case_id')::uuid
   and action_role = 'primary'
   and status = 'pending';

set local role authenticated;

select throws_ok(
    $$
    select public.get_student_learning_cases_v1(
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    )
    $$,
    'P0001',
    'XQ_CASE_PRIMARY_ACTION_INVARIANT',
    'open Case without pending primary Action fails closed'
);

reset role;

update public.learning_case_actions
   set status = 'pending',
       completed_at_server = null
 where case_id = current_setting('xq.case_history.open_case_id')::uuid
   and action_role = 'primary';

-- Break the closed invariant and prove it also fails closed.
update public.learning_case_actions
   set status = 'pending'
 where id = '61000000-0000-0000-0000-000000000090'::uuid;

set local role authenticated;

select throws_ok(
    $$
    select public.get_student_learning_cases_v1(
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    )
    $$,
    'P0001',
    'XQ_CASE_PRIMARY_ACTION_INVARIANT',
    'closed Case with pending primary Action fails closed'
);

reset role;

update public.learning_case_actions
   set status = 'cancelled'
 where id = '61000000-0000-0000-0000-000000000090'::uuid;

-- A member without the current matching assignment must not read history.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated","iss":"supabase-demo"}',
    true
);

set local role authenticated;

select throws_ok(
    $$
    select public.get_student_learning_cases_v1(
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    )
    $$,
    'P0001',
    'XQ_TEACHING_CONTEXT_UNAVAILABLE',
    'organization member without active matching assignment cannot read Case history'
);

reset role;

-- Restore Teacher A and prove active-assignment revocation denies subsequent reads.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo"}',
    true
);

update public.student_teacher_assignments
   set active = false
 where id = '50000000-0000-0000-0000-000000000001'::uuid;

set local role authenticated;

select throws_ok(
    $$
    select public.get_student_learning_cases_v1(
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    )
    $$,
    'P0001',
    'XQ_TEACHING_CONTEXT_UNAVAILABLE',
    'revoked current assignment denies subsequent history read'
);

reset role;

update public.student_teacher_assignments
   set active = true
 where id = '50000000-0000-0000-0000-000000000001'::uuid;

-- Add enough closed historical Cases to prove the 50-row bound.
insert into public.learning_cases (
    id,
    organization_id,
    student_id,
    subject_profile_id,
    owner_assignment_id,
    responsible_teacher_app_user_id,
    title,
    state,
    version,
    created_by_actor_app_user_id,
    created_at_server,
    updated_at_server
)
select
    (
        '62000000-0000-0000-0000-' ||
        pg_catalog.lpad(series.n::text, 12, '0')
    )::uuid,
    '20000000-0000-0000-0000-000000000001'::uuid,
    '30000000-0000-0000-0000-000000000001'::uuid,
    '40000000-0000-0000-0000-000000000001'::uuid,
    '50000000-0000-0000-0000-000000000002'::uuid,
    '10000000-0000-0000-0000-000000000002'::uuid,
    '历史 Case ' || series.n,
    'closed',
    3,
    '10000000-0000-0000-0000-000000000002'::uuid,
    '2026-07-01T00:00:00Z'::timestamptz + (series.n || ' minutes')::interval,
    '2026-07-01T00:00:00Z'::timestamptz + (series.n || ' minutes')::interval
from pg_catalog.generate_series(1, 50) as series(n);

set local role authenticated;

select set_config(
    'xq.case_history.bounded',
    public.get_student_learning_cases_v1(
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    )::text,
    true
);

reset role;

select is(
    pg_catalog.jsonb_array_length(
        current_setting('xq.case_history.bounded')::jsonb -> 'cases'
    ),
    50,
    'Case history is bounded to 50 items'
);

select is(
    current_setting('xq.case_history.bounded')::jsonb ->> 'has_more',
    'true'::text,
    'Case history reports additional rows beyond the bound'
);

select * from finish();
rollback;
