-- Phase 2: Exam Paper Evidence, Structured Visual Extraction Drafts,
-- Deterministic Longitudinal Projection, and Study Plan Drafts v1.
--
-- Strictly additive: leaves all Phase 1 tables, storage policies, and RPCs intact.
-- All tables enforce RLS with zero direct anon/authenticated CRUD; clients and
-- authorized Edge Functions/MCP tools interact exclusively through versioned
-- SECURITY DEFINER RPCs pinning search_path = ''.

create table public.paper_submissions (
    id uuid primary key,
    operation_id uuid not null unique,
    organization_id uuid not null,
    student_id uuid not null,
    subject_profile_id uuid not null,
    assignment_id uuid not null,
    actor_app_user_id uuid not null references public.app_users(id) on delete restrict,
    source_observation_id uuid references public.observations(id) on delete restrict,
    paper_date date not null
        check (paper_date >= date '2000-01-01'),
    paper_kind text not null
        check (paper_kind in ('homework', 'quiz', 'unit_test', 'exam')),
    title text
        check (title is null or pg_catalog.char_length(pg_catalog.btrim(title)) between 1 and 200),
    grading_source text not null default 'teacher_marked'
        check (grading_source in ('teacher_marked', 'ai_tentative_unmarked', 'human_verified')),
    confirmation_state text not null default 'awaiting_extraction'
        check (confirmation_state in ('awaiting_extraction', 'draft_ready', 'human_confirmed', 'extraction_failed')),
    question_count integer
        check (question_count is null or question_count between 1 and 500),
    mistake_count integer
        check (mistake_count is null or mistake_count between 0 and 500),
    score_earned numeric(6, 2)
        check (score_earned is null or score_earned >= 0),
    score_total numeric(6, 2)
        check (score_total is null or score_total > 0),
    duration_minutes integer
        check (duration_minutes is null or duration_minutes between 1 and 600),
    extraction_summary text
        check (
            extraction_summary is null
            or pg_catalog.char_length(pg_catalog.btrim(extraction_summary)) between 1 and 2000
        ),
    version bigint not null default 1 check (version >= 1),
    created_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    updated_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    confirmed_at_server timestamptz,
    check (score_earned is null or score_total is null or score_earned <= score_total),
    check (question_count is null or mistake_count is null or mistake_count <= question_count),
    foreign key (organization_id, student_id, subject_profile_id, assignment_id)
        references public.student_teacher_assignments(
            organization_id,
            student_id,
            subject_profile_id,
            id
        ) on delete restrict
);

create index paper_submissions_by_student_date
    on public.paper_submissions (
        organization_id,
        student_id,
        subject_profile_id,
        paper_date desc,
        created_at_server desc,
        id desc
    );

create table public.paper_pages (
    paper_submission_id uuid not null
        references public.paper_submissions(id) on delete restrict,
    page_index integer not null check (page_index between 1 and 50),
    attachment_id uuid not null unique
        references public.observation_attachments(id) on delete restrict,
    header_redacted boolean not null default false,
    created_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    primary key (paper_submission_id, page_index)
);

create table public.paper_mistake_items (
    id uuid primary key,
    paper_submission_id uuid not null
        references public.paper_submissions(id) on delete restrict,
    page_index integer not null check (page_index between 1 and 50),
    question_label text not null
        check (pg_catalog.char_length(pg_catalog.btrim(question_label)) between 1 and 60),
    topic_tags text[] not null default '{}'::text[]
        check (coalesce(pg_catalog.array_length(topic_tags, 1), 0) <= 10),
    error_root_cause text not null default 'unclassified'
        check (
            error_root_cause in (
                'concept_gap',
                'misread_condition',
                'strategy_stuck',
                'calculation_slip',
                'time_pressure',
                'unclassified'
            )
        ),
    paper_section_position text not null default 'unknown'
        check (paper_section_position in ('early', 'middle', 'late', 'unknown')),
    points_lost numeric(6, 2)
        check (points_lost is null or points_lost >= 0),
    corrected_on_paper boolean,
    summary text not null
        check (pg_catalog.char_length(pg_catalog.btrim(summary)) between 1 and 500),
    provenance text not null default 'ai_draft'
        check (provenance in ('ai_draft', 'human_confirmed')),
    linked_case_id uuid
        references public.learning_cases(id) on delete set null,
    created_at_server timestamptz not null default pg_catalog.clock_timestamp()
);

create index paper_mistake_items_by_submission
    on public.paper_mistake_items (paper_submission_id, page_index, id);

create table public.study_plan_drafts (
    id uuid primary key,
    operation_id uuid not null unique,
    organization_id uuid not null,
    student_id uuid not null,
    subject_profile_id uuid not null,
    assignment_id uuid not null,
    actor_app_user_id uuid not null references public.app_users(id) on delete restrict,
    title text not null
        check (pg_catalog.char_length(pg_catalog.btrim(title)) between 1 and 200),
    brief_markdown text not null
        check (pg_catalog.char_length(pg_catalog.btrim(brief_markdown)) between 1 and 12000),
    evidence_refs jsonb not null default '[]'::jsonb
        check (pg_catalog.jsonb_typeof(evidence_refs) = 'array'),
    proposed_actions jsonb not null default '[]'::jsonb
        check (pg_catalog.jsonb_typeof(proposed_actions) = 'array'),
    window_days integer not null default 30
        check (window_days between 7 and 365),
    paper_count_analyzed integer not null default 0
        check (paper_count_analyzed >= 0),
    status text not null default 'draft'
        check (status in ('draft', 'partially_adopted', 'fully_adopted', 'dismissed')),
    version bigint not null default 1 check (version >= 1),
    created_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    updated_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    foreign key (organization_id, student_id, subject_profile_id, assignment_id)
        references public.student_teacher_assignments(
            organization_id,
            student_id,
            subject_profile_id,
            id
        ) on delete restrict
);

create index study_plan_drafts_by_student_created
    on public.study_plan_drafts (
        organization_id,
        student_id,
        subject_profile_id,
        created_at_server desc,
        id desc
    );

alter table public.paper_submissions enable row level security;
alter table public.paper_pages enable row level security;
alter table public.paper_mistake_items enable row level security;
alter table public.study_plan_drafts enable row level security;

