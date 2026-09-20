-- Observation Attachment v1.
--
-- Bytes live in a private Supabase Storage bucket. Xueqing domain visibility
-- begins only after CommitObservationAttachment appends immutable application
-- metadata + operation receipt.
--
-- Storage object writes must use the Storage API. This migration only creates
-- bucket configuration and RLS policies; it never writes storage.objects rows.

insert into storage.buckets (
    id,
    name,
    public,
    file_size_limit,
    allowed_mime_types
) values (
    'teaching-attachments-v1',
    'teaching-attachments-v1',
    false,
    6291456,
    array['image/jpeg', 'image/png', 'image/webp']::text[]
)
on conflict (id) do update
set public = false,
    file_size_limit = excluded.file_size_limit,
    allowed_mime_types = excluded.allowed_mime_types;

create unique index if not exists observations_attachment_scope_identity
    on public.observations (
        organization_id,
        student_id,
        subject_profile_id,
        assignment_id,
        id,
        actor_app_user_id
    );

create table public.observation_attachments (
    id uuid primary key,
    operation_id uuid not null unique,
    organization_id uuid not null,
    student_id uuid not null,
    subject_profile_id uuid not null,
    assignment_id uuid not null,
    observation_id uuid not null,
    actor_app_user_id uuid not null,
    bucket_id text not null
        check (bucket_id = 'teaching-attachments-v1'),
    object_name text not null unique
        check (char_length(object_name) between 1 and 1000),
    content_type text not null
        check (content_type in ('image/jpeg', 'image/png', 'image/webp')),
    byte_size bigint not null
        check (byte_size between 1 and 6291456),
    created_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    foreign key (
        organization_id,
        student_id,
        subject_profile_id,
        assignment_id,
        observation_id,
        actor_app_user_id
    ) references public.observations (
        organization_id,
        student_id,
        subject_profile_id,
        assignment_id,
        id,
        actor_app_user_id
    ) on delete restrict
);

create index observation_attachments_by_observation
    on public.observation_attachments (
        organization_id,
        student_id,
        subject_profile_id,
        observation_id,
        created_at_server,
        id
    );

alter table public.observation_attachments enable row level security;
revoke all on table public.observation_attachments from public, anon, authenticated;

create or replace function xq_internal.observation_attachment_object_name_v1(
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid,
    p_observation_id uuid,
    p_attachment_id uuid
)
returns text
language sql
immutable
strict
set search_path = ''
as $$
    select
        'v1/org/' || p_organization_id::text ||
        '/student/' || p_student_id::text ||
        '/profile/' || p_subject_profile_id::text ||
        '/observation/' || p_observation_id::text ||
        '/attachment/' || p_attachment_id::text;
$$;

revoke all on function xq_internal.observation_attachment_object_name_v1(
    uuid, uuid, uuid, uuid, uuid
) from public, anon, authenticated;

create or replace function xq_internal.can_upload_observation_attachment_v1(
    p_object_name text
)
returns boolean
language plpgsql
volatile
security definer
set search_path = ''
as $$
declare
    v_parts text[];
    v_organization_id uuid;
    v_student_id uuid;
    v_subject_profile_id uuid;
    v_observation_id uuid;
    v_attachment_id uuid;
    v_provider_key text;
    v_issuer text;
    v_external_subject text;
    v_actor_id uuid;
    v_actor_enabled boolean;
begin
    if p_object_name is null then
        return false;
    end if;

    v_parts := pg_catalog.string_to_array(p_object_name, '/');
    if pg_catalog.array_length(v_parts, 1) <> 11
       or v_parts[1] <> 'v1'
       or v_parts[2] <> 'org'
       or v_parts[4] <> 'student'
       or v_parts[6] <> 'profile'
       or v_parts[8] <> 'observation'
       or v_parts[10] <> 'attachment' then
        return false;
    end if;

    begin
        v_organization_id := v_parts[3]::uuid;
        v_student_id := v_parts[5]::uuid;
        v_subject_profile_id := v_parts[7]::uuid;
        v_observation_id := v_parts[9]::uuid;
        v_attachment_id := v_parts[11]::uuid;
    exception
        when invalid_text_representation then
            return false;
    end;

    if v_attachment_id is null then
        return false;
    end if;

    select identity.provider_key, identity.issuer, identity.external_subject
      into v_provider_key, v_issuer, v_external_subject
      from xq_internal.current_external_identity_v1() as identity;

    if not found then
        return false;
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

    if not found or not v_actor_enabled then
        return false;
    end if;

    perform 1
      from public.observations as observation
      join public.student_teacher_assignments as assignment
        on assignment.organization_id = observation.organization_id
       and assignment.student_id = observation.student_id
       and assignment.subject_profile_id = observation.subject_profile_id
       and assignment.id = observation.assignment_id
      join public.memberships as membership
        on membership.organization_id = assignment.organization_id
       and membership.app_user_id = assignment.teacher_app_user_id
      join public.students as student
        on student.organization_id = observation.organization_id
       and student.id = observation.student_id
      join public.student_subject_profiles as profile
        on profile.organization_id = observation.organization_id
       and profile.student_id = observation.student_id
       and profile.id = observation.subject_profile_id
     where observation.organization_id = v_organization_id
       and observation.student_id = v_student_id
       and observation.subject_profile_id = v_subject_profile_id
       and observation.id = v_observation_id
       and observation.actor_app_user_id = v_actor_id
       and assignment.teacher_app_user_id = v_actor_id
       and assignment.active
       and membership.status = 'active'
       and membership.can_teach
       and student.active
       and profile.active
     for share of observation, assignment, membership, student, profile;

    return found;
