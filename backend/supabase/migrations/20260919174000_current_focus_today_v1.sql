-- Phase 1 read models: organization business date + personal Current Focus / Today.
-- Business-day semantics belong to the Organization, never to the client device.

alter table public.organizations
    add column time_zone text not null default 'Etc/UTC'
        check (pg_catalog.char_length(pg_catalog.btrim(time_zone)) between 1 and 100);

create or replace function xq_internal.validate_organization_time_zone_v1()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    perform 1
      from pg_catalog.pg_timezone_names as zone
     where zone.name = new.time_zone;

    if not found then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_INVALID_ORGANIZATION_TIME_ZONE';
    end if;

    return new;
end;
$$;

revoke all on function xq_internal.validate_organization_time_zone_v1() from public;
revoke all on function xq_internal.validate_organization_time_zone_v1() from anon;
revoke all on function xq_internal.validate_organization_time_zone_v1() from authenticated;

create trigger organizations_validate_time_zone_v1
before insert or update of time_zone on public.organizations
for each row
execute function xq_internal.validate_organization_time_zone_v1();

create or replace function xq_internal.organization_business_date_v1(
    p_time_zone text,
    p_at timestamptz
)
returns date
language plpgsql
stable
set search_path = ''
as $$
begin
    if p_time_zone is null or p_at is null then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_BUSINESS_DATE_INPUT_REQUIRED';
    end if;

    perform 1
      from pg_catalog.pg_timezone_names as zone
     where zone.name = p_time_zone;

    if not found then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_INVALID_ORGANIZATION_TIME_ZONE';
    end if;

    return pg_catalog.timezone(p_time_zone, p_at)::date;
end;
$$;

revoke all on function xq_internal.organization_business_date_v1(text, timestamptz) from public;
revoke all on function xq_internal.organization_business_date_v1(text, timestamptz) from anon;
revoke all on function xq_internal.organization_business_date_v1(text, timestamptz) from authenticated;

