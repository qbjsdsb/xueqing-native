begin;
set local search_path = public, extensions;

select plan(34);

-- 1-4: Tables exist
select has_table('public', 'paper_submissions', 'paper_submissions table exists');
select has_table('public', 'paper_pages', 'paper_pages table exists');
select has_table('public', 'paper_mistake_items', 'paper_mistake_items table exists');
select has_table('public', 'study_plan_drafts', 'study_plan_drafts table exists');

-- 5: RLS enabled on all 4 tables
select is(
    (
        select count(*)
        from pg_catalog.pg_class as rel
        join pg_catalog.pg_namespace as ns on ns.oid = rel.relnamespace
        where ns.nspname = 'public'
          and rel.relname in (
              'paper_submissions',
              'paper_pages',
              'paper_mistake_items',
              'study_plan_drafts'
          )
          and rel.relrowsecurity
    ),
    4::bigint,
    'RLS is enabled on all 4 paper evidence and study plan tables'
);

-- 6: Zero direct table CRUD grants for anon/authenticated/PUBLIC
select is(
    (
        select count(*)
        from information_schema.role_table_grants
        where table_schema = 'public'
          and table_name in (
              'paper_submissions',
              'paper_pages',
              'paper_mistake_items',
              'study_plan_drafts'
          )
          and grantee in ('anon', 'authenticated', 'PUBLIC')
    ),
    0::bigint,
    'clients receive zero direct table grants on paper evidence and study plan tables'
);

-- 7-8: All 5 RPCs are SECURITY DEFINER and pin empty search_path
select is(
    (
        select count(*)
        from pg_catalog.pg_proc as fn
        join pg_catalog.pg_namespace as ns on ns.oid = fn.pronamespace
        where ns.nspname = 'public'
          and fn.proname in (
              'create_paper_submission_v1',
              'save_paper_extraction_v1',
              'get_student_paper_longitudinal_v1',
              'save_study_plan_draft_v1',
              'update_study_plan_draft_status_v1'
          )
          and fn.prosecdef
    ),
    5::bigint,
    'all 5 paper evidence RPCs are SECURITY DEFINER'
);

select is(
    (
        select count(*)
        from pg_catalog.pg_proc as fn
        join pg_catalog.pg_namespace as ns on ns.oid = fn.pronamespace
        where ns.nspname = 'public'
          and fn.proname in (
              'create_paper_submission_v1',
              'save_paper_extraction_v1',
              'get_student_paper_longitudinal_v1',
              'save_study_plan_draft_v1',
              'update_study_plan_draft_status_v1'
          )
          and fn.proconfig @> array['search_path=""']::text[]
    ),
    5::bigint,
    'all 5 paper evidence RPCs pin search_path = ""'
);

-- 9-10: anon denied, authenticated granted
select ok(
    not pg_catalog.has_function_privilege(
        'anon',
        'public.create_paper_submission_v1(uuid,uuid,uuid,uuid,uuid,date,text,text,uuid,jsonb)',
        'EXECUTE'
    )
    and not pg_catalog.has_function_privilege(
        'anon',
        'public.get_student_paper_longitudinal_v1(uuid,uuid,uuid,integer,integer)',
        'EXECUTE'
    ),
    'anon cannot invoke paper evidence RPCs'
);

select ok(
    pg_catalog.has_function_privilege(
        'authenticated',
        'public.create_paper_submission_v1(uuid,uuid,uuid,uuid,uuid,date,text,text,uuid,jsonb)',
        'EXECUTE'
    )
    and pg_catalog.has_function_privilege(
        'authenticated',
        'public.save_paper_extraction_v1(uuid,uuid,bigint,text,text,text,integer,integer,numeric,numeric,integer,text,jsonb)',
        'EXECUTE'
    )
    and pg_catalog.has_function_privilege(
        'authenticated',
        'public.get_student_paper_longitudinal_v1(uuid,uuid,uuid,integer,integer)',
        'EXECUTE'
    )
    and pg_catalog.has_function_privilege(
        'authenticated',
        'public.save_study_plan_draft_v1(uuid,uuid,uuid,uuid,uuid,text,text,jsonb,jsonb,integer,integer)',
        'EXECUTE'
    )
    and pg_catalog.has_function_privilege(
        'authenticated',
        'public.update_study_plan_draft_status_v1(uuid,uuid,bigint,text)',
        'EXECUTE'
    ),
    'authenticated may invoke all 5 paper evidence RPCs'
);

-- Authenticate as Teacher A (active owner/teacher in Org 1, assigned to Student 1 Chinese)
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo"}',
    true
);

