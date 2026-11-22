-- Paper Evidence Precision v1
--
-- Additive hardening on top of 20261122031500_paper_evidence_longitudinal_v1.sql:
--   1. public.get_paper_submission_v1(p_paper_submission_id, p_check_operation_id):
--      Exact single-paper authorized lookup that works regardless of the 365-day / 50-paper
--      longitudinal window and lets extract-paper-v1 pre-check idempotent operation_receipts
--      BEFORE invoking Gemini upstream.
--   2. public.get_student_paper_longitudinal_v1 calibration:
--      - teacher_marked_avg_score_ratio strictly averages human_confirmed papers so unverified
--        AI OCR drafts never pollute authoritative averages;
--      - adds draft_avg_score_ratio, confirmed_late_section_mistake_ratio, and prior_window_days
--        to summary for transparent statistical calibration.

-- ---------------------------------------------------------------------------
-- 1. get_paper_submission_v1 (exact single-paper lookup + idempotency pre-check)
-- ---------------------------------------------------------------------------
create or replace function public.get_paper_submission_v1(
    p_paper_submission_id uuid,
    p_check_operation_id uuid default null
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
    v_membership_status text;
    v_can_teach boolean;
    v_paper public.paper_submissions%rowtype;
    v_context record;
    v_pages jsonb;
    v_mistakes jsonb;
    v_existing_extraction_receipt jsonb := null;
begin
    if p_paper_submission_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
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
       and identity_link.active;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_NOT_FOUND';
    end if;
    if not v_actor_enabled then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_DISABLED';
    end if;

    select paper.*
      into v_paper
      from public.paper_submissions as paper
     where paper.id = p_paper_submission_id;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_PAPER_NOT_FOUND';
    end if;

    select membership.status, membership.can_teach
      into v_membership_status, v_can_teach
      from public.memberships as membership
     where membership.organization_id = v_paper.organization_id
       and membership.app_user_id = v_actor_id;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_MEMBERSHIP_REQUIRED';
    end if;
    if v_membership_status <> 'active' then
        raise exception using errcode = 'P0001', message = 'XQ_MEMBERSHIP_DISABLED';
    end if;
    if not v_can_teach then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CAPABILITY_REQUIRED';
    end if;

    select
        organization.name as organization_name,
        student.display_name as student_display_name,
        profile.subject_key,
        assignment.id as assignment_id
      into v_context
      from public.student_teacher_assignments as assignment
      join public.organizations as organization
        on organization.id = assignment.organization_id
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
       and profile.active;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_UNAVAILABLE';
    end if;

    if p_check_operation_id is not null then
        select receipt.result_payload
          into v_existing_extraction_receipt
          from public.operation_receipts as receipt
         where receipt.operation_id = p_check_operation_id
           and receipt.actor_app_user_id = v_actor_id
           and receipt.command_name = 'save_paper_extraction_v1'
           and (receipt.result_payload ->> 'paper_submission_id') = v_paper.id::text;
    end if;

    select coalesce(
        pg_catalog.jsonb_agg(
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
        ),
        '[]'::jsonb
    )
      into v_pages
      from public.paper_pages as pp
      join public.observation_attachments as att
        on att.id = pp.attachment_id
     where pp.paper_submission_id = v_paper.id;

    select coalesce(
        pg_catalog.jsonb_agg(
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
        ),
        '[]'::jsonb
    )
      into v_mistakes
      from public.paper_mistake_items as pm
     where pm.paper_submission_id = v_paper.id;

    return pg_catalog.jsonb_build_object(
        'contract', 'paper_submission_v1',
        'actor_app_user_id', v_actor_id,
        'organization_id', v_paper.organization_id,
        'organization_name', v_context.organization_name,
        'student_id', v_paper.student_id,
        'student_display_name', v_context.student_display_name,
        'subject_profile_id', v_paper.subject_profile_id,
        'subject_key', v_context.subject_key,
        'assignment_id', v_context.assignment_id,
        'paper_submission_id', v_paper.id,
        'source_observation_id', v_paper.source_observation_id,
        'paper_date', v_paper.paper_date,
        'paper_kind', v_paper.paper_kind,
        'title', v_paper.title,
        'grading_source', v_paper.grading_source,
        'confirmation_state', v_paper.confirmation_state,
        'question_count', v_paper.question_count,
        'mistake_count', v_paper.mistake_count,
        'score_earned', v_paper.score_earned,
        'score_total', v_paper.score_total,
        'duration_minutes', v_paper.duration_minutes,
        'extraction_summary', v_paper.extraction_summary,
        'version', v_paper.version,
        'created_at_server', v_paper.created_at_server,
        'confirmed_at_server', v_paper.confirmed_at_server,
        'pages', v_pages,
        'mistakes', v_mistakes,
        'existing_extraction_receipt', v_existing_extraction_receipt
    );
end;
$$;

revoke execute on function public.get_paper_submission_v1(uuid, uuid) from public, anon;
grant execute on function public.get_paper_submission_v1(uuid, uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- 2. Calibrate get_student_paper_longitudinal_v1 summary precision
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

    -- 1. Summary KPIs
    --    - teacher_marked_avg_score_ratio strictly averages human_confirmed papers so
    --      unverified AI OCR scores never pollute authoritative longitudinal averages.
    --    - draft_avg_score_ratio exposes preview average including draft_ready papers.
    --    - confirmed_late_section_mistake_ratio separates verified vs mixed fatigue ratio.
    --    - prior_window_days tells callers explicitly when window_days <= 30 has no prior window.
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
               and sp.confirmation_state = 'human_confirmed'
               and sp.score_earned is not null
               and sp.score_total is not null
               and sp.score_total > 0
        ),
        'draft_avg_score_ratio', (
            select pg_catalog.round(
                pg_catalog.avg((sp.score_earned / sp.score_total)::numeric),
                4
            )
              from scoped_papers as sp
             where sp.grading_source in ('teacher_marked', 'human_verified')
               and sp.confirmation_state in ('human_confirmed', 'draft_ready')
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
        ),
        'confirmed_late_section_mistake_ratio', (
            select case
                when pg_catalog.count(*) filter (
                    where sm.provenance = 'human_confirmed'
                      and sm.paper_section_position <> 'unknown'
                ) = 0 then null
                else pg_catalog.round(
                    (
                        (pg_catalog.count(*) filter (
                            where sm.provenance = 'human_confirmed'
                              and sm.paper_section_position = 'late'
                        ))::numeric
                        / (pg_catalog.count(*) filter (
                            where sm.provenance = 'human_confirmed'
                              and sm.paper_section_position <> 'unknown'
                        ))::numeric
                    ),
                    4
                )
            end
              from scoped_mistakes as sm
        ),
        'prior_window_days', greatest(0, v_window_days - 30)
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
