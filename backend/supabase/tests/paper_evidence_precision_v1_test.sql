begin;
set local search_path = public, extensions;

select plan(14);

-- 1: get_paper_submission_v1 is SECURITY DEFINER and pins search_path = ""
select is(
    (
        select count(*)
        from pg_catalog.pg_proc as fn
        join pg_catalog.pg_namespace as ns on ns.oid = fn.pronamespace
        where ns.nspname = 'public'
          and fn.proname = 'get_paper_submission_v1'
          and fn.prosecdef
          and fn.proconfig @> array['search_path=""']::text[]
    ),
    1::bigint,
    'get_paper_submission_v1 is SECURITY DEFINER and pins search_path = ""'
);

-- 2: anon denied, authenticated granted
select ok(
    not pg_catalog.has_function_privilege(
        'anon',
        'public.get_paper_submission_v1(uuid,uuid)',
        'EXECUTE'
    )
    and pg_catalog.has_function_privilege(
        'authenticated',
        'public.get_paper_submission_v1(uuid,uuid)',
        'EXECUTE'
    ),
    'get_paper_submission_v1 grants EXECUTE to authenticated only'
);

-- Authenticate as Teacher A (active owner/teacher in Org 1, assigned to Student 1 Chinese)
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo"}',
    true
);

set local role authenticated;

-- 3: Create an archived paper dated 420 days ago (outside 365-day longitudinal window)
select lives_ok(
    $$
    select public.create_paper_submission_v1(
        '96000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        (current_date - 420),
        'exam',
        '去年期末存档试卷（超出365天窗口）',
        null,
        '[]'::jsonb
    )
    $$,
    'archived paper older than 365 days created'
);

reset role;

select set_config(
    'xq.precision.old_paper_id',
    (
        select result_payload ->> 'paper_submission_id'
        from public.operation_receipts
        where operation_id = '96000000-0000-0000-0000-000000000001'::uuid
    ),
    true
);

set local role authenticated;

-- 4: get_paper_submission_v1 finds >365-day-old paper and reports null existing_extraction_receipt before extraction
select is(
    (
        select
            (res ->> 'contract') || ':' ||
            (res ->> 'paper_submission_id') || ':' ||
            ((res -> 'existing_extraction_receipt') = 'null'::jsonb)::text
        from (
            select public.get_paper_submission_v1(
                current_setting('xq.precision.old_paper_id')::uuid,
                '96000000-0000-0000-0000-000000000002'::uuid
            ) as res
        ) as q
    ),
    'paper_submission_v1:' || current_setting('xq.precision.old_paper_id') || ':true',
    'get_paper_submission_v1 resolves paper older than 365 days with null pre-extraction receipt'
);

-- 5: Save ai_draft extraction for old paper
select lives_ok(
    $$
    select public.save_paper_extraction_v1(
        '96000000-0000-0000-0000-000000000002',
        current_setting('xq.precision.old_paper_id')::uuid,
        1,
        'ai_draft',
        'teacher_marked',
        null,
        12,
        1,
        88,
        100,
        60,
        '存档卷 AI 初筛完成。',
        '[]'::jsonb
    )
    $$,
    'save_paper_extraction_v1 commits extraction receipt for old paper'
);

-- 6: get_paper_submission_v1 returns committed existing_extraction_receipt for idempotent pre-check
select is(
    (
        public.get_paper_submission_v1(
            current_setting('xq.precision.old_paper_id')::uuid,
            '96000000-0000-0000-0000-000000000002'::uuid
        ) -> 'existing_extraction_receipt' ->> 'operation_id'
    ),
    '96000000-0000-0000-0000-000000000002',
    'get_paper_submission_v1 exposes committed extraction receipt for idempotent replay short-circuit'
);

-- 7-8: Create recent confirmed Paper A (90/100, 1 early confirmed mistake)
select lives_ok(
    $$
    select public.create_paper_submission_v1(
        '96000000-0000-0000-0000-000000000010',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        (current_date - 4),
        'unit_test',
        '已核对单元卷',
        null,
        '[]'::jsonb
    )
    $$,
    'recent confirmed paper A created'
);

reset role;

select set_config(
    'xq.precision.paper_a_id',
    (
        select result_payload ->> 'paper_submission_id'
        from public.operation_receipts
        where operation_id = '96000000-0000-0000-0000-000000000010'::uuid
    ),
    true
);

set local role authenticated;