revoke all on table public.paper_submissions from public, anon, authenticated;
revoke all on table public.paper_pages from public, anon, authenticated;
revoke all on table public.paper_mistake_items from public, anon, authenticated;
revoke all on table public.study_plan_drafts from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- 1. create_paper_submission_v1
-- ---------------------------------------------------------------------------
create or replace function public.create_paper_submission_v1(
    p_operation_id uuid,
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid,
    p_assignment_id uuid,
    p_paper_date date,
    p_paper_kind text,
    p_title text default null,
    p_source_observation_id uuid default null,
    p_pages jsonb default '[]'::jsonb
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_provider_key text;
    v_issuer text;
    v_external_subject text;
    v_actor_id uuid;
    v_actor_enabled boolean;
    v_membership_status text;
    v_can_teach boolean;
    v_org_time_zone text;
    v_business_date date;
    v_trimmed_title text;
    v_request_payload jsonb;
    v_existing_receipt public.operation_receipts%rowtype;
    v_paper_id uuid;
    v_committed_at timestamptz;
    v_page_item jsonb;
    v_attachment_id uuid;
    v_page_index integer;
    v_header_redacted boolean;
    v_page_count integer := 0;
    v_result jsonb;
begin
    if p_operation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_ID_REQUIRED';
    end if;

    if p_organization_id is null
       or p_student_id is null
       or p_subject_profile_id is null
       or p_assignment_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;

    if p_paper_date is null or p_paper_date < date '2000-01-01' then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_PAPER_DATE';
    end if;

    if p_paper_kind is null
       or p_paper_kind not in ('homework', 'quiz', 'unit_test', 'exam') then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_PAPER_KIND';
    end if;

    if p_title is not null then
        v_trimmed_title := pg_catalog.btrim(p_title);
        if pg_catalog.char_length(v_trimmed_title) < 1
           or pg_catalog.char_length(v_trimmed_title) > 200 then
            raise exception using errcode = 'P0001', message = 'XQ_INVALID_PAPER_TITLE';
        end if;
    end if;

    if p_pages is null
       or pg_catalog.jsonb_typeof(p_pages) <> 'array'
       or pg_catalog.jsonb_array_length(p_pages) > 50 then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_PAPER_PAGES';
    end if;

    select identity.provider_key, identity.issuer, identity.external_subject
      into v_provider_key, v_issuer, v_external_subject
      from xq_internal.current_external_identity_v1() as identity;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_AUTH_REQUIRED';
    end if;

    select app_user.id, app_user.enabled
      into v_actor_id, v_actor_enabled
      from public.identity_links as identity_link
      join public.app_users as app_user
        on app_user.id = identity_link.app_user_id
     where identity_link.provider_key = v_provider_key
       and identity_link.issuer = v_issuer
       and identity_link.external_subject = v_external_subject
       and identity_link.active
     for share of identity_link, app_user;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_NOT_FOUND';
    end if;
    if not v_actor_enabled then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_DISABLED';
    end if;

    v_request_payload := pg_catalog.jsonb_build_object(
        'actor_app_user_id', v_actor_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'subject_profile_id', p_subject_profile_id,
        'assignment_id', p_assignment_id,
        'paper_date', p_paper_date,
        'paper_kind', p_paper_kind,
        'title', v_trimmed_title,
        'source_observation_id', p_source_observation_id,
        'pages', p_pages
    );

    perform pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended(p_operation_id::text, 0)
    );

    select receipt.*
      into v_existing_receipt
      from public.operation_receipts as receipt
     where receipt.operation_id = p_operation_id;

    if found then
        if v_existing_receipt.actor_app_user_id = v_actor_id
           and v_existing_receipt.command_name = 'create_paper_submission_v1'
           and v_existing_receipt.request_payload = v_request_payload then
            return v_existing_receipt.result_payload;
        end if;

        raise exception using
            errcode = 'P0001',
            message = 'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD';
    end if;

    select membership.status, membership.can_teach, organization.time_zone
      into v_membership_status, v_can_teach, v_org_time_zone
      from public.memberships as membership
      join public.organizations as organization
        on organization.id = membership.organization_id
     where membership.organization_id = p_organization_id
       and membership.app_user_id = v_actor_id
     for share of membership, organization;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_MEMBERSHIP_REQUIRED';
    end if;
    if v_membership_status <> 'active' then
        raise exception using errcode = 'P0001', message = 'XQ_MEMBERSHIP_DISABLED';
    end if;
    if not v_can_teach then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CAPABILITY_REQUIRED';
    end if;

    perform 1
      from public.student_teacher_assignments as assignment
      join public.students as student
        on student.organization_id = assignment.organization_id
       and student.id = assignment.student_id
      join public.student_subject_profiles as profile
        on profile.organization_id = assignment.organization_id
       and profile.student_id = assignment.student_id
       and profile.id = assignment.subject_profile_id
     where assignment.organization_id = p_organization_id
       and assignment.student_id = p_student_id
       and assignment.subject_profile_id = p_subject_profile_id
       and assignment.id = p_assignment_id
       and assignment.teacher_app_user_id = v_actor_id
       and assignment.active
       and student.active
       and profile.active
     for share of assignment, student, profile;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_UNAVAILABLE';
    end if;

    v_committed_at := pg_catalog.clock_timestamp();
    v_business_date := xq_internal.organization_business_date_v1(v_org_time_zone, v_committed_at);
    if p_paper_date > (v_business_date + 7) then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_PAPER_DATE';
    end if;

    if p_source_observation_id is not null then
        perform 1
          from public.observations as obs
         where obs.id = p_source_observation_id
           and obs.organization_id = p_organization_id
           and obs.student_id = p_student_id
           and obs.subject_profile_id = p_subject_profile_id;

        if not found then
            raise exception using errcode = 'P0001', message = 'XQ_PAPER_OBSERVATION_SCOPE_MISMATCH';
        end if;
    end if;

    v_paper_id := pg_catalog.gen_random_uuid();

    insert into public.paper_submissions (
        id,
        operation_id,
        organization_id,
        student_id,
        subject_profile_id,
        assignment_id,
        actor_app_user_id,
        source_observation_id,
        paper_date,
        paper_kind,
        title,
        grading_source,
        confirmation_state,
        version,
        created_at_server,
        updated_at_server
    ) values (
        v_paper_id,
        p_operation_id,
        p_organization_id,
        p_student_id,
        p_subject_profile_id,
        p_assignment_id,
        v_actor_id,
        p_source_observation_id,
        p_paper_date,
        p_paper_kind,
        v_trimmed_title,
        'teacher_marked',
        'awaiting_extraction',
        1,
        v_committed_at,
        v_committed_at
    );

    for v_page_item in
        select page_row.elem
          from pg_catalog.jsonb_array_elements(p_pages) as page_row(elem)
    loop
        if pg_catalog.jsonb_typeof(v_page_item) <> 'object' then
            raise exception using errcode = 'P0001', message = 'XQ_INVALID_PAPER_PAGES';
        end if;

        begin
            v_attachment_id := (v_page_item ->> 'attachment_id')::uuid;
            v_page_index := (v_page_item ->> 'page_index')::integer;
            v_header_redacted := coalesce((v_page_item ->> 'header_redacted')::boolean, false);
        exception
            when others then
                raise exception using errcode = 'P0001', message = 'XQ_INVALID_PAPER_PAGES';
        end;

        if v_attachment_id is null or v_page_index is null or v_page_index < 1 or v_page_index > 50 then
            raise exception using errcode = 'P0001', message = 'XQ_INVALID_PAPER_PAGES';
        end if;

        perform 1
          from public.observation_attachments as att
         where att.id = v_attachment_id
           and att.organization_id = p_organization_id
           and att.student_id = p_student_id
           and att.subject_profile_id = p_subject_profile_id
           and (
                p_source_observation_id is null
                or att.observation_id = p_source_observation_id
           );

        if not found then
            raise exception using errcode = 'P0001', message = 'XQ_PAPER_ATTACHMENT_SCOPE_MISMATCH';
        end if;

        begin
            insert into public.paper_pages (
                paper_submission_id,
                page_index,
                attachment_id,
                header_redacted,
                created_at_server
            ) values (
                v_paper_id,
                v_page_index,
                v_attachment_id,
                v_header_redacted,
                v_committed_at
            );
        exception
            when unique_violation then
                raise exception using errcode = 'P0001', message = 'XQ_INVALID_PAPER_PAGES';
        end;

        v_page_count := v_page_count + 1;
    end loop;

    v_result := pg_catalog.jsonb_build_object(
        'contract', 'create_paper_submission_v1',
        'operation_id', p_operation_id,
        'paper_submission_id', v_paper_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'subject_profile_id', p_subject_profile_id,
        'assignment_id', p_assignment_id,
        'paper_date', p_paper_date,
        'paper_kind', p_paper_kind,
        'title', v_trimmed_title,
        'confirmation_state', 'awaiting_extraction',
        'version', 1,
        'page_count', v_page_count,
        'server_committed_at', v_committed_at
    );

    insert into public.operation_receipts (
        operation_id,
        actor_app_user_id,
        command_name,
        request_payload,
        result_payload,
        committed_at
    ) values (
        p_operation_id,
        v_actor_id,
        'create_paper_submission_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.create_paper_submission_v1(
    uuid, uuid, uuid, uuid, uuid, date, text, text, uuid, jsonb
) from public, anon;
grant execute on function public.create_paper_submission_v1(
    uuid, uuid, uuid, uuid, uuid, date, text, text, uuid, jsonb
) to authenticated;