-- Create fixture observation + 2 committed image pages for Paper 1
set local role authenticated;

select lives_ok(
    $$
    select public.create_observation(
        '95000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '试卷回归夹具：单元测验原卷两页拍照。',
        now(),
        '{"fixture":true,"source":"paper-evidence-pgtap"}'::jsonb
    )
    $$,
    'fixture Observation created for multi-page paper test'
);

reset role;

select set_config(
    'xq.paper.obs1_id',
    (
        select result_payload ->> 'observation_id'
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000001'::uuid
    ),
    true
);

insert into storage.objects (id, bucket_id, name, owner_id, metadata) values
    (
        '95200000-0000-0000-0000-000000000001'::uuid,
        'teaching-attachments-v1',
        xq_internal.observation_attachment_object_name_v1(
            '20000000-0000-0000-0000-000000000001'::uuid,
            '30000000-0000-0000-0000-000000000001'::uuid,
            '40000000-0000-0000-0000-000000000001'::uuid,
            current_setting('xq.paper.obs1_id')::uuid,
            '95100000-0000-0000-0000-000000000001'::uuid
        ),
        'a0000000-0000-0000-0000-000000000001',
        '{"size":2048,"mimetype":"image/jpeg"}'::jsonb
    ),
    (
        '95200000-0000-0000-0000-000000000002'::uuid,
        'teaching-attachments-v1',
        xq_internal.observation_attachment_object_name_v1(
            '20000000-0000-0000-0000-000000000001'::uuid,
            '30000000-0000-0000-0000-000000000001'::uuid,
            '40000000-0000-0000-0000-000000000001'::uuid,
            current_setting('xq.paper.obs1_id')::uuid,
            '95100000-0000-0000-0000-000000000002'::uuid
        ),
        'a0000000-0000-0000-0000-000000000001',
        '{"size":3072,"mimetype":"image/jpeg"}'::jsonb
    );

set local role authenticated;

select lives_ok(
    $$
    select public.commit_observation_attachment(
        '95000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.paper.obs1_id')::uuid,
        '95100000-0000-0000-0000-000000000001'
    )
    $$,
    'page 1 attachment committed'
);

select lives_ok(
    $$
    select public.commit_observation_attachment(
        '95000000-0000-0000-0000-000000000003',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.paper.obs1_id')::uuid,
        '95100000-0000-0000-0000-000000000002'
    )
    $$,
    'page 2 attachment committed'
);

-- 14: Create Paper 1 (40 days ago, prior window, unit_test, 2 pages with page 1 header redacted)
select is(
    (
        public.create_paper_submission_v1(
            '95000000-0000-0000-0000-000000000010',
            '20000000-0000-0000-0000-000000000001',
            '30000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000001',
            '50000000-0000-0000-0000-000000000001',
            (current_date - 40),
            'unit_test',
            '第一单元阅读理解测验',
            current_setting('xq.paper.obs1_id')::uuid,
            jsonb_build_array(
                jsonb_build_object(
                    'attachment_id', '95100000-0000-0000-0000-000000000001',
                    'page_index', 1,
                    'header_redacted', true
                ),
                jsonb_build_object(
                    'attachment_id', '95100000-0000-0000-0000-000000000002',
                    'page_index', 2,
                    'header_redacted', false
                )
            )
        ) ->> 'page_count'
    ),
    '2',
    'create_paper_submission_v1 stores multi-page unit_test paper'
);

reset role;

select set_config(
    'xq.paper.p1_id',
    (
        select result_payload ->> 'paper_submission_id'
        from public.operation_receipts
        where operation_id = '95000000-0000-0000-0000-000000000010'::uuid
    ),
    true
);

set local role authenticated;

-- 15: Idempotent retry with identical operation_id returns exact same paper_submission_id
select is(
    (
        public.create_paper_submission_v1(
            '95000000-0000-0000-0000-000000000010',
            '20000000-0000-0000-0000-000000000001',
            '30000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000001',
            '50000000-0000-0000-0000-000000000001',
            (current_date - 40),
            'unit_test',
            '第一单元阅读理解测验',
            current_setting('xq.paper.obs1_id')::uuid,
            jsonb_build_array(
                jsonb_build_object(
                    'attachment_id', '95100000-0000-0000-0000-000000000001',
                    'page_index', 1,
                    'header_redacted', true
                ),
                jsonb_build_object(
                    'attachment_id', '95100000-0000-0000-0000-000000000002',
                    'page_index', 2,
                    'header_redacted', false
                )
            )
        ) ->> 'paper_submission_id'
    ),
    current_setting('xq.paper.p1_id'),
    'create_paper_submission_v1 is idempotent on identical operation_id and payload'
);

