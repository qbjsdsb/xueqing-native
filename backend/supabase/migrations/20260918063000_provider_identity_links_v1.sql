-- Provider Adapter Conformance v1.
--
-- The application-owned AppUser is the durable business identity. Supabase Auth
-- is the current reference-provider adapter only. Business projections/commands
-- must resolve the external (provider, issuer, subject) tuple through an
-- IdentityLink and must never treat auth.uid() as the AppUser primary key.
--
-- app_users.auth_subject is retained temporarily as a pre-production migration
-- compatibility column. It is deliberately no longer consulted by any business
-- projection or command after this migration.

create table public.identity_links (
    id uuid primary key default pg_catalog.gen_random_uuid(),
    app_user_id uuid not null references public.app_users(id) on delete restrict,
    provider_key text not null check (char_length(btrim(provider_key)) between 1 and 100),
    issuer text not null check (char_length(btrim(issuer)) between 1 and 1000),
    external_subject text not null check (char_length(btrim(external_subject)) between 1 and 1000),
    active boolean not null default true,
    created_at timestamptz not null default pg_catalog.clock_timestamp(),
    unique (provider_key, issuer, external_subject)
);

create index identity_links_by_app_user
    on public.identity_links (app_user_id, provider_key, issuer);

alter table public.identity_links enable row level security;
revoke all on table public.identity_links from anon, authenticated;

create schema if not exists xq_internal;
revoke all on schema xq_internal from public;
revoke all on schema xq_internal from anon;
revoke all on schema xq_internal from authenticated;

-- Reference-provider boundary. Only this internal adapter knows that the current
-- deployment authenticates through Supabase JWTs. Product functions consume
-- provider-neutral strings returned here.
create or replace function xq_internal.current_external_identity_v1()
returns table (
    provider_key text,
    issuer text,
    external_subject text
)
language sql
stable
set search_path = ''
as $$
    select
        'supabase'::text,
        nullif(auth.jwt() ->> 'iss', ''),
        nullif(auth.jwt() ->> 'sub', '')
    where nullif(auth.jwt() ->> 'sub', '') is not null;
$$;

revoke all on function xq_internal.current_external_identity_v1() from public;
revoke all on function xq_internal.current_external_identity_v1() from anon;
revoke all on function xq_internal.current_external_identity_v1() from authenticated;

comment on table public.identity_links is
'Provider-neutral links from external identity tuples to application-owned AppUser identities.';

comment on function xq_internal.current_external_identity_v1() is
'Reference-provider adapter: extracts the current Supabase (provider, issuer, subject) tuple without exposing provider identity as business identity.';

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

    select identity.provider_key, identity.issuer, identity.external_subject
      into v_provider_key, v_issuer, v_external_subject
      from xq_internal.current_external_identity_v1() as identity;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_AUTH_REQUIRED';
    end if;

    -- Lock both the live identity mapping and AppUser authority row. Relinking,
    -- disabling the link, or disabling the AppUser must conflict with an
    -- in-flight teaching-fact write before it can commit.
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
        'raw_text', p_raw_text,
        'client_captured_at', p_client_captured_at,
        'client_capture_metadata', p_client_capture_metadata
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

create or replace function public.get_personal_bootstrap_v1()
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
    v_actor_display_name text;
    v_actor_enabled boolean;
    v_generated_at timestamptz;
    v_organizations jsonb;
    v_teaching_contexts jsonb;