-- ---------------------------------------------------------------------------
-- 2. save_paper_extraction_v1 (ai_draft or human_confirm)
-- ---------------------------------------------------------------------------
create or replace function public.save_paper_extraction_v1(
    p_operation_id uuid,
    p_paper_submission_id uuid,
    p_expected_version bigint,
    p_mode text,
    p_grading_source text,
    p_title text default null,
    p_question_count integer default null,
    p_mistake_count integer default null,
    p_score_earned numeric default null,
    p_score_total numeric default null,
    p_duration_minutes integer default null,
    p_extraction_summary text default null,
    p_mistakes jsonb default '[]'::jsonb
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_provider_key text;
    v_issuer text;
    v_external_subject text;
    v_actor_id uuid;
    v_actor_enabled boolean;
    v_membership_status text;
    v_can_teach boolean;
    v_paper public.paper_submissions%rowtype;
    v_trimmed_title text;
    v_trimmed_summary text;
    v_effective_grading_source text;
    v_next_state text;
    v_item_provenance text;
    v_next_version bigint;
    v_committed_at timestamptz;
    v_confirmed_at timestamptz;
    v_request_payload jsonb;
    v_existing_receipt public.operation_receipts%rowtype;
    v_mistake_item jsonb;
    v_m_page_index integer;
    v_m_question_label text;
    v_m_topic_tags text[];
    v_m_root_cause text;
    v_m_position text;
    v_m_points_lost numeric(6, 2);
    v_m_corrected boolean;
    v_m_summary text;
    v_m_linked_case_id uuid;
    v_mistake_saved_count integer := 0;
    v_result jsonb;
begin
    if p_operation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_ID_REQUIRED';
    end if;

    if p_paper_submission_id is null or p_expected_version is null or p_expected_version < 1 then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;

    if p_mode is null or p_mode not in ('ai_draft', 'human_confirm') then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_EXTRACTION_MODE';
    end if;

    if p_grading_source is null
       or p_grading_source not in ('teacher_marked', 'ai_tentative_unmarked', 'human_verified') then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_GRADING_SOURCE';
    end if;

    if p_mode = 'ai_draft' and p_grading_source = 'human_verified' then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_GRADING_SOURCE';
    end if;

    if p_title is not null then
        v_trimmed_title := pg_catalog.btrim(p_title);
        if pg_catalog.char_length(v_trimmed_title) < 1
           or pg_catalog.char_length(v_trimmed_title) > 200 then
            raise exception using errcode = 'P0001', message = 'XQ_INVALID_PAPER_TITLE';
        end if;
    end if;

    if p_extraction_summary is not null then
        v_trimmed_summary := pg_catalog.btrim(p_extraction_summary);
        if pg_catalog.char_length(v_trimmed_summary) < 1
           or pg_catalog.char_length(v_trimmed_summary) > 2000 then
            raise exception using errcode = 'P0001', message = 'XQ_INVALID_PAPER_METRICS';
        end if;
    end if;

    if (p_question_count is not null and (p_question_count < 1 or p_question_count > 500))
       or (p_mistake_count is not null and (p_mistake_count < 0 or p_mistake_count > 500))
       or (p_question_count is not null and p_mistake_count is not null and p_mistake_count > p_question_count)
       or (p_score_earned is not null and p_score_earned < 0)
       or (p_score_total is not null and p_score_total <= 0)
       or (p_score_earned is not null and p_score_total is not null and p_score_earned > p_score_total)
       or (p_duration_minutes is not null and (p_duration_minutes < 1 or p_duration_minutes > 600)) then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_PAPER_METRICS';
    end if;

    if p_mistakes is null
       or pg_catalog.jsonb_typeof(p_mistakes) <> 'array'
       or pg_catalog.jsonb_array_length(p_mistakes) > 100 then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_MISTAKE_ITEMS';
    end if;

    select identity.provider_key, identity.issuer, identity.external_subject
      into v_provider_key, v_issuer, v_external_subject
      from xq_internal.current_external_identity_v1() as identity;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_AUTH_REQUIRED';
    end if;

    select app_user.id, app_user.enabled
      into v_actor_id, v_actor_enabled
      from public.identity_links as identity_link
      join public.app_users as app_user
        on app_user.id = identity_link.app_user_id
     where identity_link.provider_key = v_provider_key
       and identity_link.issuer = v_issuer
       and identity_link.external_subject = v_external_subject
       and identity_link.active
     for share of identity_link, app_user;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_NOT_FOUND';
    end if;
    if not v_actor_enabled then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_DISABLED';
    end if;

    v_request_payload := pg_catalog.jsonb_build_object(
        'actor_app_user_id', v_actor_id,
        'paper_submission_id', p_paper_submission_id,
        'expected_version', p_expected_version,
        'mode', p_mode,
        'grading_source', p_grading_source,
        'title', v_trimmed_title,
        'question_count', p_question_count,
        'mistake_count', p_mistake_count,
        'score_earned', p_score_earned,
        'score_total', p_score_total,
        'duration_minutes', p_duration_minutes,
        'extraction_summary', v_trimmed_summary,
        'mistakes', p_mistakes
    );

    perform pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended(p_operation_id::text, 0)
    );

    select receipt.*
      into v_existing_receipt
      from public.operation_receipts as receipt
     where receipt.operation_id = p_operation_id;

    if found then
        if v_existing_receipt.actor_app_user_id = v_actor_id
           and v_existing_receipt.command_name = 'save_paper_extraction_v1'
           and v_existing_receipt.request_payload = v_request_payload then
            return v_existing_receipt.result_payload;
        end if;

        raise exception using
            errcode = 'P0001',
            message = 'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD';
    end if;

    select paper.*
      into v_paper
      from public.paper_submissions as paper
     where paper.id = p_paper_submission_id
     for update;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_PAPER_NOT_FOUND';
    end if;

    select membership.status, membership.can_teach
      into v_membership_status, v_can_teach
      from public.memberships as membership
     where membership.organization_id = v_paper.organization_id
       and membership.app_user_id = v_actor_id
     for share;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_MEMBERSHIP_REQUIRED';
    end if;
    if v_membership_status <> 'active' then
        raise exception using errcode = 'P0001', message = 'XQ_MEMBERSHIP_DISABLED';
    end if;
    if not v_can_teach then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CAPABILITY_REQUIRED';
    end if;

    perform 1
      from public.student_teacher_assignments as assignment
      join public.students as student
        on student.organization_id = assignment.organization_id
       and student.id = assignment.student_id
      join public.student_subject_profiles as profile
        on profile.organization_id = assignment.organization_id
       and profile.student_id = assignment.student_id
       and profile.id = assignment.subject_profile_id
     where assignment.organization_id = v_paper.organization_id
       and assignment.student_id = v_paper.student_id
       and assignment.subject_profile_id = v_paper.subject_profile_id
       and assignment.teacher_app_user_id = v_actor_id
       and assignment.active
       and student.active
       and profile.active
     for share of assignment, student, profile;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_UNAVAILABLE';
    end if;

    if v_paper.version <> p_expected_version then
        raise exception using errcode = 'P0001', message = 'XQ_VERSION_CONFLICT';
    end if;

    -- Safety boundary: AI draft extraction must NEVER overwrite a human-confirmed paper.
    if p_mode = 'ai_draft' and v_paper.confirmation_state = 'human_confirmed' then
        raise exception using errcode = 'P0001', message = 'XQ_PAPER_ALREADY_CONFIRMED';
    end if;

    v_committed_at := pg_catalog.clock_timestamp();
    v_next_version := v_paper.version + 1;

    if p_mode = 'human_confirm' then
        v_next_state := 'human_confirmed';
        v_item_provenance := 'human_confirmed';
        v_confirmed_at := v_committed_at;
        v_effective_grading_source :=
            case
                when p_grading_source = 'ai_tentative_unmarked' then 'human_verified'
                else p_grading_source
            end;
    else
        v_next_state := 'draft_ready';
        v_item_provenance := 'ai_draft';
        v_confirmed_at := null;
        v_effective_grading_source := p_grading_source;
    end if;

    update public.paper_submissions as paper
       set title = coalesce(v_trimmed_title, paper.title),
           grading_source = v_effective_grading_source,
           confirmation_state = v_next_state,
           question_count = p_question_count,
           mistake_count = p_mistake_count,
           score_earned = p_score_earned,
           score_total = p_score_total,
           duration_minutes = p_duration_minutes,
           extraction_summary = coalesce(v_trimmed_summary, paper.extraction_summary),
           version = v_next_version,
           updated_at_server = v_committed_at,
           confirmed_at_server = coalesce(v_confirmed_at, paper.confirmed_at_server)
     where paper.id = v_paper.id;

    delete from public.paper_mistake_items as mistake
     where mistake.paper_submission_id = v_paper.id;

    for v_mistake_item in
        select item_row.elem
          from pg_catalog.jsonb_array_elements(p_mistakes) as item_row(elem)
    loop
        if pg_catalog.jsonb_typeof(v_mistake_item) <> 'object' then
            raise exception using errcode = 'P0001', message = 'XQ_INVALID_MISTAKE_ITEMS';
        end if;

        begin
            v_m_page_index := coalesce((v_mistake_item ->> 'page_index')::integer, 1);
            v_m_question_label := pg_catalog.btrim(coalesce(v_mistake_item ->> 'question_label', ''));
            v_m_root_cause := coalesce(v_mistake_item ->> 'error_root_cause', 'unclassified');
            v_m_position := coalesce(v_mistake_item ->> 'paper_section_position', 'unknown');
            v_m_points_lost := (v_mistake_item ->> 'points_lost')::numeric(6, 2);
            v_m_corrected := (v_mistake_item ->> 'corrected_on_paper')::boolean;
            v_m_summary := pg_catalog.btrim(coalesce(v_mistake_item ->> 'summary', ''));
            v_m_linked_case_id := (v_mistake_item ->> 'linked_case_id')::uuid;
        exception
            when others then
                raise exception using errcode = 'P0001', message = 'XQ_INVALID_MISTAKE_ITEMS';
        end;

        if v_m_page_index < 1 or v_m_page_index > 50
           or pg_catalog.char_length(v_m_question_label) < 1
           or pg_catalog.char_length(v_m_question_label) > 60
           or pg_catalog.char_length(v_m_summary) < 1
           or pg_catalog.char_length(v_m_summary) > 500
           or v_m_root_cause not in (
                'concept_gap',
                'misread_condition',
                'strategy_stuck',
                'calculation_slip',
                'time_pressure',
                'unclassified'
           )
           or v_m_position not in ('early', 'middle', 'late', 'unknown')
           or (v_m_points_lost is not null and v_m_points_lost < 0) then
            raise exception using errcode = 'P0001', message = 'XQ_INVALID_MISTAKE_ITEMS';
        end if;

        if v_mistake_item ? 'topic_tags' then
            if pg_catalog.jsonb_typeof(v_mistake_item -> 'topic_tags') <> 'array' then
                raise exception using errcode = 'P0001', message = 'XQ_INVALID_MISTAKE_ITEMS';
            end if;

            select coalesce(
                pg_catalog.array_agg(pg_catalog.btrim(tag_row.tag)),
                '{}'::text[]
            )
              into v_m_topic_tags
              from pg_catalog.jsonb_array_elements_text(v_mistake_item -> 'topic_tags') as tag_row(tag)
             where pg_catalog.char_length(pg_catalog.btrim(tag_row.tag)) between 1 and 60;

            if coalesce(pg_catalog.array_length(v_m_topic_tags, 1), 0) > 10 then
                raise exception using errcode = 'P0001', message = 'XQ_INVALID_MISTAKE_ITEMS';
            end if;
        else
            v_m_topic_tags := '{}'::text[];
        end if;

        if v_m_linked_case_id is not null then
            perform 1
              from public.learning_cases as lc
             where lc.id = v_m_linked_case_id
               and lc.organization_id = v_paper.organization_id
               and lc.student_id = v_paper.student_id
               and lc.subject_profile_id = v_paper.subject_profile_id;

            if not found then
                raise exception using errcode = 'P0001', message = 'XQ_LINKED_CASE_SCOPE_MISMATCH';
            end if;
        end if;

        insert into public.paper_mistake_items (
            id,
            paper_submission_id,
            page_index,
            question_label,
            topic_tags,
            error_root_cause,
            paper_section_position,
            points_lost,
            corrected_on_paper,
            summary,
            provenance,
            linked_case_id,
            created_at_server
        ) values (
            pg_catalog.gen_random_uuid(),
            v_paper.id,
            v_m_page_index,
            v_m_question_label,
            v_m_topic_tags,
            v_m_root_cause,
            v_m_position,
            v_m_points_lost,
            v_m_corrected,
            v_m_summary,
            v_item_provenance,
            v_m_linked_case_id,
            v_committed_at
        );

        v_mistake_saved_count := v_mistake_saved_count + 1;
    end loop;

    v_result := pg_catalog.jsonb_build_object(
        'contract', 'save_paper_extraction_v1',
        'operation_id', p_operation_id,
        'paper_submission_id', v_paper.id,
        'confirmation_state', v_next_state,
        'grading_source', v_effective_grading_source,
        'version', v_next_version,
        'mistake_items_saved', v_mistake_saved_count,
        'server_committed_at', v_committed_at
    );

    insert into public.operation_receipts (
        operation_id,
        actor_app_user_id,
        command_name,
        request_payload,
        result_payload,
        committed_at
    ) values (
        p_operation_id,
        v_actor_id,
        'save_paper_extraction_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.save_paper_extraction_v1(
    uuid, uuid, bigint, text, text, text, integer, integer, numeric, numeric, integer, text, jsonb
) from public, anon;
grant execute on function public.save_paper_extraction_v1(
    uuid, uuid, bigint, text, text, text, integer, integer, numeric, numeric, integer, text, jsonb
) to authenticated;

