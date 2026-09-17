-- Phase 1 minimal backend: identity/teaching-scope records plus CreateObservation v1.
-- This is intentionally not the full Learning Case / Evidence / Assessment / Action model.

create table public.app_users (
    id uuid primary key,
    auth_subject uuid not null unique,
    display_name text not null check (char_length(btrim(display_name)) between 1 and 200),
    enabled boolean not null default true,
    created_at timestamptz not null default clock_timestamp()
);

create table public.organizations (
    id uuid primary key,
    name text not null check (char_length(btrim(name)) between 1 and 200),
    created_at timestamptz not null default clock_timestamp()
);

create table public.memberships (
    organization_id uuid not null references public.organizations(id) on delete restrict,
    app_user_id uuid not null references public.app_users(id) on delete restrict,
    membership_role text not null check (membership_role in ('owner', 'admin', 'teacher')),
    status text not null check (status in ('active', 'disabled')),
    can_teach boolean not null default false,
    created_at timestamptz not null default clock_timestamp(),
    primary key (organization_id, app_user_id)
);

create table public.students (
    id uuid primary key,
    organization_id uuid not null references public.organizations(id) on delete restrict,
    display_name text not null check (char_length(btrim(display_name)) between 1 and 200),
    active boolean not null default true,
    created_at timestamptz not null default clock_timestamp(),
    unique (organization_id, id)
);

create table public.student_subject_profiles (
    id uuid primary key,
    organization_id uuid not null,
    student_id uuid not null,
    subject_key text not null check (char_length(btrim(subject_key)) between 1 and 100),
    active boolean not null default true,
    created_at timestamptz not null default clock_timestamp(),
    foreign key (organization_id, student_id)
        references public.students(organization_id, id) on delete restrict,
    unique (organization_id, student_id, id),
    unique (organization_id, student_id, subject_key)
);

create table public.student_teacher_assignments (
    id uuid primary key,
    organization_id uuid not null,
    student_id uuid not null,
    subject_profile_id uuid not null,
    teacher_app_user_id uuid not null,
    active boolean not null default true,
    created_at timestamptz not null default clock_timestamp(),
    foreign key (organization_id, student_id, subject_profile_id)
        references public.student_subject_profiles(organization_id, student_id, id) on delete restrict,
    foreign key (organization_id, teacher_app_user_id)
        references public.memberships(organization_id, app_user_id) on delete restrict,
    unique (organization_id, student_id, subject_profile_id, id)
);

create unique index student_teacher_assignments_one_active_actor_scope
    on public.student_teacher_assignments (
        organization_id,
        student_id,
        subject_profile_id,
        teacher_app_user_id
    )
    where active;

create table public.observations (
    id uuid primary key,
    operation_id uuid not null unique,
    organization_id uuid not null,
    student_id uuid not null,
    subject_profile_id uuid not null,
    assignment_id uuid not null,
    actor_app_user_id uuid not null references public.app_users(id) on delete restrict,
    raw_text text not null check (char_length(btrim(raw_text)) between 1 and 10000),
    client_captured_at timestamptz,
    client_capture_metadata jsonb not null default '{}'::jsonb
        check (jsonb_typeof(client_capture_metadata) = 'object'),
    created_at_server timestamptz not null default clock_timestamp(),
    foreign key (organization_id, student_id, subject_profile_id, assignment_id)
        references public.student_teacher_assignments(
            organization_id,
            student_id,
            subject_profile_id,
            id
        ) on delete restrict
);

create table public.operation_receipts (
    operation_id uuid primary key,
    actor_app_user_id uuid not null references public.app_users(id) on delete restrict,
    command_name text not null,
    request_payload jsonb not null check (jsonb_typeof(request_payload) = 'object'),
    result_payload jsonb not null check (jsonb_typeof(result_payload) = 'object'),
    committed_at timestamptz not null default clock_timestamp()
);

-- Public is an exposed Data API schema. Every table is protected by BOTH grants
-- and RLS. V1 exposes no direct table CRUD at all; clients use the command RPC.
alter table public.app_users enable row level security;
alter table public.organizations enable row level security;
alter table public.memberships enable row level security;
alter table public.students enable row level security;
alter table public.student_subject_profiles enable row level security;
alter table public.student_teacher_assignments enable row level security;
alter table public.observations enable row level security;
alter table public.operation_receipts enable row level security;

revoke all on table public.app_users from anon, authenticated;
revoke all on table public.organizations from anon, authenticated;
revoke all on table public.memberships from anon, authenticated;
revoke all on table public.students from anon, authenticated;
revoke all on table public.student_subject_profiles from anon, authenticated;
revoke all on table public.student_teacher_assignments from anon, authenticated;
revoke all on table public.observations from anon, authenticated;
revoke all on table public.operation_receipts from anon, authenticated;

