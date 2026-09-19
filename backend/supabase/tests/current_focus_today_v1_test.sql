begin;
set local search_path = public, extensions;

select plan(32);

select has_column('public', 'organizations', 'time_zone', 'Organization stores an explicit business timezone');

select is(
    (select time_zone from public.organizations where id = '20000000-0000-0000-0000-000000000001'),
    'Asia/Shanghai'::text,
    'fictional Organization A uses explicit Asia/Shanghai business timezone'
);

select is(
    (select time_zone from public.organizations where id = '20000000-0000-0000-0000-000000000002'),
    'America/New_York'::text,
    'fictional Organization B uses explicit America/New_York business timezone'
);

select is(
    xq_internal.organization_business_date_v1(
        'Asia/Shanghai',
        '2026-09-19T01:00:00Z'::timestamptz
    ),
    '2026-09-19'::date,
    'business date helper interprets a fixed instant in Asia/Shanghai'
);

select is(
    xq_internal.organization_business_date_v1(
        'America/New_York',
        '2026-09-19T01:00:00Z'::timestamptz
    ),
    '2026-09-18'::date,
    'same fixed instant may belong to the previous business date in New York'
);

select throws_ok(
    $$
    insert into public.organizations (id, name, time_zone)
    values (
        '20000000-0000-0000-0000-000000000099',
        '虚构非法时区机构',
        'Mars/TrainingRoom'
    )
    $$,
    'P0001',
    'XQ_INVALID_ORGANIZATION_TIME_ZONE',
    'invalid organization timezone is rejected at the table boundary'
);