-- ---------------------------------------------------------------------------
-- 3. get_student_paper_longitudinal_v1 (deterministic trend & cadence engine)
-- ---------------------------------------------------------------------------
create or replace function public.get_student_paper_longitudinal_v1(
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid,
    p_window_days integer default 90,
    p_limit integer default 30
)
returns jsonb
language plpgsql
stable
security definer
set search_path = ''
as $$
declare
    v_provider_key text;
    v_issuer text;
    v_external_subject text;
    v_actor_id uuid;
    v_actor_enabled boolean;
    v_context record;
    v_generated_at timestamptz;
    v_business_date date;
    v_window_days integer;
    v_limit integer;
    v_window_start date;
    v_recent_30d_start date;
    v_summary jsonb;
    v_weekly_cadence jsonb;
    v_root_cause_breakdown jsonb;
    v_recurring_topics jsonb;
    v_papers jsonb;
    v_drafts jsonb;
begin
    if p_organization_id is null
       or p_student_id is null
       or p_subject_profile_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;

    v_window_days := greatest(7, least(coalesce(p_window_days, 90), 365));
    v_limit := greatest(1, least(coalesce(p_limit, 30), 50));

    select identity.provider_key, identity.issuer, identity.external_subject
      into v_provider_key, v_issuer, v_external_subject
      from xq_internal.current_external_identity_v1() as identity;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_AUTH_REQUIRED';
    end if;

    select app_user.id, app_user.enabled
      into v_actor_id, v_actor_enabled
      from public.identity_links as identity_link
      join public.app_users as app_user
        on app_user.id = identity_link.app_user_id
     where identity_link.provider_key = v_provider_key
       and identity_link.issuer = v_issuer
       and identity_link.external_subject = v_external_subject
       and identity_link.active;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_NOT_FOUND';
    end if;
    if not v_actor_enabled then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_DISABLED';
    end if;

    select
        organization.name as organization_name,
        organization.time_zone,
        student.display_name as student_display_name,
        profile.subject_key,
        assignment.id as assignment_id
      into v_context
      from public.student_teacher_assignments as assignment
      join public.memberships as membership
        on membership.organization_id = assignment.organization_id
       and membership.app_user_id = assignment.teacher_app_user_id
      join public.organizations as organization
        on organization.id = assignment.organization_id
      join public.students as student
        on student.organization_id = assignment.organization_id
       and student.id = assignment.student_id
      join public.student_subject_profiles as profile
        on profile.organization_id = assignment.organization_id
       and profile.student_id = assignment.student_id
       and profile.id = assignment.subject_profile_id
     where assignment.organization_id = p_organization_id
       and assignment.student_id = p_student_id
       and assignment.subject_profile_id = p_subject_profile_id
       and assignment.teacher_app_user_id = v_actor_id
       and assignment.active
       and membership.status = 'active'
       and membership.can_teach
       and student.active
       and profile.active;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_UNAVAILABLE';
    end if;

    v_generated_at := pg_catalog.statement_timestamp();
    v_business_date := xq_internal.organization_business_date_v1(v_context.time_zone, v_generated_at);
    v_window_start := v_business_date - (v_window_days - 1);
    v_recent_30d_start := v_business_date - 29;

    -- 1. Summary KPIs (authoritative score average strictly excludes ai_tentative_unmarked)
    with scoped_papers as (
        select paper.*
          from public.paper_submissions as paper
         where paper.organization_id = p_organization_id
           and paper.student_id = p_student_id
           and paper.subject_profile_id = p_subject_profile_id
           and paper.paper_date >= v_window_start
    ), scoped_mistakes as (
        select mistake.*
          from public.paper_mistake_items as mistake
          join scoped_papers as sp
            on sp.id = mistake.paper_submission_id
    )
    select pg_catalog.jsonb_build_object(
        'total_papers', (select pg_catalog.count(*) from scoped_papers),
        'confirmed_papers', (
            select pg_catalog.count(*)
              from scoped_papers as sp
             where sp.confirmation_state = 'human_confirmed'
        ),
        'draft_ready_papers', (
            select pg_catalog.count(*)
              from scoped_papers as sp
             where sp.confirmation_state = 'draft_ready'
        ),
        'awaiting_extraction_papers', (
            select pg_catalog.count(*)
              from scoped_papers as sp
             where sp.confirmation_state = 'awaiting_extraction'
        ),
        'total_mistakes_confirmed', (
            select pg_catalog.count(*)
              from scoped_mistakes as sm
             where sm.provenance = 'human_confirmed'
        ),
        'total_mistakes_all', (
            select pg_catalog.count(*)
              from scoped_mistakes
        ),
        'teacher_marked_avg_score_ratio', (
            select pg_catalog.round(
                pg_catalog.avg((sp.score_earned / sp.score_total)::numeric),
                4
            )
              from scoped_papers as sp
             where sp.grading_source in ('teacher_marked', 'human_verified')
               and sp.score_earned is not null
               and sp.score_total is not null
               and sp.score_total > 0
        ),
        'late_section_mistake_ratio', (
            select case
                when pg_catalog.count(*) filter (where sm.paper_section_position <> 'unknown') = 0 then null
                else pg_catalog.round(
                    (
                        (pg_catalog.count(*) filter (where sm.paper_section_position = 'late'))::numeric
                        / (pg_catalog.count(*) filter (where sm.paper_section_position <> 'unknown'))::numeric
                    ),
                    4
                )
            end
              from scoped_mistakes as sm
        )
    )
      into v_summary;

    -- 2. Weekly cadence & volume curve
    with scoped_papers as (
        select
            pg_catalog.date_trunc('week', paper.paper_date::timestamp)::date as week_start,
            paper.id,
            paper.paper_kind,
            paper.confirmation_state,
            coalesce(paper.mistake_count, 0) as mistake_count
          from public.paper_submissions as paper
         where paper.organization_id = p_organization_id
           and paper.student_id = p_student_id
           and paper.subject_profile_id = p_subject_profile_id
           and paper.paper_date >= v_window_start
    ), grouped as (
        select
            sp.week_start,
            pg_catalog.count(*) as paper_count,
            pg_catalog.count(*) filter (where sp.confirmation_state = 'human_confirmed') as confirmed_count,
            pg_catalog.sum(sp.mistake_count) as mistake_count,
            pg_catalog.count(*) filter (where sp.paper_kind = 'homework') as homework_count,
            pg_catalog.count(*) filter (where sp.paper_kind = 'quiz') as quiz_count,
            pg_catalog.count(*) filter (where sp.paper_kind = 'unit_test') as unit_test_count,
            pg_catalog.count(*) filter (where sp.paper_kind = 'exam') as exam_count
          from scoped_papers as sp
         group by sp.week_start
    )
    select coalesce(
        pg_catalog.jsonb_agg(
            pg_catalog.jsonb_build_object(
                'week_start', g.week_start,
                'paper_count', g.paper_count,
                'confirmed_count', g.confirmed_count,
                'mistake_count', g.mistake_count,
                'homework_count', g.homework_count,
                'quiz_count', g.quiz_count,
                'unit_test_count', g.unit_test_count,
                'exam_count', g.exam_count
            )
            order by g.week_start asc
        ),
        '[]'::jsonb
    )
      into v_weekly_cadence
      from grouped as g;

    -- 3. Root-cause distribution: recent 30d vs prior window
    with scoped_mistakes as (
        select
            mistake.error_root_cause,
            paper.paper_date
          from public.paper_mistake_items as mistake
          join public.paper_submissions as paper
            on paper.id = mistake.paper_submission_id
         where paper.organization_id = p_organization_id
           and paper.student_id = p_student_id
           and paper.subject_profile_id = p_subject_profile_id
           and paper.paper_date >= v_window_start
    ), grouped_rc as (
        select
            sm.error_root_cause,
            pg_catalog.count(*) filter (where sm.paper_date >= v_recent_30d_start) as recent_30d_count,
            pg_catalog.count(*) filter (where sm.paper_date < v_recent_30d_start) as prior_window_count,
            pg_catalog.count(*) as total_count
          from scoped_mistakes as sm
         group by sm.error_root_cause
    )
    select coalesce(
        pg_catalog.jsonb_agg(
            pg_catalog.jsonb_build_object(
                'error_root_cause', grc.error_root_cause,
                'recent_30d_count', grc.recent_30d_count,
                'prior_window_count', grc.prior_window_count,
                'total_count', grc.total_count
            )
            order by grc.total_count desc, grc.error_root_cause asc
        ),
        '[]'::jsonb
    )
      into v_root_cause_breakdown
      from grouped_rc as grc;

    -- 4. Recurring topics leaderboard
    with exploded_tags as (
        select
            tag_row.tag as topic_tag,
            paper.id as paper_id,
            paper.paper_date,
            mistake.linked_case_id
          from public.paper_mistake_items as mistake
          join public.paper_submissions as paper
            on paper.id = mistake.paper_submission_id
          cross join lateral pg_catalog.unnest(mistake.topic_tags) as tag_row(tag)
         where paper.organization_id = p_organization_id
           and paper.student_id = p_student_id
           and paper.subject_profile_id = p_subject_profile_id
           and paper.paper_date >= v_window_start
    ), topic_stats as (
        select
            et.topic_tag,
            pg_catalog.count(*) as occurrence_count,
            pg_catalog.count(distinct et.paper_id) as paper_count,
            pg_catalog.min(et.paper_date) as first_seen_date,
            pg_catalog.max(et.paper_date) as last_seen_date,
            coalesce(
                pg_catalog.jsonb_agg(distinct et.linked_case_id)
                    filter (where et.linked_case_id is not null),
                '[]'::jsonb
            ) as linked_case_ids
          from exploded_tags as et
         group by et.topic_tag
         order by pg_catalog.count(*) desc, pg_catalog.max(et.paper_date) desc, et.topic_tag asc
         limit 20
    )
    select coalesce(
        pg_catalog.jsonb_agg(
            pg_catalog.jsonb_build_object(
                'topic_tag', ts.topic_tag,
                'occurrence_count', ts.occurrence_count,
                'paper_count', ts.paper_count,
                'first_seen_date', ts.first_seen_date,
                'last_seen_date', ts.last_seen_date,
                'linked_case_ids', ts.linked_case_ids
            )
            order by ts.occurrence_count desc, ts.last_seen_date desc, ts.topic_tag asc
        ),
        '[]'::jsonb
    )
      into v_recurring_topics
      from topic_stats as ts;

    -- 5. Bounded recent papers with ordered pages + structured mistakes
    with bounded_papers as (
        select paper.*
          from public.paper_submissions as paper
         where paper.organization_id = p_organization_id
           and paper.student_id = p_student_id
           and paper.subject_profile_id = p_subject_profile_id
           and paper.paper_date >= v_window_start
         order by paper.paper_date desc, paper.created_at_server desc, paper.id desc
         limit v_limit
    )
    select coalesce(
        pg_catalog.jsonb_agg(
            pg_catalog.jsonb_build_object(
                'paper_submission_id', bp.id,
                'source_observation_id', bp.source_observation_id,
                'paper_date', bp.paper_date,
                'paper_kind', bp.paper_kind,
                'title', bp.title,
                'grading_source', bp.grading_source,
                'confirmation_state', bp.confirmation_state,
                'question_count', bp.question_count,
                'mistake_count', bp.mistake_count,
                'score_earned', bp.score_earned,
                'score_total', bp.score_total,
                'duration_minutes', bp.duration_minutes,
                'extraction_summary', bp.extraction_summary,
                'version', bp.version,
                'created_at_server', bp.created_at_server,
                'confirmed_at_server', bp.confirmed_at_server,
                'pages', coalesce(
                    (
                        select pg_catalog.jsonb_agg(
                            pg_catalog.jsonb_build_object(
                                'page_index', pp.page_index,
                                'attachment_id', pp.attachment_id,
                                'header_redacted', pp.header_redacted,
                                'bucket_id', att.bucket_id,
                                'object_name', att.object_name,
                                'content_type', att.content_type,
                                'byte_size', att.byte_size
                            )
                            order by pp.page_index asc
                        )
                          from public.paper_pages as pp
                          join public.observation_attachments as att
                            on att.id = pp.attachment_id
                         where pp.paper_submission_id = bp.id
                    ),
                    '[]'::jsonb
                ),
                'mistakes', coalesce(
                    (
                        select pg_catalog.jsonb_agg(
                            pg_catalog.jsonb_build_object(
                                'mistake_id', pm.id,
                                'page_index', pm.page_index,
                                'question_label', pm.question_label,
                                'topic_tags', pm.topic_tags,
                                'error_root_cause', pm.error_root_cause,
                                'paper_section_position', pm.paper_section_position,
                                'points_lost', pm.points_lost,
                                'corrected_on_paper', pm.corrected_on_paper,
                                'summary', pm.summary,
                                'provenance', pm.provenance,
                                'linked_case_id', pm.linked_case_id
                            )
                            order by pm.page_index asc, pm.id asc
                        )
                          from public.paper_mistake_items as pm
                         where pm.paper_submission_id = bp.id
                    ),
                    '[]'::jsonb
                )
            )
            order by bp.paper_date desc, bp.created_at_server desc, bp.id desc
        ),
        '[]'::jsonb
    )
      into v_papers
      from bounded_papers as bp;

    -- 6. Latest 5 study plan drafts for this student+subject
    with bounded_drafts as (
        select draft.*
          from public.study_plan_drafts as draft
         where draft.organization_id = p_organization_id
           and draft.student_id = p_student_id
           and draft.subject_profile_id = p_subject_profile_id
         order by draft.created_at_server desc, draft.id desc
         limit 5
    )
    select coalesce(
        pg_catalog.jsonb_agg(
            pg_catalog.jsonb_build_object(
                'draft_id', bd.id,
                'title', bd.title,
                'brief_markdown', bd.brief_markdown,
                'evidence_refs', bd.evidence_refs,
                'proposed_actions', bd.proposed_actions,
                'window_days', bd.window_days,
                'paper_count_analyzed', bd.paper_count_analyzed,
                'status', bd.status,
                'version', bd.version,
                'created_at_server', bd.created_at_server,
                'updated_at_server', bd.updated_at_server
            )
            order by bd.created_at_server desc, bd.id desc
        ),
        '[]'::jsonb
    )
      into v_drafts
      from bounded_drafts as bd;

    return pg_catalog.jsonb_build_object(
        'contract', 'student_paper_longitudinal_v1',
        'generated_at_server', v_generated_at,
        'actor_app_user_id', v_actor_id,
        'organization_id', p_organization_id,
        'organization_name', v_context.organization_name,
        'organization_time_zone', v_context.time_zone,
        'organization_business_date', v_business_date,
        'student_id', p_student_id,
        'student_display_name', v_context.student_display_name,
        'subject_profile_id', p_subject_profile_id,
        'subject_key', v_context.subject_key,
        'assignment_id', v_context.assignment_id,
        'window_days', v_window_days,
        'window_start_date', v_window_start,
        'summary', v_summary,
        'weekly_cadence', v_weekly_cadence,
        'root_cause_breakdown', v_root_cause_breakdown,
        'recurring_topics', v_recurring_topics,
        'papers', v_papers,
        'recent_study_plan_drafts', v_drafts
    );
