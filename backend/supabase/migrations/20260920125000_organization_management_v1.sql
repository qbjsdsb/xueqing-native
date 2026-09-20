-- Organization Management v1.
--
-- First server-authoritative Organization workspace projection. This is a
-- management visibility boundary only; it does not grant teaching authority
-- and is never authorization proof for later management commands.

create or replace function public.get_organization_management_v1(
    p_organization_id uuid
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
    v_actor_display_name text;
    v_actor_enabled boolean;
    v_actor_role text;
    v_organization_name text;
    v_organization_time_zone text;
    v_members jsonb;
begin
    if p_organization_id is null then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_ORGANIZATION_REQUIRED';
    end if;

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

    select
        membership.membership_role,
        organization.name,
        organization.time_zone
      into
        v_actor_role,
        v_organization_name,
        v_organization_time_zone
      from public.memberships as membership
      join public.organizations as organization
        on organization.id = membership.organization_id
     where membership.organization_id = p_organization_id
       and membership.app_user_id = v_actor_id
       and membership.status = 'active'
       and membership.membership_role in ('owner', 'admin');

    if not found then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_ORGANIZATION_MANAGEMENT_REQUIRED';
    end if;

    select coalesce(
        pg_catalog.jsonb_agg(
            pg_catalog.jsonb_build_object(
                'app_user_id', app_user.id,
                'display_name', app_user.display_name,
                'app_user_enabled', app_user.enabled,
                'membership_role', membership.membership_role,
                'membership_status', membership.status,
                'can_teach', membership.can_teach
            )
            order by
                case membership.membership_role
                    when 'owner' then 1
                    when 'admin' then 2
                    else 3
                end,
                app_user.display_name,
                app_user.id
        ),
        '[]'::jsonb
    )
      into v_members
      from public.memberships as membership
      join public.app_users as app_user
        on app_user.id = membership.app_user_id
     where membership.organization_id = p_organization_id;

    return pg_catalog.jsonb_build_object(
        'contract', 'organization_management_v1',
        'generated_at_server', pg_catalog.statement_timestamp(),
        'actor', pg_catalog.jsonb_build_object(
            'app_user_id', v_actor_id,
            'display_name', v_actor_display_name,
            'membership_role', v_actor_role
        ),
        'organization', pg_catalog.jsonb_build_object(
            'organization_id', p_organization_id,
            'name', v_organization_name,
            'time_zone', v_organization_time_zone
        ),
        'capabilities', pg_catalog.jsonb_build_object(
            'can_invite_owner', v_actor_role = 'admin',
            'can_invite_admin', v_actor_role = 'owner',
            'can_invite_teacher', v_actor_role in ('owner', 'admin')
        ),
        'members', v_members
    );
end;
$$;

revoke execute on function public.get_organization_management_v1(uuid) from public;
revoke execute on function public.get_organization_management_v1(uuid) from anon;
grant execute on function public.get_organization_management_v1(uuid) to authenticated;

comment on function public.get_organization_management_v1(uuid)
is 'OrganizationManagement v1: active owner/admin scoped member roster + narrow capabilities. Snapshot only; management commands must re-authorize.';