-- 16: Reusing operation_id with different payload fails closed
select throws_ok(
    $$
    select public.create_paper_submission_v1(
        '95000000-0000-0000-0000-000000000010',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        (current_date - 40),
        'exam',
        '不同标题',
        null,
        '[]'::jsonb
    )
    $$,
    'P0001',
    'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD',
    'reusing create_paper_submission_v1 operation_id with different payload raises conflict'
);

-- 17: AI draft extraction on Paper 1 (version 1 -> 2, draft_ready)
select is(
    (
        public.save_paper_extraction_v1(
            '95000000-0000-0000-0000-000000000011',
            current_setting('xq.paper.p1_id')::uuid,
            1,
            'ai_draft',
            'teacher_marked',
            null,
            10,
            2,
            80,
            100,
            45,
            'AI 初筛：阅读因果题和末尾主观题各失分一次。',
            jsonb_build_array(
                jsonb_build_object(
                    'page_index', 1,
                    'question_label', 'Q4',
                    'topic_tags', jsonb_build_array('因果关系', '限制条件'),
                    'error_root_cause', 'misread_condition',
                    'paper_section_position', 'early',
                    'points_lost', 8,
                    'corrected_on_paper', false,
                    'summary', '未圈画题干中的否定限制词。'
                ),
                jsonb_build_object(
                    'page_index', 2,
                    'question_label', 'Q10',
                    'topic_tags', jsonb_build_array('因果关系'),
                    'error_root_cause', 'time_pressure',
                    'paper_section_position', 'late',
                    'points_lost', 12,
                    'corrected_on_paper', false,
                    'summary', '末尾大题作答仓促，因果链条缺中间环节。'
                )
            )
        ) ->> 'confirmation_state'
    ),
    'draft_ready',
    'save_paper_extraction_v1 ai_draft transitions paper to draft_ready'
);

-- 18: Optimistic concurrency version conflict fails closed
select throws_ok(
    $$
    select public.save_paper_extraction_v1(
        '95000000-0000-0000-0000-000000000012',
        current_setting('xq.paper.p1_id')::uuid,
        1,
        'human_confirm',
        'teacher_marked',
        null, 10, 2, 80, 100, 45, 'stale', '[]'::jsonb
    )
    $$,
    'P0001',
    'XQ_VERSION_CONFLICT',
    'save_paper_extraction_v1 rejects stale expected_version'
);

-- 19: Human confirms Paper 1 (version 2 -> 3, human_confirmed)
select is(
    (
        public.save_paper_extraction_v1(
            '95000000-0000-0000-0000-000000000013',
            current_setting('xq.paper.p1_id')::uuid,
            2,
            'human_confirm',
            'teacher_marked',
            null,
            10,
            2,
            80,
            100,
            45,
            '老师已复核：两处因果关系与审题问题确认无误。',
            jsonb_build_array(
                jsonb_build_object(
                    'page_index', 1,
                    'question_label', 'Q4',
                    'topic_tags', jsonb_build_array('因果关系', '限制条件'),
                    'error_root_cause', 'misread_condition',
                    'paper_section_position', 'early',
                    'points_lost', 8,
                    'corrected_on_paper', true,
                    'summary', '未圈画题干限制词。'
                ),
                jsonb_build_object(
                    'page_index', 2,
                    'question_label', 'Q10',
                    'topic_tags', jsonb_build_array('因果关系'),
                    'error_root_cause', 'time_pressure',
                    'paper_section_position', 'late',
                    'points_lost', 12,
                    'corrected_on_paper', false,
                    'summary', '末尾大题时间不足。'
                )
            )
        ) ->> 'confirmation_state'
    ),
    'human_confirmed',
    'save_paper_extraction_v1 human_confirm locks confirmed paper facts'
);

-- 20: AI draft extraction CANNOT overwrite an already human_confirmed paper!
select throws_ok(
    $$
    select public.save_paper_extraction_v1(
        '95000000-0000-0000-0000-000000000014',
        current_setting('xq.paper.p1_id')::uuid,
        3,
        'ai_draft',
        'teacher_marked',
        null, 10, 1, 90, 100, 45, '试图覆盖已确认记录', '[]'::jsonb
    )
    $$,
    'P0001',
    'XQ_PAPER_ALREADY_CONFIRMED',
    'ai_draft cannot overwrite a human_confirmed paper submission'
);

