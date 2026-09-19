-- Phase 1 Action progression commands.
-- Adds explicit optimistic concurrency, immutable Verification facts and
-- atomic replacement of the open Case's one pending primary Action.

alter table public.learning_case_actions
    add constraint learning_case_actions_scope_id_unique
    unique (
        organization_id,
        student_id,
        subject_profile_id,
        case_id,
        id
    );

alter table public.learning_case_events
    drop constraint learning_case_events_event_type_check;

alter table public.learning_case_events
    add constraint learning_case_events_event_type_check
    check (
        event_type in (
            'case_created',
            'primary_action_rescheduled',
            'verification_recorded_next_action_created'
        )
    );

create table public.learning_case_verifications (
    id uuid primary key,
    operation_id uuid not null unique,
    organization_id uuid not null,
    student_id uuid not null,
    subject_profile_id uuid not null,
    case_id uuid not null,
    completed_primary_action_id uuid not null,
    action_version_before bigint not null check (action_version_before >= 1),
    outcome text not null
        check (outcome in ('met', 'partially_met', 'not_met', 'uncertain')),
    summary text not null
        check (pg_catalog.char_length(pg_catalog.btrim(summary)) between 1 and 2000),
    actor_app_user_id uuid not null
        references public.app_users(id) on delete restrict,
    occurred_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    foreign key (
        organization_id,
        student_id,
        subject_profile_id,
        case_id,
        completed_primary_action_id
    ) references public.learning_case_actions (
        organization_id,
        student_id,
        subject_profile_id,
        case_id,
        id
    ) on delete restrict
);

alter table public.learning_case_verifications enable row level security;
revoke all on table public.learning_case_verifications from anon, authenticated;