begin
    select identity.provider_key, identity.issuer, identity.external_subject
      into v_provider_key, v_issuer, v_external_subject
      from xq_internal.current_external_identity_v1() as identity;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_AUTH_REQUIRED';
    end if;

    select app_user.id, app_user.display_name, app_user.enabled
      into v_actor_id, v_actor_display_name, v_actor_enabled
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

    v_generated_at := pg_catalog.clock_timestamp();

    select coalesce(
        pg_catalog.jsonb_agg(
            pg_catalog.jsonb_build_object(
                'organization_id', organization.id,
                'name', organization.name,
                'can_teach', membership.can_teach
            )
            order by organization.name, organization.id
        ),
        '[]'::jsonb
    )
      into v_organizations
      from public.memberships as membership
      join public.organizations as organization
        on organization.id = membership.organization_id
     where membership.app_user_id = v_actor_id
       and membership.status = 'active';

    select coalesce(
        pg_catalog.jsonb_agg(
            pg_catalog.jsonb_build_object(
                'organization_id', assignment.organization_id,
                'student_id', student.id,
                'student_display_name', student.display_name,
                'subject_profile_id', profile.id,
                'subject_key', profile.subject_key,
                'assignment_id', assignment.id
            )
            order by assignment.organization_id, student.display_name, profile.subject_key, assignment.id
        ),
        '[]'::jsonb
    )
      into v_teaching_contexts
      from public.student_teacher_assignments as assignment
      join public.memberships as membership
        on membership.organization_id = assignment.organization_id
       and membership.app_user_id = assignment.teacher_app_user_id
      join public.students as student
        on student.organization_id = assignment.organization_id
       and student.id = assignment.student_id
      join public.student_subject_profiles as profile
        on profile.organization_id = assignment.organization_id
       and profile.student_id = assignment.student_id
       and profile.id = assignment.subject_profile_id
     where assignment.teacher_app_user_id = v_actor_id
       and assignment.active
       and membership.status = 'active'
       and membership.can_teach
       and student.active
       and profile.active;

    return pg_catalog.jsonb_build_object(
        'contract', 'personal_bootstrap_v1',
        'generated_at_server', v_generated_at,
        'actor', pg_catalog.jsonb_build_object(
            'app_user_id', v_actor_id,
            'display_name', v_actor_display_name
        ),
        'organizations', v_organizations,
        'teaching_contexts', v_teaching_contexts
    );
end;
$$;

create or replace function public.get_student_recent_observations_v1(
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
    v_items jsonb;
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
    if p_organization_id is null or p_student_id is null or p_subject_profile_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;

    select assignment.id as assignment_id, student.display_name, profile.subject_key
      into v_context
      from public.student_teacher_assignments as assignment
      join public.memberships as membership
        on membership.organization_id = assignment.organization_id
       and membership.app_user_id = assignment.teacher_app_user_id
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

    with bounded as materialized (
        select observation.id, observation.actor_app_user_id,
               observation.raw_text, observation.client_captured_at,
               observation.created_at_server
          from public.observations as observation
         where observation.organization_id = p_organization_id
           and observation.student_id = p_student_id
           and observation.subject_profile_id = p_subject_profile_id
         order by observation.created_at_server desc, observation.id desc
         limit 21
    ), visible as (
        select * from bounded
         order by created_at_server desc, id desc
         limit 20
    )
    select coalesce(
               (select pg_catalog.jsonb_agg(
                   pg_catalog.jsonb_build_object(
                       'observation_id', item.id,
                       'actor_app_user_id', item.actor_app_user_id,
                       'raw_text', item.raw_text,
                       'client_captured_at', item.client_captured_at,
                       'created_at_server', item.created_at_server
                   ) order by item.created_at_server desc, item.id desc
               ) from visible as item),
               '[]'::jsonb
           ),
           (select pg_catalog.count(*) > 20 from bounded)
      into v_items, v_has_more;

    return pg_catalog.jsonb_build_object(
        'contract', 'student_recent_observations_v1',
        'generated_at_server', pg_catalog.statement_timestamp(),
        'actor_app_user_id', v_actor_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'student_display_name', v_context.display_name,
        'subject_profile_id', p_subject_profile_id,
        'subject_key', v_context.subject_key,
        'assignment_id', v_context.assignment_id,
        'observations', v_items,
        'has_more', v_has_more
    );
end;
$$;

comment on function public.create_observation(uuid, uuid, uuid, uuid, uuid, text, timestamptz, jsonb)
is 'CreateObservation v1: resolves provider identity through IdentityLink, then applies live Teaching Fact Gate and appends Observation + idempotent receipt.';

comment on function public.get_personal_bootstrap_v1()
is 'PersonalBootstrap v1: provider-neutral AppUser resolution plus current active organizations and teaching contexts. Snapshot only; never authorization proof.';

comment on function public.get_student_recent_observations_v1(uuid, uuid, uuid)
is 'Personal teaching scope: provider-neutral AppUser resolution plus latest 20 authoritative Observations for one active assigned Student Subject Profile.';