end;
$$;

revoke execute on function public.get_student_paper_longitudinal_v1(
    uuid, uuid, uuid, integer, integer
) from public, anon;
grant execute on function public.get_student_paper_longitudinal_v1(
    uuid, uuid, uuid, integer, integer
) to authenticated;

-- ---------------------------------------------------------------------------
-- 4. save_study_plan_draft_v1
-- ---------------------------------------------------------------------------
create or replace function public.save_study_plan_draft_v1(
    p_operation_id uuid,
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid,
    p_assignment_id uuid,
    p_title text,
    p_brief_markdown text,
    p_evidence_refs jsonb default '[]'::jsonb,
    p_proposed_actions jsonb default '[]'::jsonb,
    p_window_days integer default 30,
    p_paper_count_analyzed integer default 0
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_provider_key text;
    v_issuer text;
    v_external_subject text;
    v_actor_id uuid;
    v_actor_enabled boolean;
    v_membership_status text;
    v_can_teach boolean;
    v_trimmed_title text;
    v_trimmed_brief text;
    v_request_payload jsonb;
    v_existing_receipt public.operation_receipts%rowtype;
    v_draft_id uuid;
    v_committed_at timestamptz;
    v_result jsonb;
begin
    if p_operation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_ID_REQUIRED';
    end if;

    if p_organization_id is null
       or p_student_id is null
       or p_subject_profile_id is null
       or p_assignment_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;

    v_trimmed_title := pg_catalog.btrim(coalesce(p_title, ''));
    v_trimmed_brief := pg_catalog.btrim(coalesce(p_brief_markdown, ''));
    if pg_catalog.char_length(v_trimmed_title) < 1
       or pg_catalog.char_length(v_trimmed_title) > 200
       or pg_catalog.char_length(v_trimmed_brief) < 1
       or pg_catalog.char_length(v_trimmed_brief) > 12000
       or p_evidence_refs is null
       or pg_catalog.jsonb_typeof(p_evidence_refs) <> 'array'
       or p_proposed_actions is null
       or pg_catalog.jsonb_typeof(p_proposed_actions) <> 'array'
       or p_window_days is null
       or p_window_days < 7
       or p_window_days > 365
       or p_paper_count_analyzed is null
       or p_paper_count_analyzed < 0 then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_STUDY_PLAN_DRAFT';
    end if;

    select identity.provider_key, identity.issuer, identity.external_subject
      into v_provider_key, v_issuer, v_external_subject
      from xq_internal.current_external_identity_v1() as identity;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_AUTH_REQUIRED';
    end if;

    select app_user.id, app_user.enabled
      into v_actor_id, v_actor_enabled
      from public.identity_links as identity_link
      join public.app_users as app_user
        on app_user.id = identity_link.app_user_id
     where identity_link.provider_key = v_provider_key
       and identity_link.issuer = v_issuer
       and identity_link.external_subject = v_external_subject
       and identity_link.active
     for share of identity_link, app_user;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_NOT_FOUND';
    end if;
    if not v_actor_enabled then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_DISABLED';
    end if;

    v_request_payload := pg_catalog.jsonb_build_object(
        'actor_app_user_id', v_actor_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'subject_profile_id', p_subject_profile_id,
        'assignment_id', p_assignment_id,
        'title', v_trimmed_title,
        'brief_markdown', v_trimmed_brief,
        'evidence_refs', p_evidence_refs,
        'proposed_actions', p_proposed_actions,
        'window_days', p_window_days,
        'paper_count_analyzed', p_paper_count_analyzed
    );

    perform pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended(p_operation_id::text, 0)
    );

    select receipt.*
      into v_existing_receipt
      from public.operation_receipts as receipt
     where receipt.operation_id = p_operation_id;

    if found then
        if v_existing_receipt.actor_app_user_id = v_actor_id
           and v_existing_receipt.command_name = 'save_study_plan_draft_v1'
           and v_existing_receipt.request_payload = v_request_payload then
            return v_existing_receipt.result_payload;
        end if;

        raise exception using
            errcode = 'P0001',
            message = 'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD';
    end if;

    select membership.status, membership.can_teach
      into v_membership_status, v_can_teach
      from public.memberships as membership
     where membership.organization_id = p_organization_id
       and membership.app_user_id = v_actor_id
     for share;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_MEMBERSHIP_REQUIRED';
    end if;
    if v_membership_status <> 'active' then
        raise exception using errcode = 'P0001', message = 'XQ_MEMBERSHIP_DISABLED';
    end if;
    if not v_can_teach then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CAPABILITY_REQUIRED';
    end if;

    perform 1
      from public.student_teacher_assignments as assignment
      join public.students as student
        on student.organization_id = assignment.organization_id
       and student.id = assignment.student_id
      join public.student_subject_profiles as profile
        on profile.organization_id = assignment.organization_id
       and profile.student_id = assignment.student_id
       and profile.id = assignment.subject_profile_id
     where assignment.organization_id = p_organization_id
       and assignment.student_id = p_student_id
       and assignment.subject_profile_id = p_subject_profile_id
       and assignment.id = p_assignment_id
       and assignment.teacher_app_user_id = v_actor_id
       and assignment.active
       and student.active
       and profile.active
     for share of assignment, student, profile;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_UNAVAILABLE';
    end if;

    v_draft_id := pg_catalog.gen_random_uuid();
    v_committed_at := pg_catalog.clock_timestamp();

    insert into public.study_plan_drafts (
        id,
        operation_id,
        organization_id,
        student_id,
        subject_profile_id,
        assignment_id,
        actor_app_user_id,
        title,
        brief_markdown,
        evidence_refs,
        proposed_actions,
        window_days,
        paper_count_analyzed,
        status,
        version,
        created_at_server,
        updated_at_server
    ) values (
        v_draft_id,
        p_operation_id,
        p_organization_id,
        p_student_id,
        p_subject_profile_id,
        p_assignment_id,
        v_actor_id,
        v_trimmed_title,
        v_trimmed_brief,
        p_evidence_refs,
        p_proposed_actions,
        p_window_days,
        p_paper_count_analyzed,
        'draft',
        1,
        v_committed_at,
        v_committed_at
    );

    v_result := pg_catalog.jsonb_build_object(
        'contract', 'save_study_plan_draft_v1',
        'operation_id', p_operation_id,
        'draft_id', v_draft_id,
        'status', 'draft',
        'version', 1,
        'server_committed_at', v_committed_at
    );

    insert into public.operation_receipts (
        operation_id,
        actor_app_user_id,
        command_name,
        request_payload,
        result_payload,
        committed_at
    ) values (
        p_operation_id,
        v_actor_id,
        'save_study_plan_draft_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.save_study_plan_draft_v1(
    uuid, uuid, uuid, uuid, uuid, text, text, jsonb, jsonb, integer, integer
) from public, anon;
grant execute on function public.save_study_plan_draft_v1(
    uuid, uuid, uuid, uuid, uuid, text, text, jsonb, jsonb, integer, integer
) to authenticated;

