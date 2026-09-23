begin;
set local search_path = public, extensions;

select plan(18);

select ok(
    compatibility.prosecdef,
    'ClientCompatibility is an explicit SECURITY DEFINER boundary'
)
from pg_catalog.pg_proc as compatibility
join pg_catalog.pg_namespace as namespace on namespace.oid = compatibility.pronamespace
where namespace.nspname = 'public'
  and compatibility.proname = 'get_client_compatibility_v1';

select ok(
    compatibility.proconfig @> array['search_path=""']::text[],
    'ClientCompatibility pins an empty search_path'
)
from pg_catalog.pg_proc as compatibility
join pg_catalog.pg_namespace as namespace on namespace.oid = compatibility.pronamespace
where namespace.nspname = 'public'
  and compatibility.proname = 'get_client_compatibility_v1';

select ok(
    not pg_catalog.has_function_privilege(
        'anon',
        compatibility.oid,
        'EXECUTE'
    ),
    'anon cannot execute ClientCompatibility'
)
from pg_catalog.pg_proc as compatibility
join pg_catalog.pg_namespace as namespace on namespace.oid = compatibility.pronamespace
where namespace.nspname = 'public'
  and compatibility.proname = 'get_client_compatibility_v1';

select ok(
    pg_catalog.has_function_privilege(
        'authenticated',
        compatibility.oid,
        'EXECUTE'
    ),
    'authenticated may execute ClientCompatibility'
)
from pg_catalog.pg_proc as compatibility
join pg_catalog.pg_namespace as namespace on namespace.oid = compatibility.pronamespace
where namespace.nspname = 'public'
  and compatibility.proname = 'get_client_compatibility_v1';

select ok(
    not pg_catalog.has_table_privilege(
        'authenticated',
        'public.client_compatibility_policies',
        'SELECT'
    ),
    'authenticated cannot read compatibility policy rows directly'
);

insert into public.client_compatibility_policies (
    platform,
    policy_revision,
    supported_app_versions,
    security_blocked_app_versions,
    minimum_supported_app_version,
    recommended_app_version,
    minimum_supported_contract_version,
    server_contract_version,
    update_uri
) values (
    'android',
    'test-v1',
    array['0.9.0', '1.0.0'],
    array['0.8.0'],
    '0.9.0',
    '1.0.0',
    1,
    1,
    'https://github.com/qbjsdsb/xueqing-native/releases'
);

select throws_ok(
    $$ select public.get_client_compatibility_v1('android', '1.0.0', 1) $$,
    'P0001',
    'XQ_AUTH_REQUIRED',
    'compatibility lookup requires an ordinary authenticated provider session'
);

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo"}',
    true
);
set local role authenticated;

select is(
    public.get_client_compatibility_v1('android', '1.0.0', 1)
        #>> '{decision,state}',
    'supported',
    'recommended compatible release is supported'
);

select is(
    public.get_client_compatibility_v1('android', '0.9.0', 1)
        #>> '{decision,state}',
    'update_recommended',
    'explicit N-1 release remains supported with a non-blocking update recommendation'
);

select is(
    public.get_client_compatibility_v1('android', '0.9.0', 1)
        #>> '{decision,reason_code}',
    'XQ_UPDATE_RECOMMENDED',
    'N-1 response has a stable machine-readable reason'
);

select is(
    public.get_client_compatibility_v1('android', '0.8.0', 1)
        #>> '{decision,state}',
    'security_blocked',
    'explicit security-block list wins before general version support'
);

select is(
    public.get_client_compatibility_v1('android', '2.0.0', 1)
        #>> '{decision,state}',
    'update_required',
    'unknown app release is not guessed compatible'
);

select is(
    public.get_client_compatibility_v1('android', '1.0.0', 2)
        #>> '{decision,state}',
    'update_required',
    'future client contract is not guessed compatible by an older server'
);

select is(
    public.get_client_compatibility_v1('android', '1.0.0', 2)
        #>> '{decision,reason_code}',
    'XQ_CLIENT_CONTRACT_UNSUPPORTED',
    'contract incompatibility has a stable machine-readable reason'
);

select is(
    public.get_client_compatibility_v1('android', '1.0.0', 1)
        #>> '{policy_revision}',
    'test-v1',
    'response exposes the audited policy revision'
);

select is(
    public.get_client_compatibility_v1('android', '1.0.0', 1)
        #>> '{decision,update_uri}',
    'https://github.com/qbjsdsb/xueqing-native/releases',
    'response exposes bounded HTTPS update guidance'
);

select throws_ok(
    $$ select public.get_client_compatibility_v1('linux', '1.0.0', 1) $$,
    'P0001',
    'XQ_CLIENT_PLATFORM_INVALID',
    'unknown platform fails closed'
);

select throws_ok(
    $$ select public.get_client_compatibility_v1('windows', '1.0.0.0', 1) $$,
    'P0001',
    'XQ_COMPATIBILITY_POLICY_UNAVAILABLE',
    'missing platform policy fails closed instead of fabricating support'
);

reset role;

select throws_ok(
    $$ insert into public.client_compatibility_policies (
        platform,
        policy_revision,
        supported_app_versions,
        minimum_supported_app_version,
        recommended_app_version,
        minimum_supported_contract_version,
        server_contract_version
    ) values (
        'windows',
        'bad-contract-window',
        array['1.0.0.0'],
        '1.0.0.0',
        '1.0.0.0',
        2,
        1
    ) $$,
    '23514',
    null,
    'policy table rejects an impossible contract window'
);

select * from finish();
rollback;
