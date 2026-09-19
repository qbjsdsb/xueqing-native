-- Phase 1 Learning Case + Primary Action vertical slice.
-- Establishes the first formal Case invariant without introducing lifecycle
-- transitions, Evidence/Intervention/Assessment mutations, or offline queuing.

alter table public.observations
    add constraint observations_scope_id_unique
    unique (organization_id, student_id, subject_profile_id, id);

alter table public.student_teacher_assignments
    add constraint student_teacher_assignments_scope_teacher_unique
    unique (
        organization_id,
        student_id,
        subject_profile_id,
        id,
        teacher_app_user_id
    );

create table public.learning_cases (
    id uuid primary key,
    organization_id uuid not null,
    student_id uuid not null,
    subject_profile_id uuid not null,
    owner_assignment_id uuid not null,
    responsible_teacher_app_user_id uuid not null,
    title text not null
        check (pg_catalog.char_length(pg_catalog.btrim(title)) between 1 and 500),
    state text not null
        check (state in ('new', 'confirmed', 'intervening', 'pending_verification', 'stable', 'closed')),
    version bigint not null default 1 check (version >= 1),
    created_by_actor_app_user_id uuid not null
        references public.app_users(id) on delete restrict,
    created_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    updated_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    foreign key (
        organization_id,
        student_id,
        subject_profile_id,
        owner_assignment_id,
        responsible_teacher_app_user_id
    ) references public.student_teacher_assignments (
        organization_id,
        student_id,
        subject_profile_id,
        id,
        teacher_app_user_id
    ) on delete restrict,
    unique (organization_id, student_id, subject_profile_id, id)
);

create table public.learning_case_actions (
    id uuid primary key,
    organization_id uuid not null,
    student_id uuid not null,
    subject_profile_id uuid not null,
    case_id uuid not null,
    assignee_assignment_id uuid not null,
    assignee_teacher_app_user_id uuid not null,
    action_role text not null check (action_role in ('primary')),
    status text not null check (status in ('pending', 'completed', 'cancelled')),
    action_text text not null
        check (pg_catalog.char_length(pg_catalog.btrim(action_text)) between 1 and 1000),
    due_on date,
    version bigint not null default 1 check (version >= 1),
    created_by_actor_app_user_id uuid not null
        references public.app_users(id) on delete restrict,
    created_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    updated_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    completed_at_server timestamptz,
    foreign key (organization_id, student_id, subject_profile_id, case_id)
        references public.learning_cases(
            organization_id,
            student_id,
            subject_profile_id,
            id
        ) on delete restrict,
    foreign key (
        organization_id,
        student_id,
        subject_profile_id,
        assignee_assignment_id,
        assignee_teacher_app_user_id
    ) references public.student_teacher_assignments (
        organization_id,
        student_id,
        subject_profile_id,
        id,
        teacher_app_user_id
    ) on delete restrict
);

create unique index learning_case_one_pending_primary_action
    on public.learning_case_actions (case_id)
    where action_role = 'primary' and status = 'pending';

create table public.learning_case_events (
    id uuid primary key,
    operation_id uuid not null,
    organization_id uuid not null,
    student_id uuid not null,
    subject_profile_id uuid not null,
    case_id uuid not null,
    event_type text not null check (event_type in ('case_created')),
    actor_app_user_id uuid not null references public.app_users(id) on delete restrict,
    event_payload jsonb not null default '{}'::jsonb
        check (pg_catalog.jsonb_typeof(event_payload) = 'object'),
    occurred_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    foreign key (organization_id, student_id, subject_profile_id, case_id)
        references public.learning_cases(
            organization_id,
            student_id,
            subject_profile_id,
            id
        ) on delete restrict,
    unique (operation_id, event_type, case_id)
);

create table public.learning_case_observation_links (
    organization_id uuid not null,
    student_id uuid not null,
    subject_profile_id uuid not null,
    case_id uuid not null,
    observation_id uuid not null,
    operation_id uuid not null,
    linked_by_actor_app_user_id uuid not null
        references public.app_users(id) on delete restrict,
    linked_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    primary key (case_id, observation_id),
    foreign key (organization_id, student_id, subject_profile_id, case_id)
        references public.learning_cases(
            organization_id,
            student_id,
            subject_profile_id,
            id
        ) on delete restrict,
    foreign key (organization_id, student_id, subject_profile_id, observation_id)
        references public.observations(
            organization_id,
            student_id,
            subject_profile_id,
            id
        ) on delete restrict
);

alter table public.learning_cases enable row level security;
alter table public.learning_case_actions enable row level security;
alter table public.learning_case_events enable row level security;
alter table public.learning_case_observation_links enable row level security;

revoke all on table public.learning_cases from anon, authenticated;
revoke all on table public.learning_case_actions from anon, authenticated;
revoke all on table public.learning_case_events from anon, authenticated;
revoke all on table public.learning_case_observation_links from anon, authenticated;

