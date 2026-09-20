-- Organization Invitation v1.
--
-- Persists management intent independently from provider-specific email/auth
-- delivery. Acceptance/onboarding is a later gate; this command creates no
-- Membership and no StudentTeacherAssignment.

create table public.organization_invitations (
    id uuid primary key,
    create_operation_id uuid not null unique,
    organization_id uuid not null references public.organizations(id) on delete restrict,
    invited_email text not null
        check (pg_catalog.char_length(invited_email) between 3 and 320)
        check (invited_email = pg_catalog.lower(pg_catalog.btrim(invited_email)))
        check (invited_email ~ '^[^[:space:]@]+@[^[:space:]@]+$'),
    target_role text not null
        check (target_role in ('owner', 'admin', 'teacher')),
    target_can_teach boolean not null,
    status text not null
        check (status in ('pending', 'accepted', 'revoked', 'expired')),
    invited_by_app_user_id uuid not null references public.app_users(id) on delete restrict,
    expires_at timestamptz not null,
    created_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    accepted_by_app_user_id uuid references public.app_users(id) on delete restrict,
    accepted_at_server timestamptz,
    check (expires_at > created_at_server),
    check (
        (status = 'accepted' and accepted_by_app_user_id is not null and accepted_at_server is not null)
        or
        (status <> 'accepted' and accepted_by_app_user_id is null and accepted_at_server is null)
    )
);

create unique index organization_invitations_one_pending_email
    on public.organization_invitations (organization_id, invited_email)
    where status = 'pending';

create index organization_invitations_by_org_status
    on public.organization_invitations (
        organization_id,
        status,
        created_at_server desc,
        id
    );

alter table public.organization_invitations enable row level security;
revoke all on table public.organization_invitations from anon, authenticated;

create or replace function public.create_organization_invitation_v1(
    p_operation_id uuid,
    p_organization_id uuid,
    p_invited_email text,
    p_target_role text,
    p_target_can_teach boolean
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
    v_actor_membership_status text;
    v_actor_membership_role text;
    v_email text;
    v_request_payload jsonb;
    v_existing_receipt public.operation_receipts%rowtype;
    v_invitation_id uuid;
    v_committed_at timestamptz;
    v_expires_at timestamptz;
    v_result jsonb;
begin
    if p_operation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_ID_REQUIRED';
    end if;
    if p_organization_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_ORGANIZATION_REQUIRED';
    end if;
    if p_target_role is null or p_target_role not in ('owner', 'admin', 'teacher') then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_ROLE_INVALID';
    end if;
    if p_target_can_teach is null then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_TEACHING_CAPABILITY_REQUIRED';
    end if;

    v_email := pg_catalog.lower(pg_catalog.btrim(p_invited_email));
    if v_email is null
       or pg_catalog.char_length(v_email) < 3
       or pg_catalog.char_length(v_email) > 320
       or v_email !~ '^[^[:space:]@]+@[^[:space:]@]+$' then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_EMAIL_INVALID';
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
        'invited_email', v_email,
        'target_role', p_target_role,
        'target_can_teach', p_target_can_teach
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
           and v_existing_receipt.command_name = 'create_organization_invitation_v1'
           and v_existing_receipt.request_payload = v_request_payload then
            return v_existing_receipt.result_payload;
        end if;
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD';
    end if;

    select membership.status, membership.membership_role
      into v_actor_membership_status, v_actor_membership_role
      from public.memberships as membership
     where membership.organization_id = p_organization_id
       and membership.app_user_id = v_actor_id
     for share;

    if not found
       or v_actor_membership_status <> 'active'
       or v_actor_membership_role not in ('owner', 'admin') then
        raise exception using errcode = 'P0001', message = 'XQ_ORGANIZATION_MANAGEMENT_REQUIRED';
    end if;

    if (v_actor_membership_role = 'owner' and p_target_role not in ('admin', 'teacher'))
       or (v_actor_membership_role = 'admin' and p_target_role not in ('owner', 'teacher')) then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_ROLE_NOT_ALLOWED';
    end if;

    -- Serialize invitation intent per Organization + normalized address so two
    -- different operation ids cannot create competing pending invitations.
    perform pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended(
            'organization-invitation:' || p_organization_id::text || ':' || v_email,
            0
        )
    );

    v_committed_at := pg_catalog.clock_timestamp();

    update public.organization_invitations
       set status = 'expired'
     where organization_id = p_organization_id
       and invited_email = v_email
       and status = 'pending'
       and expires_at <= v_committed_at;

    if exists (
        select 1
          from public.organization_invitations as invitation
         where invitation.organization_id = p_organization_id
           and invitation.invited_email = v_email
           and invitation.status = 'pending'
    ) then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_ALREADY_PENDING';
    end if;

    v_invitation_id := pg_catalog.gen_random_uuid();
    v_expires_at := v_committed_at + interval '7 days';

    insert into public.organization_invitations (
        id,
        create_operation_id,
        organization_id,
        invited_email,
        target_role,
        target_can_teach,
        status,
        invited_by_app_user_id,
        expires_at,
        created_at_server
    ) values (
        v_invitation_id,
        p_operation_id,
        p_organization_id,
        v_email,
        p_target_role,
        p_target_can_teach,
        'pending',
        v_actor_id,
        v_expires_at,
        v_committed_at
    );

    v_result := pg_catalog.jsonb_build_object(
        'command', 'create_organization_invitation_v1',
        'operation_id', p_operation_id,
        'invitation_id', v_invitation_id,
        'actor_app_user_id', v_actor_id,
        'organization_id', p_organization_id,
        'invited_email', v_email,
        'target_role', p_target_role,
        'target_can_teach', p_target_can_teach,
        'status', 'pending',
        'expires_at', v_expires_at,
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
        'create_organization_invitation_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.create_organization_invitation_v1(uuid, uuid, text, text, boolean) from public;
revoke execute on function public.create_organization_invitation_v1(uuid, uuid, text, text, boolean) from anon;
grant execute on function public.create_organization_invitation_v1(uuid, uuid, text, text, boolean) to authenticated;

comment on table public.organization_invitations is
'Provider-neutral Organization invitation intent. Delivery and acceptance are separate gates.';

comment on function public.create_organization_invitation_v1(uuid, uuid, text, text, boolean) is
'Creates one idempotent pending Organization invitation after live owner/admin authorization. Does not send email or create membership.';
