-- Phase 1 authoritative Learning Case lifecycle commands.
-- Open-state progression remains forward-only. Close/reopen are dedicated
-- commands because they change the pending-primary-Action invariant.

alter table public.learning_case_events
    drop constraint learning_case_events_event_type_check;

alter table public.learning_case_events
    add constraint learning_case_events_event_type_check
    check (
        event_type in (
            'case_created',
            'primary_action_rescheduled',
            'verification_recorded_next_action_created',
            'case_state_transitioned',
            'case_closed',
            'case_reopened'
        )
    );

create or replace function public.transition_learning_case_state(
    p_operation_id uuid,
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid,
    p_owner_assignment_id uuid,
    p_case_id uuid,
    p_expected_case_version bigint,
    p_target_state text
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
    v_allowed_target_state text;
    v_primary_action_id uuid;
    v_new_case_version bigint;
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
       or p_case_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;

    if p_expected_case_version is null or p_expected_case_version < 1 then
        raise exception using errcode = 'P0001', message = 'XQ_EXPECTED_CASE_VERSION_REQUIRED';
    end if;

    if p_target_state is null
       or p_target_state not in ('confirmed', 'intervening', 'pending_verification', 'stable') then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_CASE_TARGET_STATE';
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
        'expected_case_version', p_expected_case_version,
        'target_state', p_target_state
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
           and v_existing_receipt.command_name = 'transition_learning_case_state_v1'
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

    if v_case_version <> p_expected_case_version then
        raise exception using errcode = 'P0001', message = 'XQ_CASE_VERSION_CONFLICT';
    end if;

    v_allowed_target_state := case v_case_state
        when 'new' then 'confirmed'
        when 'confirmed' then 'intervening'
        when 'intervening' then 'pending_verification'
        when 'pending_verification' then 'stable'
        else null
    end;

    if v_allowed_target_state is null or p_target_state <> v_allowed_target_state then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_CASE_TRANSITION';
    end if;

    select action.id
      into v_primary_action_id
      from public.learning_case_actions as action
     where action.organization_id = p_organization_id
       and action.student_id = p_student_id
       and action.subject_profile_id = p_subject_profile_id
       and action.case_id = p_case_id
       and action.assignee_assignment_id = p_owner_assignment_id
       and action.assignee_teacher_app_user_id = v_actor_id
       and action.action_role = 'primary'
       and action.status = 'pending'
     for update;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_PRIMARY_ACTION_REQUIRED';
    end if;

    v_committed_at := pg_catalog.clock_timestamp();
    v_new_case_version := v_case_version + 1;
    v_event_id := pg_catalog.gen_random_uuid();

    update public.learning_cases
       set state = p_target_state,
           version = v_new_case_version,
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
        'case_state_transitioned',
        v_actor_id,
        pg_catalog.jsonb_build_object(
            'previous_case_state', v_case_state,
            'case_state', p_target_state,
            'case_version_before', v_case_version,
            'case_version_after', v_new_case_version,
            'primary_action_id', v_primary_action_id
        ),
        v_committed_at
    );

    v_result := pg_catalog.jsonb_build_object(
        'command', 'transition_learning_case_state_v1',
        'operation_id', p_operation_id,
        'responsible_teacher_app_user_id', v_actor_id,
        'owner_assignment_id', p_owner_assignment_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'subject_profile_id', p_subject_profile_id,
        'subject_key', v_subject_key,
        'case_id', p_case_id,
        'previous_case_state', v_case_state,
        'case_state', p_target_state,
        'case_version', v_new_case_version,
        'primary_action_id', v_primary_action_id,
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
        'transition_learning_case_state_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.transition_learning_case_state(
    uuid, uuid, uuid, uuid, uuid, uuid, bigint, text
) from public;
revoke execute on function public.transition_learning_case_state(
    uuid, uuid, uuid, uuid, uuid, uuid, bigint, text
) from anon;
grant execute on function public.transition_learning_case_state(
    uuid, uuid, uuid, uuid, uuid, uuid, bigint, text
) to authenticated;

comment on function public.transition_learning_case_state(
    uuid, uuid, uuid, uuid, uuid, uuid, bigint, text
) is
'TransitionLearningCaseState v1: forward-only open-state transition with live teaching authority, Action lock, optimistic concurrency, event and idempotent receipt.';


create or replace function public.close_learning_case(
    p_operation_id uuid,
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid,
    p_owner_assignment_id uuid,
    p_case_id uuid,
    p_primary_action_id uuid,
    p_expected_case_version bigint,
    p_expected_action_version bigint
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
        'expected_action_version', p_expected_action_version
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
           and v_existing_receipt.command_name = 'close_learning_case_v1'
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

    if v_case_state <> 'stable' then
        raise exception using errcode = 'P0001', message = 'XQ_CASE_NOT_STABLE';
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

    v_committed_at := pg_catalog.clock_timestamp();
    v_new_action_version := v_action_version + 1;
    v_new_case_version := v_case_version + 1;
    v_event_id := pg_catalog.gen_random_uuid();

    update public.learning_case_actions
       set status = 'cancelled',
           version = v_new_action_version,
           updated_at_server = v_committed_at
     where id = p_primary_action_id;

    update public.learning_cases
       set state = 'closed',
           version = v_new_case_version,
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
        'case_closed',
        v_actor_id,
        pg_catalog.jsonb_build_object(
            'previous_case_state', v_case_state,
            'case_state', 'closed',
            'case_version_before', v_case_version,
            'case_version_after', v_new_case_version,
            'cancelled_primary_action_id', p_primary_action_id,
            'action_version_before', v_action_version,
            'action_version_after', v_new_action_version
        ),
        v_committed_at
    );

    v_result := pg_catalog.jsonb_build_object(
        'command', 'close_learning_case_v1',
        'operation_id', p_operation_id,
        'responsible_teacher_app_user_id', v_actor_id,
        'owner_assignment_id', p_owner_assignment_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'subject_profile_id', p_subject_profile_id,
        'subject_key', v_subject_key,
        'case_id', p_case_id,
        'previous_case_state', v_case_state,
        'case_state', 'closed',
        'case_version', v_new_case_version,
        'cancelled_primary_action_id', p_primary_action_id,
        'cancelled_action_version', v_new_action_version,
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
        'close_learning_case_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.close_learning_case(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint
) from public;
revoke execute on function public.close_learning_case(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint
) from anon;
grant execute on function public.close_learning_case(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint
) to authenticated;

