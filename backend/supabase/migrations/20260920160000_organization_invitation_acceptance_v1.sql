-- Organization Invitation Acceptance v1.
--
-- The Auth provider proves external identity + invitation email. Xueqing owns
-- AppUser identity, Membership state, invitation acceptance and receipts.

-- Legacy pre-provider-decoupling compatibility only. New AppUsers created by
-- invitation acceptance must not need a provider UUID in the business table.
alter table public.app_users
    alter column auth_subject drop not null;

comment on column public.app_users.auth_subject is
'Legacy pre-provider-decoupling compatibility column. New AppUsers use application-owned id + IdentityLink and leave this null.';

create or replace function xq_internal.current_external_invitation_email_v1()
returns table (
    invited_email text
)
language sql
stable
set search_path = ''
as $$
    select pg_catalog.lower(pg_catalog.btrim(auth.jwt() ->> 'email'))
    where nullif(auth.jwt() ->> 'sub', '') is not null
      and auth.jwt() ->> 'role' = 'authenticated'
      and pg_catalog.coalesce(auth.jwt() ->> 'is_anonymous', 'false') = 'false'
      and nullif(pg_catalog.btrim(auth.jwt() ->> 'email'), '') is not null;
$$;

revoke all on function xq_internal.current_external_invitation_email_v1() from public;
revoke all on function xq_internal.current_external_invitation_email_v1() from anon;
revoke all on function xq_internal.current_external_invitation_email_v1() from authenticated;

comment on function xq_internal.current_external_invitation_email_v1() is
'Reference-provider adapter: returns the normalized authenticated non-anonymous email usable for Organization invitation acceptance.';

