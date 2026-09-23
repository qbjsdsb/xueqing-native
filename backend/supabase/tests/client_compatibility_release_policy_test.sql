begin;
set local search_path = public, extensions;

select plan(12);

select is(
    (select policy_revision from public.client_compatibility_policies where platform = 'android'),
    'v1-rc1',
    'Android RC policy revision is frozen'
);

select is(
    (select supported_app_versions from public.client_compatibility_policies where platform = 'android'),
    array['1.0.0-rc.1']::text[],
    'Android RC policy supports only the frozen RC version'
);

select is(
    (select recommended_app_version from public.client_compatibility_policies where platform = 'android'),
    '1.0.0-rc.1',
    'Android RC recommended version matches release identity'
);

select is(
    (select policy_revision from public.client_compatibility_policies where platform = 'windows'),
    'v1-rc1',
    'Windows RC policy revision is frozen'
);

select is(
    (select supported_app_versions from public.client_compatibility_policies where platform = 'windows'),
    array['1.0.0.1']::text[],
    'Windows RC policy supports only the frozen package version'
);

select is(
    (select recommended_app_version from public.client_compatibility_policies where platform = 'windows'),
    '1.0.0.1',
    'Windows RC recommended version matches release identity'
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
    public.get_client_compatibility_v1('android', '1.0.0-rc.1', 1)
        #>> '{decision,state}',
    'supported',
    'Frozen Android RC is supported'
);

select is(
    public.get_client_compatibility_v1('windows', '1.0.0.1', 1)
        #>> '{decision,state}',
    'supported',
    'Frozen Windows RC is supported'
);

select is(
    public.get_client_compatibility_v1('android', '1.0.0-rc.2', 1)
        #>> '{decision,state}',
    'update_required',
    'Unlisted Android RC does not receive guessed support'
);

select is(
    public.get_client_compatibility_v1('windows', '1.0.0.2', 1)
        #>> '{decision,state}',
    'update_required',
    'Unlisted Windows package version does not receive guessed support'
);

select is(
    public.get_client_compatibility_v1('android', '1.0.0-rc.1', 1)
        #>> '{policy_revision}',
    'v1-rc1',
    'Android response exposes release policy revision'
);

select is(
    public.get_client_compatibility_v1('windows', '1.0.0.1', 1)
        #>> '{decision,update_uri}',
    'https://github.com/qbjsdsb/xueqing-native/releases',
    'Windows response exposes durable release guidance'
);

reset role;

select * from finish();
rollback;
