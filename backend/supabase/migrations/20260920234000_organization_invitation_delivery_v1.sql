-- Organization Invitation Delivery v1.
--
-- Delivery is a provider side effect. The Organization invitation remains the
-- authoritative business object and email is never sent from this transaction.

create table public.organization_invitation_deliveries (
    id uuid primary key,
    operation_id uuid not null unique,
    invitation_id uuid not null unique
        references public.organization_invitations(id) on delete restrict,
    organization_id uuid not null
        references public.organizations(id) on delete restrict,
    requested_by_app_user_id uuid not null
        references public.app_users(id) on delete restrict,
    provider_key text not null
        check (pg_catalog.char_length(provider_key) between 1 and 100),
    status text not null
        check (status in ('dispatching', 'sent', 'failed')),
    failure_code text,
    requested_at_server timestamptz not null default pg_catalog.clock_timestamp(),
    completed_at_server timestamptz,
    check (
        (status = 'dispatching' and completed_at_server is null and failure_code is null)
        or
        (status = 'sent' and completed_at_server is not null and failure_code is null)
        or
        (
            status = 'failed'
            and completed_at_server is not null
            and failure_code is not null
            and pg_catalog.char_length(failure_code) between 3 and 100
        )
    )
);

create index organization_invitation_deliveries_by_org_requested
    on public.organization_invitation_deliveries (
        organization_id,
        requested_at_server desc,
        id
    );

alter table public.organization_invitation_deliveries enable row level security;
revoke all on table public.organization_invitation_deliveries from anon, authenticated;

create or replace function public.begin_organization_invitation_delivery_v1(
    p_operation_id uuid,
    p_invitation_id uuid
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
    v_invitation public.organization_invitations%rowtype;
    v_existing_receipt public.operation_receipts%rowtype;
    v_existing_delivery public.organization_invitation_deliveries%rowtype;
    v_request_payload jsonb;
    v_delivery_id uuid;
    v_committed_at timestamptz;
    v_result jsonb;
begin
    if p_operation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_OPERATION_ID_REQUIRED';
    end if;
    if p_invitation_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_INVITATION_REQUIRED';
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
        'invitation_id', p_invitation_id
    );

    perform pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended(p_operation_id::text, 0)
    );

    select receipt.*
      into v_existing_receipt
      from public.operation_receipts as receipt
     where receipt.operation_id = p_operation_id;

    if found then
        if v_existing_receipt.actor_app_user_id <> v_actor_id
           or v_existing_receipt.command_name <> 'begin_organization_invitation_delivery_v1'
           or v_existing_receipt.request_payload <> v_request_payload then
            raise exception using
                errcode = 'P0001',
                message = 'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD';
        end if;

        select delivery.*
          into v_existing_delivery
          from public.organization_invitation_deliveries as delivery
         where delivery.operation_id = p_operation_id;

        if not found then
            raise exception using
                errcode = 'P0001',
                message = 'XQ_INVITATION_DELIVERY_RECEIPT_INVARIANT';
        end if;

        return pg_catalog.jsonb_build_object(
            'command', 'begin_organization_invitation_delivery_v1',
            'operation_id', p_operation_id,
            'delivery_id', v_existing_delivery.id,
            'invitation_id', v_existing_delivery.invitation_id,
            'organization_id', v_existing_delivery.organization_id,
            'provider_key', v_existing_delivery.provider_key,
            'state', v_existing_delivery.status,
            'should_dispatch', false,
            'failure_code', v_existing_delivery.failure_code,
            'requested_at_server', v_existing_delivery.requested_at_server,
            'completed_at_server', v_existing_delivery.completed_at_server
        );
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

    select membership.status, membership.membership_role
      into v_actor_membership_status, v_actor_membership_role
      from public.memberships as membership
     where membership.organization_id = v_invitation.organization_id
       and membership.app_user_id = v_actor_id
     for share;

    if not found
       or v_actor_membership_status <> 'active'
       or v_actor_membership_role not in ('owner', 'admin') then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_ORGANIZATION_MANAGEMENT_REQUIRED';
    end if;

    select delivery.*
      into v_existing_delivery
      from public.organization_invitation_deliveries as delivery
     where delivery.invitation_id = p_invitation_id
     for update;

    if found then
        if v_existing_delivery.status = 'sent' then
            raise exception using
                errcode = 'P0001',
                message = 'XQ_INVITATION_ALREADY_DELIVERED';
        end if;
        if v_existing_delivery.status = 'dispatching' then
            raise exception using
                errcode = 'P0001',
                message = 'XQ_INVITATION_DELIVERY_IN_PROGRESS';
        end if;
        raise exception using
            errcode = 'P0001',
            message = 'XQ_INVITATION_DELIVERY_RETRY_REQUIRES_RESEND';
    end if;

    v_delivery_id := pg_catalog.gen_random_uuid();
    v_committed_at := pg_catalog.clock_timestamp();

    v_result := pg_catalog.jsonb_build_object(
        'command', 'begin_organization_invitation_delivery_v1',
        'operation_id', p_operation_id,
        'delivery_id', v_delivery_id,
        'invitation_id', p_invitation_id,
        'organization_id', v_invitation.organization_id,
        'provider_key', 'supabase-auth-email-otp',
        'state', 'dispatching',
        'should_dispatch', true,
        'requested_at_server', v_committed_at
    );

    insert into public.organization_invitation_deliveries (
        id,
        operation_id,
        invitation_id,
        organization_id,
        requested_by_app_user_id,
        provider_key,
        status,
        requested_at_server
    ) values (
        v_delivery_id,
        p_operation_id,
        p_invitation_id,
        v_invitation.organization_id,
        v_actor_id,
        'supabase-auth-email-otp',
        'dispatching',
        v_committed_at
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
        'begin_organization_invitation_delivery_v1',
        v_request_payload,
        v_result,
        v_committed_at
    );

    return v_result || pg_catalog.jsonb_build_object(
        'invited_email', v_invitation.invited_email
    );