create or replace function public.create_learning_case(
    p_operation_id uuid,
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid,
    p_owner_assignment_id uuid,
    p_title text,
    p_primary_action_text text,
    p_primary_action_due_on date default null,
    p_source_observation_id uuid default null
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
    v_case_id uuid;
    v_action_id uuid;
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
       or p_owner_assignment_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;

    if p_title is null
       or pg_catalog.char_length(pg_catalog.btrim(p_title)) < 1
       or pg_catalog.char_length(pg_catalog.btrim(p_title)) > 500 then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_CASE_TITLE';
    end if;

    if p_primary_action_text is null
       or pg_catalog.char_length(pg_catalog.btrim(p_primary_action_text)) < 1
       or pg_catalog.char_length(pg_catalog.btrim(p_primary_action_text)) > 1000 then
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
        'title', pg_catalog.btrim(p_title),
        'primary_action_text', pg_catalog.btrim(p_primary_action_text),
        'primary_action_due_on', p_primary_action_due_on,
        'source_observation_id', p_source_observation_id
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
           and v_existing_receipt.command_name = 'create_learning_case_v1'
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

    if p_source_observation_id is not null then
        perform 1
          from public.observations as observation
         where observation.organization_id = p_organization_id
           and observation.student_id = p_student_id
           and observation.subject_profile_id = p_subject_profile_id
           and observation.id = p_source_observation_id
         for share;

        if not found then
            raise exception using
                errcode = 'P0001',
                message = 'XQ_SOURCE_OBSERVATION_SCOPE_MISMATCH';
        end if;
    end if;

    v_case_id := pg_catalog.gen_random_uuid();
    v_action_id := pg_catalog.gen_random_uuid();
    v_event_id := pg_catalog.gen_random_uuid();
    v_committed_at := pg_catalog.clock_timestamp();

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
        v_case_id,
        p_organization_id,
        p_student_id,
        p_subject_profile_id,
        p_owner_assignment_id,
        v_actor_id,
        pg_catalog.btrim(p_title),
        'new',
        1,
        v_actor_id,
        v_committed_at,
        v_committed_at
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
        v_action_id,
        p_organization_id,
        p_student_id,
        p_subject_profile_id,
        v_case_id,
        p_owner_assignment_id,
        v_actor_id,
        'primary',
        'pending',
        pg_catalog.btrim(p_primary_action_text),
        p_primary_action_due_on,
        1,
        v_actor_id,
        v_committed_at,
        v_committed_at
    );

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
        v_case_id,
        'case_created',
        v_actor_id,
        pg_catalog.jsonb_build_object(
            'case_state', 'new',
            'case_version', 1,
            'title', pg_catalog.btrim(p_title),
            'owner_assignment_id', p_owner_assignment_id,
            'responsible_teacher_app_user_id', v_actor_id,
            'primary_action_id', v_action_id,
            'primary_action_text', pg_catalog.btrim(p_primary_action_text),
            'primary_action_due_on', p_primary_action_due_on,
            'source_observation_id', p_source_observation_id
        ),
        v_committed_at
    );

    if p_source_observation_id is not null then
        insert into public.learning_case_observation_links (
            organization_id,
            student_id,
            subject_profile_id,
            case_id,
            observation_id,
            operation_id,
            linked_by_actor_app_user_id,
            linked_at_server
        ) values (
            p_organization_id,
            p_student_id,
            p_subject_profile_id,
            v_case_id,
            p_source_observation_id,
            p_operation_id,
            v_actor_id,
            v_committed_at
        );
    end if;

    v_result := pg_catalog.jsonb_build_object(
        'command', 'create_learning_case_v1',
        'operation_id', p_operation_id,
        'case_id', v_case_id,
        'case_state', 'new',
        'case_version', 1,
        'primary_action_id', v_action_id,
        'case_event_id', v_event_id,
        'responsible_teacher_app_user_id', v_actor_id,
        'owner_assignment_id', p_owner_assignment_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'subject_profile_id', p_subject_profile_id,
        'subject_key', v_subject_key,
        'source_observation_id', p_source_observation_id,
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
        'create_learning_case_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.create_learning_case(
    uuid, uuid, uuid, uuid, uuid, text, text, date, uuid
) from public;
revoke execute on function public.create_learning_case(
    uuid, uuid, uuid, uuid, uuid, text, text, date, uuid
) from anon;
grant execute on function public.create_learning_case(
    uuid, uuid, uuid, uuid, uuid, text, text, date, uuid
) to authenticated;

comment on function public.create_learning_case(
    uuid, uuid, uuid, uuid, uuid, text, text, date, uuid
) is
'CreateLearningCase v1: live teaching authority + atomic new Case, pending primary Action, provenance event, optional Observation link and idempotent receipt.';