create or replace function public.accept_organization_invitation_v1(
    p_operation_id uuid,
    p_invitation_id uuid,
    p_display_name text
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
    v_invited_email text;
    v_display_name text;
    v_actor_id uuid;
    v_actor_enabled boolean;
    v_identity_link_active boolean;
    v_has_identity_link boolean := false;
    v_invitation public.organization_invitations%rowtype;
    v_existing_membership public.memberships%rowtype;
    v_request_payload jsonb;
    v_existing_receipt public.operation_receipts%rowtype;
    v_committed_at timestamptz;
    v_result jsonb;
begin
    if p_operation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_ID_REQUIRED';
    end if;
    if p_invitation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_REQUIRED';
    end if;

    v_display_name := pg_catalog.btrim(p_display_name);
    if v_display_name is null
       or pg_catalog.char_length(v_display_name) < 1
       or pg_catalog.char_length(v_display_name) > 200 then
        raise exception using errcode = 'P0001', message = 'XQ_DISPLAY_NAME_INVALID';
    end if;

    select identity.provider_key, identity.issuer, identity.external_subject
      into v_provider_key, v_issuer, v_external_subject
      from xq_internal.current_external_identity_v1() as identity;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_AUTH_REQUIRED';
    end if;

    select contact.invited_email
      into v_invited_email
      from xq_internal.current_external_invitation_email_v1() as contact;

    if not found
       or v_invited_email is null
       or pg_catalog.char_length(v_invited_email) < 3
       or pg_catalog.char_length(v_invited_email) > 320
       or v_invited_email !~ '^[^[:space:]@]+@[^[:space:]@]+$' then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_EMAIL_REQUIRED';
    end if;

    -- Serialize onboarding for one external identity before consulting or
    -- creating IdentityLink/AppUser state.
    perform pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended(
            'external-identity:' || v_provider_key || ':' || v_issuer || ':' || v_external_subject,
            0
        )
    );

    perform pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended(p_operation_id::text, 0)
    );

    select app_user.id, app_user.enabled, identity_link.active
      into v_actor_id, v_actor_enabled, v_identity_link_active
      from public.identity_links as identity_link
      join public.app_users as app_user
        on app_user.id = identity_link.app_user_id
     where identity_link.provider_key = v_provider_key
       and identity_link.issuer = v_issuer
       and identity_link.external_subject = v_external_subject
     for share of identity_link, app_user;

    if found then
        v_has_identity_link := true;
        if not v_identity_link_active then
            raise exception using errcode = 'P0001', message = 'XQ_IDENTITY_LINK_INACTIVE';
        end if;
        if not v_actor_enabled then
            raise exception using errcode = 'P0001', message = 'XQ_ACTOR_DISABLED';
        end if;

        v_request_payload := pg_catalog.jsonb_build_object(
            'invitation_id', p_invitation_id,
            'display_name', v_display_name
        );

        select receipt.*
          into v_existing_receipt
          from public.operation_receipts as receipt
         where receipt.operation_id = p_operation_id;

        if found then
            if v_existing_receipt.actor_app_user_id = v_actor_id
               and v_existing_receipt.command_name = 'accept_organization_invitation_v1'
               and v_existing_receipt.request_payload = v_request_payload then
                return v_existing_receipt.result_payload;
            end if;
            raise exception using
                errcode = 'P0001',
                message = 'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD';
        end if;
    end if;

    select invitation.*
      into v_invitation
      from public.organization_invitations as invitation
     where invitation.id = p_invitation_id
     for update;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_NOT_FOUND';
    end if;
    if v_invitation.status <> 'pending' then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_NOT_PENDING';
    end if;
    if v_invitation.expires_at <= pg_catalog.clock_timestamp() then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_EXPIRED';
    end if;
    if v_invitation.invited_email <> v_invited_email then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_EMAIL_MISMATCH';
    end if;

    if not v_has_identity_link then
        v_actor_id := pg_catalog.gen_random_uuid();
        v_actor_enabled := true;

        insert into public.app_users (
            id,
            auth_subject,
            display_name,
            enabled
        ) values (
            v_actor_id,
            null,
            v_display_name,
            true
        );

        insert into public.identity_links (
            app_user_id,
            provider_key,
            issuer,
            external_subject,
            active
        ) values (
            v_actor_id,
            v_provider_key,
            v_issuer,
            v_external_subject,
            true
        );
    end if;

    v_request_payload := pg_catalog.jsonb_build_object(
        'invitation_id', p_invitation_id,
        'display_name', v_display_name
    );

    -- Detect operation reuse after onboarding too. Any newly inserted AppUser /
    -- IdentityLink rows are in this same transaction and roll back on conflict.
    select receipt.*
      into v_existing_receipt
      from public.operation_receipts as receipt
     where receipt.operation_id = p_operation_id;

    if found then
        if v_existing_receipt.actor_app_user_id = v_actor_id
           and v_existing_receipt.command_name = 'accept_organization_invitation_v1'
           and v_existing_receipt.request_payload = v_request_payload then
            return v_existing_receipt.result_payload;
        end if;
        raise exception using
            errcode = 'P0001',
            message = 'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD';
    end if;

    select membership.*
      into v_existing_membership
      from public.memberships as membership
     where membership.organization_id = v_invitation.organization_id
       and membership.app_user_id = v_actor_id
     for share;

    if found then
        raise exception using errcode = 'P0001', message = 'XQ_MEMBERSHIP_ALREADY_EXISTS';
    end if;

    v_committed_at := pg_catalog.clock_timestamp();

    insert into public.memberships (
        organization_id,
        app_user_id,
        membership_role,
        status,
        can_teach,
        created_at
    ) values (
        v_invitation.organization_id,
        v_actor_id,
        v_invitation.target_role,
        'active',
        v_invitation.target_can_teach,
        v_committed_at
    );

    update public.organization_invitations
       set status = 'accepted',
           accepted_by_app_user_id = v_actor_id,
           accepted_at_server = v_committed_at
     where id = p_invitation_id
       and status = 'pending';

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_NOT_PENDING';
    end if;

    v_result := pg_catalog.jsonb_build_object(
        'command', 'accept_organization_invitation_v1',
        'operation_id', p_operation_id,
        'invitation_id', p_invitation_id,
        'actor_app_user_id', v_actor_id,
        'organization_id', v_invitation.organization_id,
        'membership_role', v_invitation.target_role,
        'can_teach', v_invitation.target_can_teach,
        'status', 'accepted',
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
        'accept_organization_invitation_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result;
end;
$$;

revoke execute on function public.accept_organization_invitation_v1(uuid, uuid, text) from public;
revoke execute on function public.accept_organization_invitation_v1(uuid, uuid, text) from anon;
grant execute on function public.accept_organization_invitation_v1(uuid, uuid, text) to authenticated;

comment on function public.accept_organization_invitation_v1(uuid, uuid, text) is
'AcceptOrganizationInvitation v1: provider-neutral invite-email proof + AppUser/IdentityLink onboarding + atomic Membership/invitation receipt.';