select lives_ok(
    $$
    select public.save_paper_extraction_v1(
        '96000000-0000-0000-0000-000000000011',
        current_setting('xq.precision.paper_a_id')::uuid,
        1,
        'human_confirm',
        'teacher_marked',
        null,
        10,
        1,
        90,
        100,
        40,
        '老师确认 90 分。',
        jsonb_build_array(
            jsonb_build_object(
                'page_index', 1,
                'question_label', 'Q2',
                'topic_tags', jsonb_build_array('基础知识'),
                'error_root_cause', 'calculation_slip',
                'paper_section_position', 'early',
                'points_lost', 10,
                'corrected_on_paper', true,
                'summary', '基础题笔误。'
            )
        )
    )
    $$,
    'recent paper A human_confirmed at 90/100'
);

-- 9-10: Create recent UNCONFIRMED draft Paper B (teacher_marked 30/100, 1 late ai_draft mistake)
select lives_ok(
    $$
    select public.create_paper_submission_v1(
        '96000000-0000-0000-0000-000000000020',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        (current_date - 2),
        'quiz',
        '未核对草稿卷（模拟OCR误读分数）',
        null,
        '[]'::jsonb
    )
    $$,
    'recent draft paper B created'
);

reset role;

select set_config(
    'xq.precision.paper_b_id',
    (
        select result_payload ->> 'paper_submission_id'
        from public.operation_receipts
        where operation_id = '96000000-0000-0000-0000-000000000020'::uuid
    ),
    true
);

set local role authenticated;

select lives_ok(
    $$
    select public.save_paper_extraction_v1(
        '96000000-0000-0000-0000-000000000021',
        current_setting('xq.precision.paper_b_id')::uuid,
        1,
        'ai_draft',
        'teacher_marked',
        null,
        10,
        1,
        30,
        100,
        30,
        'AI 初筛草稿（待核对分数）。',
        jsonb_build_array(
            jsonb_build_object(
                'page_index', 1,
                'question_label', 'Q10',
                'topic_tags', jsonb_build_array('阅读概括'),
                'error_root_cause', 'time_pressure',
                'paper_section_position', 'late',
                'points_lost', 20,
                'corrected_on_paper', false,
                'summary', 'AI 疑似末尾大题失分。'
            )
        )
    )
    $$,
    'recent paper B saved as unconfirmed ai_draft with teacher_marked 30/100'
);

-- 11: Verify authoritative teacher_marked_avg_score_ratio strictly excludes unconfirmed draft (0.9000),
--     while draft_avg_score_ratio previews both (0.6000), confirmed_late_section_mistake_ratio = 0.0000,
--     late_section_mistake_ratio = 0.5000, and prior_window_days = 60 for 90-day window.
select is(
    (
        select
            (res -> 'summary' ->> 'teacher_marked_avg_score_ratio') || ':' ||
            (res -> 'summary' ->> 'draft_avg_score_ratio') || ':' ||
            (res -> 'summary' ->> 'confirmed_late_section_mistake_ratio') || ':' ||
            (res -> 'summary' ->> 'late_section_mistake_ratio') || ':' ||
            (res -> 'summary' ->> 'prior_window_days')
        from (
            select public.get_student_paper_longitudinal_v1(
                '20000000-0000-0000-0000-000000000001',
                '30000000-0000-0000-0000-000000000001',
                '40000000-0000-0000-0000-000000000001',
                90,
                30
            ) as res
        ) as q
    ),
    '0.9000:0.6000:0.0000:0.5000:60',
    'calibrated longitudinal summary separates human_confirmed averages and fatigue ratios from unconfirmed drafts'
);

-- 12: Verify prior_window_days is 0 when window_days <= 30
select is(
    (
        public.get_student_paper_longitudinal_v1(
            '20000000-0000-0000-0000-000000000001',
            '30000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000001',
            30,
            30
        ) -> 'summary' ->> 'prior_window_days'
    ),
    '0',
    'prior_window_days reports 0 for 30-day window so callers never confuse empty prior window with zero errors'
);

-- 13: Unassigned Teacher B cannot fetch single paper via get_paper_submission_v1
reset role;
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated","iss":"supabase-demo"}',
    true
);
set local role authenticated;

select throws_ok(
    $$
    select public.get_paper_submission_v1(
        current_setting('xq.precision.old_paper_id')::uuid,
        null
    )
    $$,
    'P0001',
    'XQ_TEACHING_CONTEXT_UNAVAILABLE',
    'unassigned org admin cannot read single paper via get_paper_submission_v1'
);

-- 14: Disabled Teacher C fails closed on get_paper_submission_v1
reset role;
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000003', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000003","role":"authenticated","iss":"supabase-demo"}',
    true
);
set local role authenticated;

select throws_ok(
    $$
    select public.get_paper_submission_v1(
        current_setting('xq.precision.old_paper_id')::uuid,
        null
    )
    $$,
    'P0001',
    'XQ_MEMBERSHIP_DISABLED',
    'disabled membership fails closed on get_paper_submission_v1'
);

reset role;

select * from finish();
rollback;