-- CreateObservation is the one intentional SECURITY DEFINER exception in this
-- slice. It must atomically re-read authorization/assignment rows that are not
-- directly exposed to clients, then append Observation + receipt in one DB
-- transaction. Every relation/function is schema-qualified because search_path
-- is empty, and execute is restricted below.
create or replace function public.create_observation(
    p_operation_id uuid,
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid,
    p_assignment_id uuid,
    p_raw_text text,
    p_client_captured_at timestamptz default null,
    p_client_capture_metadata jsonb default '{}'::jsonb
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_auth_subject uuid;
    v_actor_id uuid;
    v_actor_enabled boolean;
    v_membership_status text;
    v_can_teach boolean;
    v_subject_key text;
    v_request_payload jsonb;
    v_existing_receipt public.operation_receipts%rowtype;
    v_observation_id uuid;
    v_committed_at timestamptz;
    v_result jsonb;
begin
    if p_operation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_ID_REQUIRED';
    end if;
    if p_organization_id is null or p_student_id is null or p_subject_profile_id is null or p_assignment_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;
    if p_raw_text is null or pg_catalog.char_length(pg_catalog.btrim(p_raw_text)) < 1
        or pg_catalog.char_length(pg_catalog.btrim(p_raw_text)) > 10000 then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_OBSERVATION_TEXT';
    end if;
    if p_client_capture_metadata is null or pg_catalog.jsonb_typeof(p_client_capture_metadata) <> 'object' then
        raise exception using errcode = 'P0001', message = 'XQ_INVALID_CAPTURE_METADATA';
    end if;

    v_auth_subject := auth.uid();
    if v_auth_subject is null then
        raise exception using errcode = 'P0001', message = 'XQ_AUTH_REQUIRED';
    end if;

    -- Live authority rows use FOR SHARE, not FOR KEY SHARE. Revoking enabled,
    -- membership status/capability, active Student/Profile, or assignment must
    -- conflict with an in-flight teaching-fact command before it commits.
    select app_user.id, app_user.enabled
      into v_actor_id, v_actor_enabled
      from public.app_users as app_user
     where app_user.auth_subject = v_auth_subject
     for share;

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
        'raw_text', p_raw_text,
        'client_captured_at', p_client_captured_at,
        'client_capture_metadata', p_client_capture_metadata
    );

    -- One operation_id has exactly one serialization lane. The 64-bit hash may
    -- serialize unrelated operations on a rare collision, which is safe; it can
    -- never allow duplicate side effects.
    perform pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended(p_operation_id::text, 0)
    );

    select receipt.*
      into v_existing_receipt
      from public.operation_receipts as receipt
     where receipt.operation_id = p_operation_id;

    if found then
        if v_existing_receipt.actor_app_user_id = v_actor_id
           and v_existing_receipt.command_name = 'create_observation_v1'
           and v_existing_receipt.request_payload = v_request_payload then
            return v_existing_receipt.result_payload;
        end if;
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD';
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
       and assignment.id = p_assignment_id
       and assignment.teacher_app_user_id = v_actor_id
       and assignment.active
     for share;
    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHER_ASSIGNMENT_REQUIRED';
    end if;

    v_observation_id := pg_catalog.gen_random_uuid();
    v_committed_at := pg_catalog.clock_timestamp();

    insert into public.observations (
        id,
        operation_id,
        organization_id,
        student_id,
        subject_profile_id,
        assignment_id,
        actor_app_user_id,
        raw_text,
        client_captured_at,
        client_capture_metadata,
        created_at_server
    ) values (
        v_observation_id,
        p_operation_id,
        p_organization_id,
        p_student_id,
        p_subject_profile_id,
        p_assignment_id,
        v_actor_id,
        p_raw_text,
        p_client_captured_at,
        p_client_capture_metadata,
        v_committed_at
    );

    v_result := pg_catalog.jsonb_build_object(
        'command', 'create_observation_v1',
        'operation_id', p_operation_id,
        'observation_id', v_observation_id,
        'actor_app_user_id', v_actor_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'subject_profile_id', p_subject_profile_id,
        'subject_key', v_subject_key,
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
        'create_observation_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.create_observation(uuid, uuid, uuid, uuid, uuid, text, timestamptz, jsonb) from public;
revoke execute on function public.create_observation(uuid, uuid, uuid, uuid, uuid, text, timestamptz, jsonb) from anon;
grant execute on function public.create_observation(uuid, uuid, uuid, uuid, uuid, text, timestamptz, jsonb) to authenticated;

comment on function public.create_observation(uuid, uuid, uuid, uuid, uuid, text, timestamptz, jsonb)
is 'CreateObservation v1: authoritative Teaching Fact Gate + append-only Observation + idempotent operation receipt.';