create or replace function public.get_student_learning_focus_v1(
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid
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
    v_cases jsonb;
    v_has_more boolean;
begin
    if p_organization_id is null
       or p_student_id is null
       or p_subject_profile_id is null then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_TEACHING_CONTEXT_REQUIRED';
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
        raise exception using
            errcode = 'P0001',
            message = 'XQ_TEACHING_CONTEXT_UNAVAILABLE';
    end if;

    v_generated_at := pg_catalog.statement_timestamp();
    v_business_date := xq_internal.organization_business_date_v1(
        v_context.time_zone,
        v_generated_at
    );

    if exists (
        select 1
          from public.learning_cases as learning_case
         where learning_case.organization_id = p_organization_id
           and learning_case.student_id = p_student_id
           and learning_case.subject_profile_id = p_subject_profile_id
           and learning_case.owner_assignment_id = v_context.assignment_id
           and learning_case.responsible_teacher_app_user_id = v_actor_id
           and learning_case.state <> 'closed'
           and (
                select pg_catalog.count(*)
                  from public.learning_case_actions as action
                 where action.case_id = learning_case.id
                   and action.action_role = 'primary'
                   and action.status = 'pending'
           ) <> 1
    ) then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_CASE_PRIMARY_ACTION_INVARIANT';
    end if;

    with bounded as materialized (
        select
            learning_case.id as case_id,
            learning_case.title,
            learning_case.state,
            learning_case.version as case_version,
            learning_case.responsible_teacher_app_user_id,
            learning_case.owner_assignment_id,
            learning_case.created_at_server,
            learning_case.updated_at_server,
            action.id as action_id,
            action.action_text,
            action.due_on,
            action.version as action_version,
            case
                when action.due_on is null then 'undated'
                when action.due_on < v_business_date then 'overdue'
                when action.due_on = v_business_date then 'today'
                else 'future'
            end as due_bucket
          from public.learning_cases as learning_case
          join public.learning_case_actions as action
            on action.case_id = learning_case.id
           and action.action_role = 'primary'
           and action.status = 'pending'
         where learning_case.organization_id = p_organization_id
           and learning_case.student_id = p_student_id
           and learning_case.subject_profile_id = p_subject_profile_id
           and learning_case.owner_assignment_id = v_context.assignment_id
           and learning_case.responsible_teacher_app_user_id = v_actor_id
           and learning_case.state <> 'closed'
         order by learning_case.updated_at_server desc, learning_case.id desc
         limit 4
    ), visible as (
        select *
          from bounded
         order by updated_at_server desc, case_id desc
         limit 3
    )
    select
        coalesce(
            (
                select pg_catalog.jsonb_agg(
                    pg_catalog.jsonb_build_object(
                        'case_id', item.case_id,
                        'title', item.title,
                        'state', item.state,
                        'case_version', item.case_version,
                        'responsible_teacher_app_user_id', item.responsible_teacher_app_user_id,
                        'owner_assignment_id', item.owner_assignment_id,
                        'created_at_server', item.created_at_server,
                        'updated_at_server', item.updated_at_server,
                        'primary_action', pg_catalog.jsonb_build_object(
                            'action_id', item.action_id,
                            'action_text', item.action_text,
                            'due_on', item.due_on,
                            'due_bucket', item.due_bucket,
                            'action_version', item.action_version
                        )
                    )
                    order by item.updated_at_server desc, item.case_id desc
                )
                from visible as item
            ),
            '[]'::jsonb
        ),
        (select pg_catalog.count(*) > 3 from bounded)
      into v_cases, v_has_more;

    return pg_catalog.jsonb_build_object(
        'contract', 'student_learning_focus_v1',
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
        'cases', v_cases,
        'has_more', v_has_more
    );
end;
$$;

revoke execute on function public.get_student_learning_focus_v1(uuid, uuid, uuid) from public;
revoke execute on function public.get_student_learning_focus_v1(uuid, uuid, uuid) from anon;
grant execute on function public.get_student_learning_focus_v1(uuid, uuid, uuid) to authenticated;

comment on function public.get_student_learning_focus_v1(uuid, uuid, uuid) is
'Personal Student Learning Focus v1: at most three recent-active Cases with the authoritative pending primary Action, scoped by live teaching responsibility.';

create or replace function public.get_personal_today_actions_v1()
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
    v_generated_at timestamptz;
    v_actions jsonb;
    v_has_more boolean;
begin
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

    v_generated_at := pg_catalog.statement_timestamp();

    if exists (
        select 1
          from public.learning_cases as learning_case
          join public.student_teacher_assignments as assignment
            on assignment.organization_id = learning_case.organization_id
           and assignment.student_id = learning_case.student_id
           and assignment.subject_profile_id = learning_case.subject_profile_id
           and assignment.id = learning_case.owner_assignment_id
           and assignment.teacher_app_user_id = v_actor_id
           and assignment.active
          join public.memberships as membership
            on membership.organization_id = assignment.organization_id
           and membership.app_user_id = v_actor_id
           and membership.status = 'active'
           and membership.can_teach
          join public.students as student
            on student.organization_id = learning_case.organization_id
           and student.id = learning_case.student_id
           and student.active
          join public.student_subject_profiles as profile
            on profile.organization_id = learning_case.organization_id
           and profile.student_id = learning_case.student_id
           and profile.id = learning_case.subject_profile_id
           and profile.active
         where learning_case.responsible_teacher_app_user_id = v_actor_id
           and learning_case.state <> 'closed'
           and (
                select pg_catalog.count(*)
                  from public.learning_case_actions as action
                 where action.case_id = learning_case.id
                   and action.action_role = 'primary'
                   and action.status = 'pending'
           ) <> 1
    ) then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_CASE_PRIMARY_ACTION_INVARIANT';
    end if;

    with candidates as materialized (
        select
            organization.id as organization_id,
            organization.name as organization_name,
            organization.time_zone as organization_time_zone,
            xq_internal.organization_business_date_v1(
                organization.time_zone,
                v_generated_at
            ) as organization_business_date,
            student.id as student_id,
            student.display_name as student_display_name,
            profile.id as subject_profile_id,
            profile.subject_key,
            assignment.id as assignment_id,
            learning_case.id as case_id,
            learning_case.title as case_title,
            learning_case.state as case_state,
            learning_case.version as case_version,
            learning_case.updated_at_server as case_updated_at_server,
            action.id as action_id,
            action.action_text,
            action.due_on,
            action.version as action_version
          from public.learning_case_actions as action
          join public.learning_cases as learning_case
            on learning_case.id = action.case_id
           and learning_case.organization_id = action.organization_id
           and learning_case.student_id = action.student_id
           and learning_case.subject_profile_id = action.subject_profile_id
          join public.student_teacher_assignments as assignment
            on assignment.organization_id = action.organization_id
           and assignment.student_id = action.student_id
           and assignment.subject_profile_id = action.subject_profile_id
           and assignment.id = action.assignee_assignment_id
           and assignment.teacher_app_user_id = v_actor_id
           and assignment.active
          join public.memberships as membership
            on membership.organization_id = assignment.organization_id
           and membership.app_user_id = v_actor_id
           and membership.status = 'active'
           and membership.can_teach
          join public.organizations as organization
            on organization.id = action.organization_id
          join public.students as student
            on student.organization_id = action.organization_id
           and student.id = action.student_id
           and student.active
          join public.student_subject_profiles as profile
            on profile.organization_id = action.organization_id
           and profile.student_id = action.student_id
           and profile.id = action.subject_profile_id
           and profile.active
         where action.action_role = 'primary'
           and action.status = 'pending'
           and action.assignee_teacher_app_user_id = v_actor_id
           and learning_case.responsible_teacher_app_user_id = v_actor_id
           and learning_case.owner_assignment_id = assignment.id
           and learning_case.state <> 'closed'
    ), bucketed as materialized (
        select
            candidate.*,
            case
                when candidate.due_on is null then 'undated'
                when candidate.due_on < candidate.organization_business_date then 'overdue'
                when candidate.due_on = candidate.organization_business_date then 'today'
                else 'future'
            end as due_bucket,
            case
                when candidate.due_on is not null
                     and candidate.due_on < candidate.organization_business_date then 0
                when candidate.due_on = candidate.organization_business_date then 1
                when candidate.due_on is null then 2
                else 3
            end as bucket_rank
          from candidates as candidate
    ), bounded as materialized (
        select *
          from bucketed
         order by
            bucket_rank,
            due_on asc nulls last,
            case_updated_at_server desc,
            organization_name,
            student_display_name,
            action_id
         limit 201
    ), visible as (
        select *
          from bounded
         order by
            bucket_rank,
            due_on asc nulls last,
            case_updated_at_server desc,
            organization_name,
            student_display_name,
            action_id
         limit 200
    )
    select
        coalesce(
            (
                select pg_catalog.jsonb_agg(
                    pg_catalog.jsonb_build_object(
                        'organization_id', item.organization_id,
                        'organization_name', item.organization_name,
                        'organization_time_zone', item.organization_time_zone,
                        'organization_business_date', item.organization_business_date,
                        'student_id', item.student_id,
                        'student_display_name', item.student_display_name,
                        'subject_profile_id', item.subject_profile_id,
                        'subject_key', item.subject_key,
                        'assignment_id', item.assignment_id,
                        'case_id', item.case_id,
                        'case_title', item.case_title,
                        'case_state', item.case_state,
                        'case_version', item.case_version,
                        'action_id', item.action_id,
                        'action_text', item.action_text,
                        'due_on', item.due_on,
                        'due_bucket', item.due_bucket,
                        'action_version', item.action_version,
                        'case_updated_at_server', item.case_updated_at_server
                    )
                    order by
                        item.bucket_rank,
                        item.due_on asc nulls last,
                        item.case_updated_at_server desc,
                        item.organization_name,
                        item.student_display_name,
                        item.action_id
                )
                from visible as item
            ),
            '[]'::jsonb
        ),
        (select pg_catalog.count(*) > 200 from bounded)
      into v_actions, v_has_more;

    return pg_catalog.jsonb_build_object(
        'contract', 'personal_today_actions_v1',
        'generated_at_server', v_generated_at,
        'actor_app_user_id', v_actor_id,
        'actions', v_actions,
        'has_more', v_has_more
    );
end;
$$;

revoke execute on function public.get_personal_today_actions_v1() from public;
revoke execute on function public.get_personal_today_actions_v1() from anon;
grant execute on function public.get_personal_today_actions_v1() to authenticated;

comment on function public.get_personal_today_actions_v1() is
'Personal Today Actions v1: bounded explicit pending primary Actions, bucketed by each Organization business date and scoped by live personal teaching responsibility.';