end;
$$;

revoke all on function xq_internal.can_upload_observation_attachment_v1(text)
from public, anon, authenticated;
grant usage on schema xq_internal to authenticated;
grant execute on function xq_internal.can_upload_observation_attachment_v1(text)
to authenticated;

create or replace function xq_internal.can_read_observation_attachment_v1(
    p_object_name text
)
returns boolean
language plpgsql
stable
security definer
set search_path = ''
as $$
declare
    v_parts text[];
    v_organization_id uuid;
    v_student_id uuid;
    v_subject_profile_id uuid;
    v_observation_id uuid;
    v_attachment_id uuid;
    v_provider_key text;
    v_issuer text;
    v_external_subject text;
    v_actor_id uuid;
    v_actor_enabled boolean;
begin
    if p_object_name is null then
        return false;
    end if;

    v_parts := pg_catalog.string_to_array(p_object_name, '/');
    if pg_catalog.array_length(v_parts, 1) <> 11
       or v_parts[1] <> 'v1'
       or v_parts[2] <> 'org'
       or v_parts[4] <> 'student'
       or v_parts[6] <> 'profile'
       or v_parts[8] <> 'observation'
       or v_parts[10] <> 'attachment' then
        return false;
    end if;

    begin
        v_organization_id := v_parts[3]::uuid;
        v_student_id := v_parts[5]::uuid;
        v_subject_profile_id := v_parts[7]::uuid;
        v_observation_id := v_parts[9]::uuid;
        v_attachment_id := v_parts[11]::uuid;
    exception
        when invalid_text_representation then
            return false;
    end;

    select identity.provider_key, identity.issuer, identity.external_subject
      into v_provider_key, v_issuer, v_external_subject
      from xq_internal.current_external_identity_v1() as identity;

    if not found then
        return false;
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

    if not found or not v_actor_enabled then
        return false;
    end if;

    return exists (
        select 1
          from public.observation_attachments as attachment
          join public.student_teacher_assignments as assignment
            on assignment.organization_id = attachment.organization_id
           and assignment.student_id = attachment.student_id
           and assignment.subject_profile_id = attachment.subject_profile_id
           and assignment.teacher_app_user_id = v_actor_id
          join public.memberships as membership
            on membership.organization_id = assignment.organization_id
           and membership.app_user_id = assignment.teacher_app_user_id
          join public.students as student
            on student.organization_id = attachment.organization_id
           and student.id = attachment.student_id
          join public.student_subject_profiles as profile
            on profile.organization_id = attachment.organization_id
           and profile.student_id = attachment.student_id
           and profile.id = attachment.subject_profile_id
         where attachment.organization_id = v_organization_id
           and attachment.student_id = v_student_id
           and attachment.subject_profile_id = v_subject_profile_id
           and attachment.observation_id = v_observation_id
           and attachment.id = v_attachment_id
           and attachment.object_name = p_object_name
           and assignment.active
           and membership.status = 'active'
           and membership.can_teach
           and student.active
           and profile.active
    );
end;
$$;

revoke all on function xq_internal.can_read_observation_attachment_v1(text)
from public, anon, authenticated;
grant execute on function xq_internal.can_read_observation_attachment_v1(text)
to authenticated;

drop policy if exists xq_observation_attachment_insert_v1 on storage.objects;
create policy xq_observation_attachment_insert_v1
on storage.objects
for insert
to authenticated
with check (
    bucket_id = 'teaching-attachments-v1'
    and xq_internal.can_upload_observation_attachment_v1(name)
);

drop policy if exists xq_observation_attachment_select_v1 on storage.objects;
create policy xq_observation_attachment_select_v1
on storage.objects
for select
to authenticated
using (
    bucket_id = 'teaching-attachments-v1'
    and xq_internal.can_read_observation_attachment_v1(name)
);