select ok(
    command.prosecdef,
    'StudentLearningFocus is an explicit SECURITY DEFINER projection boundary'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'get_student_learning_focus_v1';

select ok(
    command.proconfig @> array['search_path=""']::text[],
    'StudentLearningFocus pins an empty search_path'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'get_student_learning_focus_v1';

select ok(
    not pg_catalog.has_function_privilege('anon', command.oid, 'EXECUTE'),
    'anon cannot execute StudentLearningFocus'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'get_student_learning_focus_v1';

select ok(
    pg_catalog.has_function_privilege('authenticated', command.oid, 'EXECUTE'),
    'authenticated may execute StudentLearningFocus'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'get_student_learning_focus_v1';

select ok(
    command.prosecdef,
    'PersonalTodayActions is an explicit SECURITY DEFINER projection boundary'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'get_personal_today_actions_v1';

select ok(
    command.proconfig @> array['search_path=""']::text[],
    'PersonalTodayActions pins an empty search_path'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'get_personal_today_actions_v1';

select ok(
    not pg_catalog.has_function_privilege('anon', command.oid, 'EXECUTE'),
    'anon cannot execute PersonalTodayActions'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'get_personal_today_actions_v1';

select ok(
    pg_catalog.has_function_privilege('authenticated', command.oid, 'EXECUTE'),
    'authenticated may execute PersonalTodayActions'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'get_personal_today_actions_v1';

select set_config(
    'xq.test.org_a_today',
    xq_internal.organization_business_date_v1(
        'Asia/Shanghai',
        pg_catalog.statement_timestamp()
    )::text,
    true
);

select set_config(
    'xq.test.org_b_today',
    xq_internal.organization_business_date_v1(
        'America/New_York',
        pg_catalog.statement_timestamp()
    )::text,
    true
);

-- Give Teacher A a real current teaching assignment in fictional Org B so one
-- Personal Today response can prove per-organization business dates.
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
        '94000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '概括题压缩仍不稳定',
        '复核三道陌生材料概括题',
        current_setting('xq.test.org_a_today')::date - 1,
        null
    )
    $$,
    'Teacher A creates an overdue Case Action in Organization A'
);

select lives_ok(
    $$
    select public.create_learning_case(
        '94000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '作文论据选择需要继续验证',
        '今天检查一次陌生作文材料',
        current_setting('xq.test.org_a_today')::date,
        null
    )
    $$,
    'Teacher A creates a due-today Case Action in Organization A'
);

select lives_ok(
    $$
    select public.create_learning_case(
        '94000000-0000-0000-0000-000000000003',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '文言实词辨析需持续观察',
        '安排下一次课堂抽查',
        null,
        null
    )
    $$,
    'Teacher A creates an undated Case Action in Organization A'
);

select lives_ok(
    $$
    select public.create_learning_case(
        '94000000-0000-0000-0000-000000000004',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '阅读结构迁移仍需巩固',
        '后天再做一次结构迁移验证',
        current_setting('xq.test.org_a_today')::date + 2,
        null
    )
    $$,
    'Teacher A creates a future Case Action in Organization A'
);

select lives_ok(
    $$
    select public.create_learning_case(
        '94000000-0000-0000-0000-000000000005',
        '20000000-0000-0000-0000-000000000002',
        '30000000-0000-0000-0000-000000000002',
        '40000000-0000-0000-0000-000000000002',
        '50000000-0000-0000-0000-000000000002',
        '另一机构的当日教学关注',
        '按该机构本地日期完成一次检查',
        current_setting('xq.test.org_b_today')::date,
        null
    )
    $$,
    'Teacher A creates a due-today Case Action in Organization B'
);

reset role;

-- Make Current Focus ordering deterministic without changing business meaning.
update public.learning_cases
   set updated_at_server = case title
       when '概括题压缩仍不稳定' then '2026-09-19T01:00:00Z'::timestamptz
       when '作文论据选择需要继续验证' then '2026-09-19T02:00:00Z'::timestamptz
       when '文言实词辨析需持续观察' then '2026-09-19T03:00:00Z'::timestamptz
       when '阅读结构迁移仍需巩固' then '2026-09-19T04:00:00Z'::timestamptz
       else updated_at_server
   end
 where organization_id = '20000000-0000-0000-0000-000000000001'::uuid
   and student_id = '30000000-0000-0000-0000-000000000001'::uuid
   and title in (
       '概括题压缩仍不稳定',
       '作文论据选择需要继续验证',
       '文言实词辨析需持续观察',
       '阅读结构迁移仍需巩固'
   );

set local role authenticated;

select is(
    public.get_student_learning_focus_v1(
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    ) ->> 'contract',
    'student_learning_focus_v1'::text,
    'Student Learning Focus returns the versioned contract'
);

select is(
    pg_catalog.jsonb_array_length(
        public.get_student_learning_focus_v1(
            '20000000-0000-0000-0000-000000000001',
            '30000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000001'
        ) -> 'cases'
    ),
    3,
    'Student Learning Focus returns at most three current Cases'
);

select ok(
    (
        public.get_student_learning_focus_v1(
            '20000000-0000-0000-0000-000000000001',
            '30000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000001'
        ) ->> 'has_more'
    )::boolean,
    'Student Learning Focus reports additional open Cases'
);

select is(
    public.get_student_learning_focus_v1(
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    ) -> 'cases' -> 0 ->> 'title',
    '阅读结构迁移仍需巩固'::text,
    'Current Focus uses deterministic recent-active ordering, not invented priority'
);

select is(
    public.get_personal_today_actions_v1() ->> 'contract',
    'personal_today_actions_v1'::text,
    'Personal Today returns the versioned contract'
);

select is(
    pg_catalog.jsonb_array_length(
        public.get_personal_today_actions_v1() -> 'actions'
    ),
    5,
    'Personal Today contains exactly the five explicit pending primary Actions'
);

select is(
    public.get_personal_today_actions_v1()
        -> 'actions' -> 0 ->> 'due_bucket',
    'overdue'::text,
    'Personal Today sorts overdue Actions first'
);

select is(
    (
        select pg_catalog.count(*)::int
        from pg_catalog.jsonb_array_elements(
            public.get_personal_today_actions_v1() -> 'actions'
        ) as item
        where item ->> 'due_bucket' = 'today'
    ),
    2,
    'Personal Today classifies one today Action in each Organization timezone'
);

select is(
    (
        select item ->> 'organization_business_date'
        from pg_catalog.jsonb_array_elements(
            public.get_personal_today_actions_v1() -> 'actions'
        ) as item
        where item ->> 'organization_id' =
            '20000000-0000-0000-0000-000000000002'
        limit 1
    ),
    current_setting('xq.test.org_b_today')::text,
    'Today exposes Organization B business date instead of device-local date'
);

reset role;

-- Teacher B is made an organization admin to prove management authority alone
-- does not leak Teacher A's personal Actions into another user's Today.
update public.memberships
   set membership_role = 'admin'
 where organization_id = '20000000-0000-0000-0000-000000000001'::uuid
   and app_user_id = '10000000-0000-0000-0000-000000000002'::uuid;

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated","iss":"supabase-demo"}',
    true
);

set local role authenticated;

select is(
    pg_catalog.jsonb_array_length(
        public.get_personal_today_actions_v1() -> 'actions'
    ),
    0,
    'organization admin without teaching assignment gets no other teacher Actions in Personal Today'
);

select throws_ok(
    $$
    select public.get_student_learning_focus_v1(
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    )
    $$,
    'P0001',
    'XQ_TEACHING_CONTEXT_UNAVAILABLE',
    'organization admin without assignment cannot read another teacher personal Current Focus'
);

reset role;

-- Corrupt one privileged fixture into an open Case with zero pending primary
-- Actions. Both projections must fail closed rather than silently hiding it.
update public.learning_case_actions
   set status = 'completed',
       completed_at_server = pg_catalog.clock_timestamp()
 where action_text = '复核三道陌生材料概括题';

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo"}',
    true
);

set local role authenticated;

select throws_ok(
    $$
    select public.get_student_learning_focus_v1(
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    )
    $$,
    'P0001',
    'XQ_CASE_PRIMARY_ACTION_INVARIANT',
    'Current Focus fails closed when an open Case has no pending primary Action'
);

select throws_ok(
    $$
    select public.get_personal_today_actions_v1()
    $$,
    'P0001',
    'XQ_CASE_PRIMARY_ACTION_INVARIANT',
    'Personal Today fails closed when an open Case has no pending primary Action'
);

reset role;

select * from finish();
rollback;
