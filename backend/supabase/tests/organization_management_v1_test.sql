begin;
set local search_path = public, extensions;

select plan(17);

select ok(
    management.prosecdef,
    'OrganizationManagement is an explicit SECURITY DEFINER projection boundary'
)
from pg_catalog.pg_proc as management
join pg_catalog.pg_namespace as namespace on namespace.oid = management.pronamespace
where namespace.nspname = 'public' and management.proname = 'get_organization_management_v1';

select ok(
    management.proconfig @> array['search_path=""']::text[],
    'OrganizationManagement pins an empty search_path'
)
from pg_catalog.pg_proc as management
join pg_catalog.pg_namespace as namespace on namespace.oid = management.pronamespace
where namespace.nspname = 'public' and management.proname = 'get_organization_management_v1';

select ok(
    not pg_catalog.has_function_privilege('anon', management.oid, 'EXECUTE'),
    'anon cannot execute OrganizationManagement'
)
from pg_catalog.pg_proc as management
join pg_catalog.pg_namespace as namespace on namespace.oid = management.pronamespace
where namespace.nspname = 'public' and management.proname = 'get_organization_management_v1';

select ok(
    pg_catalog.has_function_privilege('authenticated', management.oid, 'EXECUTE'),
    'authenticated may call the guarded OrganizationManagement projection'
)
from pg_catalog.pg_proc as management
join pg_catalog.pg_namespace as namespace on namespace.oid = management.pronamespace
where namespace.nspname = 'public' and management.proname = 'get_organization_management_v1';

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo"}', true);
set local role authenticated;

select is(
    public.get_organization_management_v1('20000000-0000-0000-0000-000000000001'::uuid) ->> 'contract',
    'organization_management_v1',
    'projection advertises the frozen contract'
);
select is(
    public.get_organization_management_v1('20000000-0000-0000-0000-000000000001'::uuid) #>> '{actor,membership_role}',
    'owner',
    'owner role is server-derived from the active organization membership'
);
select is(
    public.get_organization_management_v1('20000000-0000-0000-0000-000000000001'::uuid) #>> '{organization,organization_id}',
    '20000000-0000-0000-0000-000000000001',
    'projection remains bound to the requested authorized organization'
);
select is(
    pg_catalog.jsonb_array_length(
        public.get_organization_management_v1('20000000-0000-0000-0000-000000000001'::uuid) -> 'members'
    ),
    3,
    'owner sees the complete organization member roster including disabled membership'
);
select is(
    public.get_organization_management_v1('20000000-0000-0000-0000-000000000001'::uuid) #>> '{capabilities,can_invite_admin}',
    'true',
    'owner may invite admin'
);
select is(
    public.get_organization_management_v1('20000000-0000-0000-0000-000000000001'::uuid) #>> '{capabilities,can_invite_owner}',
    'false',
    'owner does not receive owner-invite capability'
);
select ok(
    pg_catalog.jsonb_path_exists(
        public.get_organization_management_v1('20000000-0000-0000-0000-000000000001'::uuid),
        '$.members[*] ? (@.app_user_id == "10000000-0000-0000-0000-000000000003" && @.membership_status == "disabled")'
    ),
    'disabled membership remains visible to management'
);
select throws_ok(
    $$ select public.get_organization_management_v1('20000000-0000-0000-0000-000000000002'::uuid) $$,
    'P0001',
    'XQ_ORGANIZATION_MANAGEMENT_REQUIRED',
    'teacher-only membership cannot enter management for another organization'
);
reset role;

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated","iss":"supabase-demo"}', true);
set local role authenticated;
select is(
    public.get_organization_management_v1('20000000-0000-0000-0000-000000000001'::uuid) #>> '{actor,membership_role}',
    'admin',
    'admin membership may enter Organization workspace'
);
select is(
    public.get_organization_management_v1('20000000-0000-0000-0000-000000000001'::uuid) #>> '{capabilities,can_invite_owner}',
    'true',
    'admin may invite owner under the frozen capability policy'
);
select is(
    public.get_organization_management_v1('20000000-0000-0000-0000-000000000001'::uuid) #>> '{capabilities,can_invite_admin}',
    'false',
    'admin cannot mint another admin under v1 capability policy'
);
reset role;

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000003', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000003","role":"authenticated","iss":"supabase-demo"}', true);
set local role authenticated;
select throws_ok(
    $$ select public.get_organization_management_v1('20000000-0000-0000-0000-000000000001'::uuid) $$,
    'P0001',
    'XQ_ORGANIZATION_MANAGEMENT_REQUIRED',
    'disabled membership cannot enter management'
);
reset role;

select set_config('request.jwt.claim.sub', 'ffffffff-ffff-ffff-ffff-ffffffffffff', true);
select set_config('request.jwt.claims', '{"sub":"ffffffff-ffff-ffff-ffff-ffffffffffff","role":"authenticated","iss":"supabase-demo"}', true);
set local role authenticated;
select throws_ok(
    $$ select public.get_organization_management_v1('20000000-0000-0000-0000-000000000001'::uuid) $$,
    'P0001',
    'XQ_ACTOR_NOT_FOUND',
    'unknown external identity receives no management information'
);
reset role;

select * from finish();
rollback;