-- 21: Create Paper 2 (5 days ago, recent 30d window, quiz, teacher_marked 90/100) + confirm
select lives_ok(
    $$
    select public.create_paper_submission_v1(
        '95000000-0000-0000-0000-000000000020',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        (current_date - 5),
        'quiz',
        '本周随堂小测',
        null,
        '[]'::jsonb
    )
    $$,
    'Paper 2 (recent quiz) created'
);

-- 22: Create Paper 3 (2 days ago, recent 30d window, homework, ai_tentative_unmarked 50/100)
select lives_ok(
    $$
    select public.create_paper_submission_v1(
        '95000000-0000-0000-0000-000000000030',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        (current_date - 2),
        'homework',
        '未批改周末练习卷',
        null,
        '[]'::jsonb
    )
    $$,
    'Paper 3 (unmarked homework) created'
);

reset role;

select set_config(
    'xq.paper.p2_id',
    (select result_payload ->> 'paper_submission_id' from public.operation_receipts where operation_id = '95000000-0000-0000-0000-000000000020'::uuid),
    true
);
select set_config(
    'xq.paper.p3_id',
    (select result_payload ->> 'paper_submission_id' from public.operation_receipts where operation_id = '95000000-0000-0000-0000-000000000030'::uuid),
    true
);

set local role authenticated;

select lives_ok(
    $$
    select public.save_paper_extraction_v1(
        '95000000-0000-0000-0000-000000000021',
        current_setting('xq.paper.p2_id')::uuid,
        1,
        'human_confirm',
        'teacher_marked',
        null, 8, 1, 90, 100, 20, '进步明显，仅末尾题因时间紧张漏步。',
        jsonb_build_array(
            jsonb_build_object(
                'page_index', 1,
                'question_label', 'Q8',
                'topic_tags', jsonb_build_array('因果关系'),
                'error_root_cause', 'time_pressure',
                'paper_section_position', 'late',
                'points_lost', 10,
                'corrected_on_paper', false,
                'summary', '末尾归纳题未写完。'
            )
        )
    )
    $$,
    'Paper 2 confirmed with score 90/100 and late-section mistake'
);

select lives_ok(
    $$
    select public.save_paper_extraction_v1(
        '95000000-0000-0000-0000-000000000031',
        current_setting('xq.paper.p3_id')::uuid,
        1,
        'ai_draft',
        'ai_tentative_unmarked',
        null, 6, 1, 50, 100, null, 'AI 疑似错题标记（未人工核对，不得计入权威均分）。',
        jsonb_build_array(
            jsonb_build_object(
                'page_index', 1,
                'question_label', 'Q6',
                'topic_tags', jsonb_build_array('限制条件'),
                'error_root_cause', 'misread_condition',
                'paper_section_position', 'late',
                'points_lost', null,
                'corrected_on_paper', false,
                'summary', '疑似漏读题干前提。'
            )
        )
    )
    $$,
    'Paper 3 saved as ai_tentative_unmarked draft (score 50/100 must NOT pollute teacher average)'
);