end;
$$;

create or replace function public.complete_organization_invitation_delivery_v1(
    p_operation_id uuid,
    p_delivery_id uuid
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_delivery public.organization_invitation_deliveries%rowtype;
    v_completed_at timestamptz;
begin
    if p_operation_id is null or p_delivery_id is null then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_INVITATION_DELIVERY_ID_REQUIRED';
    end if;

    select delivery.*
      into v_delivery
      from public.organization_invitation_deliveries as delivery
     where delivery.operation_id = p_operation_id
       and delivery.id = p_delivery_id
     for update;

    if not found then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_INVITATION_DELIVERY_NOT_FOUND';
    end if;

    if v_delivery.status = 'sent' then
        return pg_catalog.jsonb_build_object(
            'operation_id', v_delivery.operation_id,
            'delivery_id', v_delivery.id,
            'invitation_id', v_delivery.invitation_id,
            'state', 'sent',
            'completed_at_server', v_delivery.completed_at_server
        );
    end if;

    if v_delivery.status <> 'dispatching' then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_INVITATION_DELIVERY_NOT_DISPATCHING';
    end if;

    v_completed_at := pg_catalog.clock_timestamp();

    update public.organization_invitation_deliveries
       set status = 'sent',
           completed_at_server = v_completed_at
     where id = v_delivery.id;

    return pg_catalog.jsonb_build_object(
        'operation_id', v_delivery.operation_id,
        'delivery_id', v_delivery.id,
        'invitation_id', v_delivery.invitation_id,
        'state', 'sent',
        'completed_at_server', v_completed_at
    );
end;
$$;

create or replace function public.fail_organization_invitation_delivery_v1(
    p_operation_id uuid,
    p_delivery_id uuid,
    p_failure_code text
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_delivery public.organization_invitation_deliveries%rowtype;
    v_failure_code text;
    v_completed_at timestamptz;
begin
    if p_operation_id is null or p_delivery_id is null then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_INVITATION_DELIVERY_ID_REQUIRED';
    end if;

    v_failure_code := pg_catalog.upper(pg_catalog.btrim(p_failure_code));
    if v_failure_code is null
       or pg_catalog.char_length(v_failure_code) < 3
       or pg_catalog.char_length(v_failure_code) > 100
       or v_failure_code !~ '^XQ_[A-Z0-9_]+$' then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_INVITATION_DELIVERY_FAILURE_INVALID';
    end if;

    select delivery.*
      into v_delivery
      from public.organization_invitation_deliveries as delivery
     where delivery.operation_id = p_operation_id
       and delivery.id = p_delivery_id
     for update;

    if not found then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_INVITATION_DELIVERY_NOT_FOUND';
    end if;

    if v_delivery.status = 'failed' then
        if v_delivery.failure_code <> v_failure_code then
            raise exception using
                errcode = 'P0001',
                message = 'XQ_INVITATION_DELIVERY_FAILURE_CONFLICT';
        end if;
        return pg_catalog.jsonb_build_object(
            'operation_id', v_delivery.operation_id,
            'delivery_id', v_delivery.id,
            'invitation_id', v_delivery.invitation_id,
            'state', 'failed',
            'failure_code', v_delivery.failure_code,
            'completed_at_server', v_delivery.completed_at_server
        );
    end if;

    if v_delivery.status <> 'dispatching' then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_INVITATION_DELIVERY_NOT_DISPATCHING';
    end if;

    v_completed_at := pg_catalog.clock_timestamp();

    update public.organization_invitation_deliveries
       set status = 'failed',
           failure_code = v_failure_code,
           completed_at_server = v_completed_at
     where id = v_delivery.id;

    return pg_catalog.jsonb_build_object(
        'operation_id', v_delivery.operation_id,
        'delivery_id', v_delivery.id,
        'invitation_id', v_delivery.invitation_id,
        'state', 'failed',
        'failure_code', v_failure_code,
        'completed_at_server', v_completed_at
    );
end;
$$;

revoke execute on function public.begin_organization_invitation_delivery_v1(uuid, uuid)
    from public;
revoke execute on function public.begin_organization_invitation_delivery_v1(uuid, uuid)
    from anon;
grant execute on function public.begin_organization_invitation_delivery_v1(uuid, uuid)
    to authenticated;

revoke execute on function public.complete_organization_invitation_delivery_v1(uuid, uuid)
    from public, anon, authenticated;
grant execute on function public.complete_organization_invitation_delivery_v1(uuid, uuid)
    to service_role;

revoke execute on function public.fail_organization_invitation_delivery_v1(uuid, uuid, text)
    from public, anon, authenticated;
grant execute on function public.fail_organization_invitation_delivery_v1(uuid, uuid, text)
    to service_role;

comment on table public.organization_invitation_deliveries is
'Provider-side delivery attempts for authoritative Organization invitations. V1 permits one attempt per invitation and never auto-resends an unknown result.';

comment on function public.begin_organization_invitation_delivery_v1(uuid, uuid) is
'Claims one provider delivery attempt after live Organization management authorization. Does not send email.';

comment on function public.complete_organization_invitation_delivery_v1(uuid, uuid) is
'Service-only completion after the provider accepted the invitation email send request.';

comment on function public.fail_organization_invitation_delivery_v1(uuid, uuid, text) is
'Service-only terminal failure for provider rejections known not to have accepted the send request.';