create or replace function public.reschedule_primary_action(
    p_operation_id uuid,
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid,
    p_owner_assignment_id uuid,
    p_case_id uuid,
    p_primary_action_id uuid,
    p_expected_case_version bigint,
    p_expected_action_version bigint,
    p_new_due_on date default null
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
    v_subject_key text;
    v_request_payload jsonb;
    v_existing_receipt public.operation_receipts%rowtype;
    v_case_state text;
    v_case_version bigint;
    v_action_version bigint;
    v_previous_due_on date;
    v_new_case_version bigint;
    v_new_action_version bigint;
    v_event_id uuid;
    v_committed_at timestamptz;
    v_result jsonb;
begin
    if p_operation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_ID_REQUIRED';
    end if;

    if p_organization_id is null
       or p_student_id is null
       or p_subject_profile_id is null
       or p_owner_assignment_id is null
       or p_case_id is null
       or p_primary_action_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;

    if p_expected_case_version is null or p_expected_case_version < 1 then
        raise exception using errcode = 'P0001', message = 'XQ_EXPECTED_CASE_VERSION_REQUIRED';
    end if;

    if p_expected_action_version is null or p_expected_action_version < 1 then
        raise exception using errcode = 'P0001', message = 'XQ_EXPECTED_ACTION_VERSION_REQUIRED';
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
        'owner_assignment_id', p_owner_assignment_id,
        'case_id', p_case_id,
        'primary_action_id', p_primary_action_id,
        'expected_case_version', p_expected_case_version,
        'expected_action_version', p_expected_action_version,
        'new_due_on', p_new_due_on
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
           and v_existing_receipt.command_name = 'reschedule_primary_action_v1'
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
      from public.students as student
     where student.organization_id = p_organization_id
       and student.id = p_student_id
       and student.active
     for share;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_STUDENT_NOT_IN_ORG';
    end if;

    select profile.subject_key
      into v_subject_key
      from public.student_subject_profiles as profile
     where profile.organization_id = p_organization_id
       and profile.student_id = p_student_id
       and profile.id = p_subject_profile_id
       and profile.active
     for share;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_SUBJECT_PROFILE_REQUIRED';
    end if;

    perform 1
      from public.student_teacher_assignments as assignment
     where assignment.organization_id = p_organization_id
       and assignment.student_id = p_student_id
       and assignment.subject_profile_id = p_subject_profile_id
       and assignment.id = p_owner_assignment_id
       and assignment.teacher_app_user_id = v_actor_id
       and assignment.active
     for share;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHER_ASSIGNMENT_REQUIRED';
    end if;

    select learning_case.state, learning_case.version
      into v_case_state, v_case_version
      from public.learning_cases as learning_case
     where learning_case.organization_id = p_organization_id
       and learning_case.student_id = p_student_id
       and learning_case.subject_profile_id = p_subject_profile_id
       and learning_case.id = p_case_id
       and learning_case.owner_assignment_id = p_owner_assignment_id
       and learning_case.responsible_teacher_app_user_id = v_actor_id
     for update;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_LEARNING_CASE_REQUIRED';
    end if;

    if v_case_state = 'closed' then
        raise exception using errcode = 'P0001', message = 'XQ_CASE_CLOSED';
    end if;

    if v_case_version <> p_expected_case_version then
        raise exception using errcode = 'P0001', message = 'XQ_CASE_VERSION_CONFLICT';
    end if;

    select action.version, action.due_on
      into v_action_version, v_previous_due_on
      from public.learning_case_actions as action
     where action.organization_id = p_organization_id
       and action.student_id = p_student_id
       and action.subject_profile_id = p_subject_profile_id
       and action.case_id = p_case_id
       and action.id = p_primary_action_id
       and action.assignee_assignment_id = p_owner_assignment_id
       and action.assignee_teacher_app_user_id = v_actor_id
       and action.action_role = 'primary'
       and action.status = 'pending'
     for update;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_PRIMARY_ACTION_REQUIRED';
    end if;

    if v_action_version <> p_expected_action_version then
        raise exception using errcode = 'P0001', message = 'XQ_ACTION_VERSION_CONFLICT';
    end if;

    if v_previous_due_on is not distinct from p_new_due_on then
        raise exception using errcode = 'P0001', message = 'XQ_ACTION_DUE_UNCHANGED';
    end if;

    v_committed_at := pg_catalog.clock_timestamp();
    v_new_action_version := v_action_version + 1;
    v_new_case_version := v_case_version + 1;
    v_event_id := pg_catalog.gen_random_uuid();

    update public.learning_case_actions
       set due_on = p_new_due_on,
           version = v_new_action_version,
           updated_at_server = v_committed_at
     where id = p_primary_action_id;

    update public.learning_cases
       set version = v_new_case_version,
           updated_at_server = v_committed_at
     where id = p_case_id;

    insert into public.learning_case_events (
        id,
        operation_id,
        organization_id,
        student_id,
        subject_profile_id,
        case_id,
        event_type,
        actor_app_user_id,
        event_payload,
        occurred_at_server
    ) values (
        v_event_id,
        p_operation_id,
        p_organization_id,
        p_student_id,
        p_subject_profile_id,
        p_case_id,
        'primary_action_rescheduled',
        v_actor_id,
        pg_catalog.jsonb_build_object(
            'case_version_before', v_case_version,
            'case_version_after', v_new_case_version,
            'primary_action_id', p_primary_action_id,
            'action_version_before', v_action_version,
            'action_version_after', v_new_action_version,
            'previous_due_on', v_previous_due_on,
            'due_on', p_new_due_on
        ),
        v_committed_at
    );

    v_result := pg_catalog.jsonb_build_object(
        'command', 'reschedule_primary_action_v1',
        'operation_id', p_operation_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'subject_profile_id', p_subject_profile_id,
        'subject_key', v_subject_key,
        'case_id', p_case_id,
        'case_state', v_case_state,
        'case_version', v_new_case_version,
        'primary_action_id', p_primary_action_id,
        'action_version', v_new_action_version,
        'previous_due_on', v_previous_due_on,
        'due_on', p_new_due_on,
        'case_event_id', v_event_id,
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
        'reschedule_primary_action_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.reschedule_primary_action(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint, date
) from public;
revoke execute on function public.reschedule_primary_action(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint, date
) from anon;
grant execute on function public.reschedule_primary_action(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint, date
) to authenticated;

comment on function public.reschedule_primary_action(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint, date
) is
'ReschedulePrimaryAction v1: optimistic concurrency + live teaching authority + append-only event + idempotent receipt.';

create or replace function public.record_verification_and_next_action(
    p_operation_id uuid,
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid,
    p_owner_assignment_id uuid,
    p_case_id uuid,
    p_current_primary_action_id uuid,
    p_expected_case_version bigint,
    p_expected_action_version bigint,
    p_verification_outcome text,
    p_verification_summary text,
    p_next_action_text text,
    p_next_action_due_on date default null
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
    v_subject_key text;
    v_request_payload jsonb;
    v_existing_receipt public.operation_receipts%rowtype;
    v_case_state text;
    v_case_version bigint;
    v_action_version bigint;
    v_completed_action_version bigint;
    v_new_case_version bigint;
    v_verification_id uuid;
    v_next_action_id uuid;
    v_event_id uuid;
    v_committed_at timestamptz;
    v_result jsonb;
begin
    if p_operation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_ID_REQUIRED';
    end if;

    if p_organization_id is null
       or p_student_id is null
       or p_subject_profile_id is null
       or p_owner_assignment_id is null
       or p_case_id is null
       or p_current_primary_action_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;

    if p_expected_case_version is null or p_expected_case_version < 1 then
        raise exception using errcode = 'P0001', message = 'XQ_EXPECTED_CASE_VERSION_REQUIRED';
    end if;

    if p_expected_action_version is null or p_expected_action_version < 1 then
        raise exception using errcode = 'P0001', message = 'XQ_EXPECTED_ACTION_VERSION_REQUIRED';
    end if;

    if p_verification_outcome is null
       or p_verification_outcome not in ('met', 'partially_met', 'not_met', 'uncertain') then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_VERIFICATION_OUTCOME';
    end if;

    if p_verification_summary is null
       or pg_catalog.char_length(pg_catalog.btrim(p_verification_summary)) < 1
       or pg_catalog.char_length(pg_catalog.btrim(p_verification_summary)) > 2000 then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_VERIFICATION_SUMMARY';
    end if;

    if p_next_action_text is null
       or pg_catalog.char_length(pg_catalog.btrim(p_next_action_text)) < 1
       or pg_catalog.char_length(pg_catalog.btrim(p_next_action_text)) > 1000 then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_PRIMARY_ACTION';
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
        'owner_assignment_id', p_owner_assignment_id,
        'case_id', p_case_id,
        'current_primary_action_id', p_current_primary_action_id,
        'expected_case_version', p_expected_case_version,
        'expected_action_version', p_expected_action_version,
        'verification_outcome', p_verification_outcome,
        'verification_summary', pg_catalog.btrim(p_verification_summary),
        'next_action_text', pg_catalog.btrim(p_next_action_text),
        'next_action_due_on', p_next_action_due_on
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
           and v_existing_receipt.command_name = 'record_verification_and_next_action_v1'
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
      from public.students as student
     where student.organization_id = p_organization_id
       and student.id = p_student_id
       and student.active
     for share;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_STUDENT_NOT_IN_ORG';
    end if;

    select profile.subject_key
      into v_subject_key
      from public.student_subject_profiles as profile
     where profile.organization_id = p_organization_id
       and profile.student_id = p_student_id
       and profile.id = p_subject_profile_id
       and profile.active
     for share;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_SUBJECT_PROFILE_REQUIRED';
    end if;

    perform 1
      from public.student_teacher_assignments as assignment
     where assignment.organization_id = p_organization_id
       and assignment.student_id = p_student_id
       and assignment.subject_profile_id = p_subject_profile_id
       and assignment.id = p_owner_assignment_id
       and assignment.teacher_app_user_id = v_actor_id
       and assignment.active
     for share;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHER_ASSIGNMENT_REQUIRED';
    end if;

    select learning_case.state, learning_case.version
      into v_case_state, v_case_version
      from public.learning_cases as learning_case
     where learning_case.organization_id = p_organization_id
       and learning_case.student_id = p_student_id
       and learning_case.subject_profile_id = p_subject_profile_id
       and learning_case.id = p_case_id
       and learning_case.owner_assignment_id = p_owner_assignment_id
       and learning_case.responsible_teacher_app_user_id = v_actor_id
     for update;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_LEARNING_CASE_REQUIRED';
    end if;

    if v_case_state = 'closed' then
        raise exception using errcode = 'P0001', message = 'XQ_CASE_CLOSED';
    end if;

    if v_case_version <> p_expected_case_version then
        raise exception using errcode = 'P0001', message = 'XQ_CASE_VERSION_CONFLICT';
    end if;

    select action.version
      into v_action_version
      from public.learning_case_actions as action
     where action.organization_id = p_organization_id
       and action.student_id = p_student_id
       and action.subject_profile_id = p_subject_profile_id
       and action.case_id = p_case_id
       and action.id = p_current_primary_action_id
       and action.assignee_assignment_id = p_owner_assignment_id
       and action.assignee_teacher_app_user_id = v_actor_id
       and action.action_role = 'primary'
       and action.status = 'pending'
     for update;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_PRIMARY_ACTION_REQUIRED';
    end if;

    if v_action_version <> p_expected_action_version then
        raise exception using errcode = 'P0001', message = 'XQ_ACTION_VERSION_CONFLICT';
    end if;

    v_committed_at := pg_catalog.clock_timestamp();
    v_completed_action_version := v_action_version + 1;
    v_new_case_version := v_case_version + 1;
    v_verification_id := pg_catalog.gen_random_uuid();
    v_next_action_id := pg_catalog.gen_random_uuid();
    v_event_id := pg_catalog.gen_random_uuid();

    insert into public.learning_case_verifications (
        id,
        operation_id,
        organization_id,
        student_id,
        subject_profile_id,
        case_id,
        completed_primary_action_id,
        action_version_before,
        outcome,
        summary,
        actor_app_user_id,
        occurred_at_server
    ) values (
        v_verification_id,
        p_operation_id,
        p_organization_id,
        p_student_id,
        p_subject_profile_id,
        p_case_id,
        p_current_primary_action_id,
        v_action_version,
        p_verification_outcome,
        pg_catalog.btrim(p_verification_summary),
        v_actor_id,
        v_committed_at
    );

    update public.learning_case_actions
       set status = 'completed',
           version = v_completed_action_version,
           completed_at_server = v_committed_at,
           updated_at_server = v_committed_at
     where id = p_current_primary_action_id;

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
        v_next_action_id,
        p_organization_id,
        p_student_id,
        p_subject_profile_id,
        p_case_id,
        p_owner_assignment_id,
        v_actor_id,
        'primary',
        'pending',
        pg_catalog.btrim(p_next_action_text),
        p_next_action_due_on,
        1,
        v_actor_id,
        v_committed_at,
        v_committed_at
    );

    update public.learning_cases
       set version = v_new_case_version,
           updated_at_server = v_committed_at
     where id = p_case_id;

    insert into public.learning_case_events (
        id,
        operation_id,
        organization_id,
        student_id,
        subject_profile_id,
        case_id,
        event_type,
        actor_app_user_id,
        event_payload,
        occurred_at_server
    ) values (
        v_event_id,
        p_operation_id,
        p_organization_id,
        p_student_id,
        p_subject_profile_id,
        p_case_id,
        'verification_recorded_next_action_created',
        v_actor_id,
        pg_catalog.jsonb_build_object(
            'case_state', v_case_state,
            'case_version_before', v_case_version,
            'case_version_after', v_new_case_version,
            'completed_primary_action_id', p_current_primary_action_id,
            'completed_action_version_before', v_action_version,
            'completed_action_version_after', v_completed_action_version,
            'verification_id', v_verification_id,
            'verification_outcome', p_verification_outcome,
            'next_primary_action_id', v_next_action_id,
            'next_action_text', pg_catalog.btrim(p_next_action_text),
            'next_action_due_on', p_next_action_due_on
        ),
        v_committed_at
    );

    v_result := pg_catalog.jsonb_build_object(
        'command', 'record_verification_and_next_action_v1',
        'operation_id', p_operation_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'subject_profile_id', p_subject_profile_id,
        'subject_key', v_subject_key,
        'case_id', p_case_id,
        'case_state', v_case_state,
        'case_version', v_new_case_version,
        'completed_primary_action_id', p_current_primary_action_id,
        'completed_action_version', v_completed_action_version,
        'verification_id', v_verification_id,
        'verification_outcome', p_verification_outcome,
        'next_primary_action_id', v_next_action_id,
        'next_action_version', 1,
        'case_event_id', v_event_id,
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
        'record_verification_and_next_action_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.record_verification_and_next_action(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint, text, text, text, date
) from public;
revoke execute on function public.record_verification_and_next_action(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint, text, text, text, date
) from anon;
grant execute on function public.record_verification_and_next_action(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint, text, text, text, date
) to authenticated;

comment on function public.record_verification_and_next_action(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint, text, text, text, date
) is
'RecordVerificationAndNextAction v1: immutable Verification + atomic current Action completion + next pending primary Action + optimistic concurrency.';