-- 25: Verify longitudinal projection summary:
-- total_papers=3, confirmed_papers=2, draft_ready_papers=1,
-- teacher_marked_avg_score_ratio = avg(0.80, 0.90) = 0.8500 (strictly excludes Paper 3's 0.50!),
-- late_section_mistake_ratio = 3 late / 4 known = 0.7500!
select is(
    (
        select
            (res -> 'summary' ->> 'total_papers') || ':' ||
            (res -> 'summary' ->> 'confirmed_papers') || ':' ||
            (res -> 'summary' ->> 'draft_ready_papers') || ':' ||
            (res -> 'summary' ->> 'teacher_marked_avg_score_ratio') || ':' ||
            (res -> 'summary' ->> 'late_section_mistake_ratio')
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
    '3:2:1:0.8500:0.7500',
    'longitudinal summary computes exact counts, excludes ai_tentative_unmarked from avg score, and computes late-section fatigue ratio'
);

-- 26: Verify recurring_topics ranks '因果关系' #1 (3 occurrences across 2 papers)
select is(
    (
        select
            (res -> 'recurring_topics' -> 0 ->> 'topic_tag') || ':' ||
            (res -> 'recurring_topics' -> 0 ->> 'occurrence_count') || ':' ||
            (res -> 'recurring_topics' -> 0 ->> 'paper_count')
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
    '因果关系:3:2',
    'recurring_topics deterministically ranks top repeated topic and paper count'
);

-- 27: Verify root_cause_breakdown splits recent_30d vs prior_window accurately
select is(
    (
        select
            (elem ->> 'recent_30d_count') || ':' || (elem ->> 'prior_window_count')
        from (
            select jsonb_array_elements(
                public.get_student_paper_longitudinal_v1(
                    '20000000-0000-0000-0000-000000000001',
                    '30000000-0000-0000-0000-000000000001',
                    '40000000-0000-0000-0000-000000000001',
                    90,
                    30
                ) -> 'root_cause_breakdown'
            ) as elem
        ) as q
        where elem ->> 'error_root_cause' = 'time_pressure'
    ),
    '1:1',
    'root_cause_breakdown separates recent_30d_count and prior_window_count'
);

-- 28: Verify weekly_cadence returns multiple weekly buckets
select ok(
    jsonb_array_length(
        public.get_student_paper_longitudinal_v1(
            '20000000-0000-0000-0000-000000000001',
            '30000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000001',
            90,
            30
        ) -> 'weekly_cadence'
    ) >= 2,
    'weekly_cadence aggregates papers into chronological week buckets'
);

-- 29: Save Study Plan Draft (never creates Learning Cases silently)
select is(
    (
        public.save_study_plan_draft_v1(
            '95000000-0000-0000-0000-000000000040',
            '20000000-0000-0000-0000-000000000001',
            '30000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000001',
            '50000000-0000-0000-0000-000000000001',
            '近90天语文阅读专项复盘与下两周计划草案',
            '## 趋势观察\n后半段大题失分占比 75%，因果关系连续 2 份卷子出现。建议下两周减少整卷刷题，专攻限时因果拆解。',
            jsonb_build_array(
                jsonb_build_object(
                    'paper_submission_id', current_setting('xq.paper.p1_id'),
                    'question_label', 'Q10'
                )
            ),
            jsonb_build_array(
                jsonb_build_object(
                    'kind', 'create_learning_case',
                    'title', '阅读长文本因果关系链条与后半段控时',
                    'next_action', '本周安排 2 次 15 分钟限时段落因果拆解练习',
                    'suggested_due_on', (current_date + 3)::text
                )
            ),
            90,
            3
        ) ->> 'status'
    ),
    'draft',
    'save_study_plan_draft_v1 creates draft record'
);

reset role;

select set_config(
    'xq.paper.draft1_id',
    (select result_payload ->> 'draft_id' from public.operation_receipts where operation_id = '95000000-0000-0000-0000-000000000040'::uuid),
    true
);

-- 30: Prove saving study plan draft created ZERO silent learning_cases
select is(
    (
        select count(*)
        from public.learning_cases
        where student_id = '30000000-0000-0000-0000-000000000001'::uuid
          and title = '阅读长文本因果关系链条与后半段控时'
    ),
    0::bigint,
    'save_study_plan_draft_v1 never silently creates authoritative learning_cases'
);

set local role authenticated;

-- 31: Adopt study plan draft with optimistic concurrency
select is(
    (
        public.update_study_plan_draft_status_v1(
            '95000000-0000-0000-0000-000000000041',
            current_setting('xq.paper.draft1_id')::uuid,
            1,
            'partially_adopted'
        ) ->> 'status'
    ),
    'partially_adopted',
    'update_study_plan_draft_status_v1 transitions draft status with expected_version'
);

-- 32: Longitudinal projection includes the saved study plan draft
select is(
    (
        public.get_student_paper_longitudinal_v1(
            '20000000-0000-0000-0000-000000000001',
            '30000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000001',
            90,
            30
        ) -> 'recent_study_plan_drafts' -> 0 ->> 'status'
    ),
    'partially_adopted',
    'get_student_paper_longitudinal_v1 returns recent_study_plan_drafts'
);

-- 33: Unauthorized teacher (Teacher B, admin in Org 1 but NOT assigned to Student 1) is denied read & write
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
    select public.get_student_paper_longitudinal_v1(
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        90,
        30
    )
    $$,
    'P0001',
    'XQ_TEACHING_CONTEXT_UNAVAILABLE',
    'unassigned org admin cannot read another teacher student paper longitudinal data'
);

-- 34: Disabled teacher (Teacher C) fails closed on write
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
    select public.create_paper_submission_v1(
        '95000000-0000-0000-0000-000000000099',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_date,
        'quiz',
        '禁用账号写入测试',
        null,
        '[]'::jsonb
    )
    $$,
    'P0001',
    'XQ_MEMBERSHIP_DISABLED',
    'disabled membership cannot create paper submissions'
);

reset role;

select * from finish();
rollback;