-- ---------------------------------------------------------------------------
-- 5. update_study_plan_draft_status_v1
-- ---------------------------------------------------------------------------
create or replace function public.update_study_plan_draft_status_v1(
    p_operation_id uuid,
    p_draft_id uuid,
    p_expected_version bigint,
    p_status text
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_provider_key text;
    v_issuer text;
    v_external_subject text;
    v_actor_id uuid;
    v_actor_enabled boolean;
    v_membership_status text;
    v_can_teach boolean;
    v_draft public.study_plan_drafts%rowtype;
    v_request_payload jsonb;
    v_existing_receipt public.operation_receipts%rowtype;
    v_next_version bigint;
    v_committed_at timestamptz;
    v_result jsonb;
begin
    if p_operation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_ID_REQUIRED';
    end if;

    if p_draft_id is null or p_expected_version is null or p_expected_version < 1 then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;

    if p_status is null
       or p_status not in ('partially_adopted', 'fully_adopted', 'dismissed') then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_STUDY_PLAN_STATUS';
    end if;

    select identity.provider_key, identity.issuer, identity.external_subject
      into v_provider_key, v_issuer, v_external_subject
      from xq_internal.current_external_identity_v1() as identity;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_AUTH_REQUIRED';
    end if;

    select app_user.id, app_user.enabled
      into v_actor_id, v_actor_enabled
      from public.identity_links as identity_link
      join public.app_users as app_user
        on app_user.id = identity_link.app_user_id
     where identity_link.provider_key = v_provider_key
       and identity_link.issuer = v_issuer
       and identity_link.external_subject = v_external_subject
       and identity_link.active
     for share of identity_link, app_user;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_NOT_FOUND';
    end if;
    if not v_actor_enabled then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_DISABLED';
    end if;

    v_request_payload := pg_catalog.jsonb_build_object(
        'actor_app_user_id', v_actor_id,
        'draft_id', p_draft_id,
        'expected_version', p_expected_version,
        'status', p_status
    );

    perform pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended(p_operation_id::text, 0)
    );

    select receipt.*
      into v_existing_receipt
      from public.operation_receipts as receipt
     where receipt.operation_id = p_operation_id;

    if found then
        if v_existing_receipt.actor_app_user_id = v_actor_id
           and v_existing_receipt.command_name = 'update_study_plan_draft_status_v1'
           and v_existing_receipt.request_payload = v_request_payload then
            return v_existing_receipt.result_payload;
        end if;

        raise exception using
            errcode = 'P0001',
            message = 'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD';
    end if;

    select draft.*
      into v_draft
      from public.study_plan_drafts as draft
     where draft.id = p_draft_id
     for update;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_STUDY_PLAN_DRAFT_NOT_FOUND';
    end if;

    select membership.status, membership.can_teach
      into v_membership_status, v_can_teach
      from public.memberships as membership
     where membership.organization_id = v_draft.organization_id
       and membership.app_user_id = v_actor_id
     for share;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_MEMBERSHIP_REQUIRED';
    end if;
    if v_membership_status <> 'active' then
        raise exception using errcode = 'P0001', message = 'XQ_MEMBERSHIP_DISABLED';
    end if;
    if not v_can_teach then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CAPABILITY_REQUIRED';
    end if;

    perform 1
      from public.student_teacher_assignments as assignment
      join public.students as student
        on student.organization_id = assignment.organization_id
       and student.id = assignment.student_id
      join public.student_subject_profiles as profile
        on profile.organization_id = assignment.organization_id
       and profile.student_id = assignment.student_id
       and profile.id = assignment.subject_profile_id
     where assignment.organization_id = v_draft.organization_id
       and assignment.student_id = v_draft.student_id
       and assignment.subject_profile_id = v_draft.subject_profile_id
       and assignment.teacher_app_user_id = v_actor_id
       and assignment.active
       and student.active
       and profile.active
     for share of assignment, student, profile;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_UNAVAILABLE';
    end if;

    if v_draft.version <> p_expected_version then
        raise exception using errcode = 'P0001', message = 'XQ_VERSION_CONFLICT';
    end if;

    v_next_version := v_draft.version + 1;
    v_committed_at := pg_catalog.clock_timestamp();

    update public.study_plan_drafts as draft
       set status = p_status,
           version = v_next_version,
           updated_at_server = v_committed_at
     where draft.id = v_draft.id;

    v_result := pg_catalog.jsonb_build_object(
        'contract', 'update_study_plan_draft_status_v1',
        'operation_id', p_operation_id,
        'draft_id', v_draft.id,
        'status', p_status,
        'version', v_next_version,
        'server_committed_at', v_committed_at
    );

    insert into public.operation_receipts (
        operation_id,
        actor_app_user_id,
        command_name,
        request_payload,
        result_payload,
        committed_at
    ) values (
        p_operation_id,
        v_actor_id,
        'update_study_plan_draft_status_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.update_study_plan_draft_status_v1(
    uuid, uuid, bigint, text
) from public, anon;
grant execute on function public.update_study_plan_draft_status_v1(
    uuid, uuid, bigint, text
) to authenticated;
