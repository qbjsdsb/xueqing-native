-- Client Compatibility v1: explicit server-authoritative release policy.
--
-- App-version ordering is deliberately NOT parsed in SQL. Each accepted release
-- is listed explicitly so Android versionName and Windows package versions do
-- not leak provider/platform parsing rules into the application contract.
--
-- The table has no direct client grants. Authenticated clients call the narrow
-- SECURITY DEFINER projection below after an ordinary provider session exists.

create table if not exists public.client_compatibility_policies (
    platform text primary key
        check (platform in ('android', 'windows')),
    policy_revision text not null
        check (policy_revision ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    supported_app_versions text[] not null,
    security_blocked_app_versions text[] not null default '{}'::text[],
    minimum_supported_app_version text not null,
    recommended_app_version text not null,
    minimum_supported_contract_version integer not null
        check (minimum_supported_contract_version >= 1),
    server_contract_version integer not null
        check (server_contract_version >= minimum_supported_contract_version),
    update_uri text,
    updated_at timestamptz not null default pg_catalog.clock_timestamp(),
    check (pg_catalog.cardinality(supported_app_versions) > 0),
    check (minimum_supported_app_version = any(supported_app_versions)),
    check (recommended_app_version = any(supported_app_versions)),
    check (
        update_uri is null
        or update_uri ~ '^https://[^[:space:]]+$'
    )
);

revoke all on table public.client_compatibility_policies from public;
revoke all on table public.client_compatibility_policies from anon;
revoke all on table public.client_compatibility_policies from authenticated;

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

    if p_platform not in ('android', 'windows') then
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

comment on table public.client_compatibility_policies
is 'Audited release-policy rows for CLIENT_COMPATIBILITY_V1. No direct client access.';

comment on function public.get_client_compatibility_v1(text, text, integer)
is 'Server-authoritative CLIENT_COMPATIBILITY_V1 projection. Missing policy fails closed; clients map unavailable/invalid responses to local unknown.';
