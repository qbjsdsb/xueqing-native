-- Client Compatibility v1: enable the Linux native desktop platform.
--
-- Extends public.client_compatibility_policies and public.get_client_compatibility_v1
-- to recognize 'linux' alongside 'android' and 'windows', and seeds the initial
-- explicit 'linux' release-policy row without introducing semver parsing in SQL.

do $$
declare
    v_constraint record;
begin
    for v_constraint in
        select conname
          from pg_catalog.pg_constraint
         where conrelid = 'public.client_compatibility_policies'::regclass
           and contype = 'c'
           and pg_catalog.pg_get_constraintdef(oid) ilike '%platform%'
    loop
        execute format(
            'alter table public.client_compatibility_policies drop constraint %I',
            v_constraint.conname
        );
    end loop;
end;
$$;

alter table public.client_compatibility_policies
    add constraint client_compatibility_policies_platform_check
    check (platform in ('android', 'windows', 'linux'));

create or replace function public.get_client_compatibility_v1(
    p_platform text,
    p_app_version text,
    p_client_contract_version integer
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_policy public.client_compatibility_policies%rowtype;
    v_generated_at timestamptz;
    v_state text;
    v_reason text;
begin
    if auth.uid() is null then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_AUTH_REQUIRED';
    end if;

    if p_platform not in ('android', 'windows', 'linux') then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_CLIENT_PLATFORM_INVALID';
    end if;

    if p_app_version is null
       or pg_catalog.length(p_app_version) < 1
       or pg_catalog.length(p_app_version) > 64
       or p_app_version <> pg_catalog.btrim(p_app_version) then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_CLIENT_VERSION_INVALID';
    end if;

    if p_client_contract_version is null
       or p_client_contract_version < 1 then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_CLIENT_CONTRACT_VERSION_INVALID';
    end if;

    select policy.*
      into v_policy
      from public.client_compatibility_policies as policy
     where policy.platform = p_platform;

    if not found then
        raise exception using
            errcode = 'P0001',
            message = 'XQ_COMPATIBILITY_POLICY_UNAVAILABLE';
    end if;

    if p_app_version = any(v_policy.security_blocked_app_versions) then
        v_state := 'security_blocked';
        v_reason := 'XQ_CLIENT_SECURITY_BLOCKED';
    elsif p_client_contract_version < v_policy.minimum_supported_contract_version
       or p_client_contract_version > v_policy.server_contract_version then
        v_state := 'update_required';
        v_reason := 'XQ_CLIENT_CONTRACT_UNSUPPORTED';
    elsif not (p_app_version = any(v_policy.supported_app_versions)) then
        v_state := 'update_required';
        v_reason := 'XQ_CLIENT_VERSION_UNSUPPORTED';
    elsif p_app_version <> v_policy.recommended_app_version then
        v_state := 'update_recommended';
        v_reason := 'XQ_UPDATE_RECOMMENDED';
    else
        v_state := 'supported';
        v_reason := 'XQ_CLIENT_SUPPORTED';
    end if;

    v_generated_at := pg_catalog.clock_timestamp();

    return pg_catalog.jsonb_build_object(
        'contract', 'client_compatibility_v1',
        'generated_at_server', v_generated_at,
        'policy_revision', v_policy.policy_revision,
        'client', pg_catalog.jsonb_build_object(
            'platform', p_platform,
            'app_version', p_app_version,
            'contract_version', p_client_contract_version
        ),
        'decision', pg_catalog.jsonb_build_object(
            'state', v_state,
            'reason_code', v_reason,
            'minimum_supported_app_version', v_policy.minimum_supported_app_version,
            'recommended_app_version', v_policy.recommended_app_version,
            'minimum_supported_contract_version', v_policy.minimum_supported_contract_version,
            'server_contract_version', v_policy.server_contract_version,
            'update_uri', v_policy.update_uri
        )
    );
end;
$$;

revoke execute on function public.get_client_compatibility_v1(text, text, integer) from public;
revoke execute on function public.get_client_compatibility_v1(text, text, integer) from anon;
grant execute on function public.get_client_compatibility_v1(text, text, integer) to authenticated;

insert into public.client_compatibility_policies (
    platform,
    policy_revision,
    supported_app_versions,
    security_blocked_app_versions,
    minimum_supported_app_version,
    recommended_app_version,
    minimum_supported_contract_version,
    server_contract_version,
    update_uri,
    updated_at
) values (
    'linux',
    'v1-rc1',
    array['1.0.0-rc.1'],
    '{}'::text[],
    '1.0.0-rc.1',
    '1.0.0-rc.1',
    1,
    1,
    'https://github.com/qbjsdsb/xueqing-native/releases',
    pg_catalog.clock_timestamp()
)
on conflict (platform) do update
set
    policy_revision = excluded.policy_revision,
    supported_app_versions = excluded.supported_app_versions,
    security_blocked_app_versions = excluded.security_blocked_app_versions,
    minimum_supported_app_version = excluded.minimum_supported_app_version,
    recommended_app_version = excluded.recommended_app_version,
    minimum_supported_contract_version = excluded.minimum_supported_contract_version,
    server_contract_version = excluded.server_contract_version,
    update_uri = excluded.update_uri,
    updated_at = excluded.updated_at;