comment on function public.close_learning_case(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint
) is
'CloseLearningCase v1: stable-to-closed transition with atomic pending primary Action cancellation, optimistic concurrency, event and idempotent receipt.';


create or replace function public.reopen_learning_case(
    p_operation_id uuid,
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid,
    p_owner_assignment_id uuid,
    p_case_id uuid,
    p_expected_case_version bigint,
    p_new_primary_action_text text,
    p_new_primary_action_due_on date default null
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
    v_new_case_version bigint;
    v_new_action_id uuid;
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
       or p_case_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;

    if p_expected_case_version is null or p_expected_case_version < 1 then
        raise exception using errcode = 'P0001', message = 'XQ_EXPECTED_CASE_VERSION_REQUIRED';
    end if;

    if p_new_primary_action_text is null
       or pg_catalog.char_length(pg_catalog.btrim(p_new_primary_action_text)) < 1
       or pg_catalog.char_length(pg_catalog.btrim(p_new_primary_action_text)) > 1000 then
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
        'expected_case_version', p_expected_case_version,
        'new_primary_action_text', pg_catalog.btrim(p_new_primary_action_text),
        'new_primary_action_due_on', p_new_primary_action_due_on
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
           and v_existing_receipt.command_name = 'reopen_learning_case_v1'
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

    if v_case_state <> 'closed' then
        raise exception using errcode = 'P0001', message = 'XQ_CASE_NOT_CLOSED';
    end if;

    if v_case_version <> p_expected_case_version then
        raise exception using errcode = 'P0001', message = 'XQ_CASE_VERSION_CONFLICT';
    end if;

    perform 1
      from public.learning_case_actions as action
     where action.organization_id = p_organization_id
       and action.student_id = p_student_id
       and action.subject_profile_id = p_subject_profile_id
       and action.case_id = p_case_id
       and action.action_role = 'primary'
       and action.status = 'pending'
     for update;

    if found then
        raise exception using errcode = 'P0001', message = 'XQ_CLOSED_CASE_HAS_PENDING_ACTION';
    end if;

    v_committed_at := pg_catalog.clock_timestamp();
    v_new_case_version := v_case_version + 1;
    v_new_action_id := pg_catalog.gen_random_uuid();
    v_event_id := pg_catalog.gen_random_uuid();

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
        v_new_action_id,
        p_organization_id,
        p_student_id,
        p_subject_profile_id,
        p_case_id,
        p_owner_assignment_id,
        v_actor_id,
        'primary',
        'pending',
        pg_catalog.btrim(p_new_primary_action_text),
        p_new_primary_action_due_on,
        1,
        v_actor_id,
        v_committed_at,
        v_committed_at
    );

    update public.learning_cases
       set state = 'intervening',
           version = v_new_case_version,
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
        'case_reopened',
        v_actor_id,
        pg_catalog.jsonb_build_object(
            'previous_case_state', v_case_state,
            'case_state', 'intervening',
            'case_version_before', v_case_version,
            'case_version_after', v_new_case_version,
            'new_primary_action_id', v_new_action_id,
            'new_action_text', pg_catalog.btrim(p_new_primary_action_text),
            'new_action_due_on', p_new_primary_action_due_on
        ),
        v_committed_at
    );

    v_result := pg_catalog.jsonb_build_object(
        'command', 'reopen_learning_case_v1',
        'operation_id', p_operation_id,
        'responsible_teacher_app_user_id', v_actor_id,
        'owner_assignment_id', p_owner_assignment_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'subject_profile_id', p_subject_profile_id,
        'subject_key', v_subject_key,
        'case_id', p_case_id,
        'previous_case_state', v_case_state,
        'case_state', 'intervening',
        'case_version', v_new_case_version,
        'new_primary_action_id', v_new_action_id,
        'new_action_version', 1,
        'new_action_text', pg_catalog.btrim(p_new_primary_action_text),
        'new_action_due_on', p_new_primary_action_due_on,
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
        'reopen_learning_case_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.reopen_learning_case(
    uuid, uuid, uuid, uuid, uuid, uuid, bigint, text, date
) from public;
revoke execute on function public.reopen_learning_case(
    uuid, uuid, uuid, uuid, uuid, uuid, bigint, text, date
) from anon;
grant execute on function public.reopen_learning_case(
    uuid, uuid, uuid, uuid, uuid, uuid, bigint, text, date
) to authenticated;

comment on function public.reopen_learning_case(
    uuid, uuid, uuid, uuid, uuid, uuid, bigint, text, date
) is
'ReopenLearningCase v1: closed-to-intervening reopen event with one new pending primary Action, live teaching authority, optimistic concurrency and idempotent receipt.';