create or replace function public.commit_observation_attachment(
    p_operation_id uuid,
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid,
    p_assignment_id uuid,
    p_observation_id uuid,
    p_attachment_id uuid
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
    v_object_name text;
    v_storage_metadata jsonb;
    v_content_type text;
    v_size_text text;
    v_byte_size bigint;
    v_committed_at timestamptz;
    v_result jsonb;
begin
    if p_operation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_ID_REQUIRED';
    end if;

    if p_organization_id is null
       or p_student_id is null
       or p_subject_profile_id is null
       or p_assignment_id is null
       or p_observation_id is null
       or p_attachment_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_ATTACHMENT_CONTEXT_REQUIRED';
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
        'observation_id', p_observation_id,
        'attachment_id', p_attachment_id
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
           and v_existing_receipt.command_name = 'commit_observation_attachment_v1'
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
       and assignment.id = p_assignment_id
       and assignment.teacher_app_user_id = v_actor_id
       and assignment.active
     for share;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHER_ASSIGNMENT_REQUIRED';
    end if;

    perform 1
      from public.observations as observation
     where observation.organization_id = p_organization_id
       and observation.student_id = p_student_id
       and observation.subject_profile_id = p_subject_profile_id
       and observation.assignment_id = p_assignment_id
       and observation.id = p_observation_id
       and observation.actor_app_user_id = v_actor_id
     for share;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_OBSERVATION_ATTACHMENT_PARENT_REQUIRED';
    end if;

    v_object_name := xq_internal.observation_attachment_object_name_v1(
        p_organization_id,
        p_student_id,
        p_subject_profile_id,
        p_observation_id,
        p_attachment_id
    );

    select object.metadata
      into v_storage_metadata
      from storage.objects as object
     where object.bucket_id = 'teaching-attachments-v1'
       and object.name = v_object_name
     for share;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_ATTACHMENT_OBJECT_REQUIRED';
    end if;

    v_content_type := v_storage_metadata ->> 'mimetype';
    v_size_text := v_storage_metadata ->> 'size';

    if v_content_type is null
       or v_content_type not in ('image/jpeg', 'image/png', 'image/webp') then
        raise exception using errcode = 'P0001', message = 'XQ_ATTACHMENT_CONTENT_TYPE_INVALID';
    end if;

    if v_size_text is null or v_size_text !~ '^[0-9]+$' then
        raise exception using errcode = 'P0001', message = 'XQ_ATTACHMENT_STORAGE_METADATA_INVALID';
    end if;

    v_byte_size := v_size_text::bigint;
    if v_byte_size < 1 or v_byte_size > 6291456 then
        raise exception using errcode = 'P0001', message = 'XQ_ATTACHMENT_SIZE_INVALID';
    end if;

    perform 1
      from public.observation_attachments as attachment
     where attachment.id = p_attachment_id
        or attachment.object_name = v_object_name
     for share;

    if found then
        raise exception using errcode = 'P0001', message = 'XQ_ATTACHMENT_ALREADY_COMMITTED';
    end if;

    v_committed_at := pg_catalog.clock_timestamp();

    insert into public.observation_attachments (
        id,
        operation_id,
        organization_id,
        student_id,
        subject_profile_id,
        assignment_id,
        observation_id,
        actor_app_user_id,
        bucket_id,
        object_name,
        content_type,
        byte_size,
        created_at_server
    ) values (
        p_attachment_id,
        p_operation_id,
        p_organization_id,
        p_student_id,
        p_subject_profile_id,
        p_assignment_id,
        p_observation_id,
        v_actor_id,
        'teaching-attachments-v1',
        v_object_name,
        v_content_type,
        v_byte_size,
        v_committed_at
    );

    v_result := pg_catalog.jsonb_build_object(
        'command', 'commit_observation_attachment_v1',
        'operation_id', p_operation_id,
        'attachment_id', p_attachment_id,
        'observation_id', p_observation_id,
        'actor_app_user_id', v_actor_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'subject_profile_id', p_subject_profile_id,
        'subject_key', v_subject_key,
        'assignment_id', p_assignment_id,
        'bucket_id', 'teaching-attachments-v1',
        'object_name', v_object_name,
        'content_type', v_content_type,
        'byte_size', v_byte_size,
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
        'commit_observation_attachment_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.commit_observation_attachment(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid
) from public;
revoke execute on function public.commit_observation_attachment(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid
) from anon;
grant execute on function public.commit_observation_attachment(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid
) to authenticated;

comment on table public.observation_attachments is
'Immutable metadata for private media committed to an authoritative Observation. An attachment is not automatically finalized Evidence.';

comment on function public.commit_observation_attachment(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid
) is
'CommitObservationAttachment v1: live Teaching Fact Gate + exact private Storage object verification + immutable metadata + idempotent receipt.';
