begin;
set local search_path = public, extensions;

select plan(14);

select ok(
    bootstrap.prosecdef,
    'PersonalBootstrap is an explicit SECURITY DEFINER projection boundary'
)
from pg_catalog.pg_proc as bootstrap
join pg_catalog.pg_namespace as namespace on namespace.oid = bootstrap.pronamespace
where namespace.nspname = 'public' and bootstrap.proname = 'get_personal_bootstrap_v1';

select ok(
    bootstrap.proconfig @> array['search_path=""']::text[],
    'PersonalBootstrap pins an empty search_path'
)
from pg_catalog.pg_proc as bootstrap
join pg_catalog.pg_namespace as namespace on namespace.oid = bootstrap.pronamespace
where namespace.nspname = 'public' and bootstrap.proname = 'get_personal_bootstrap_v1';

select ok(
    not pg_catalog.has_function_privilege('anon', bootstrap.oid, 'EXECUTE'),
    'anon cannot execute PersonalBootstrap'
)
from pg_catalog.pg_proc as bootstrap
join pg_catalog.pg_namespace as namespace on namespace.oid = bootstrap.pronamespace
where namespace.nspname = 'public' and bootstrap.proname = 'get_personal_bootstrap_v1';

select ok(
    pg_catalog.has_function_privilege('authenticated', bootstrap.oid, 'EXECUTE'),
    'authenticated may execute PersonalBootstrap'
)
from pg_catalog.pg_proc as bootstrap
join pg_catalog.pg_namespace as namespace on namespace.oid = bootstrap.pronamespace
where namespace.nspname = 'public' and bootstrap.proname = 'get_personal_bootstrap_v1';

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated"}', true);
set local role authenticated;
create temporary table bootstrap_teacher_a as
select public.get_personal_bootstrap_v1() as payload;
reset role;

select is(
    (select payload ->> 'contract' from bootstrap_teacher_a),
    'personal_bootstrap_v1',
    'projection advertises the frozen v1 contract'
);
select is(
    (select payload #>> '{actor,app_user_id}' from bootstrap_teacher_a),
    '10000000-0000-0000-0000-000000000001',
    'actor application id is derived from the authenticated subject'
);
select is(
    (select payload #>> '{actor,display_name}' from bootstrap_teacher_a),
    '虚构教师甲',
    'actor display name is returned from application identity'
);
select is(
    (select pg_catalog.jsonb_array_length(payload -> 'organizations') from bootstrap_teacher_a),
    2,
    'Teacher A sees both active organization memberships'
);
select is(
    (select pg_catalog.jsonb_array_length(payload -> 'teaching_contexts') from bootstrap_teacher_a),
    1,
    'Teacher A sees only the one currently assigned teaching context'
);
select is(
    (select payload #>> '{teaching_contexts,0,assignment_id}' from bootstrap_teacher_a),
    '50000000-0000-0000-0000-000000000001',
    'teaching context returns the real assignment id required by CreateObservation'
);
select is(
    (select payload #>> '{teaching_contexts,0,subject_key}' from bootstrap_teacher_a),
    'chinese',
    'teaching context returns its active subject key'
);

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated"}', true);
set local role authenticated;
create temporary table bootstrap_teacher_b as
select public.get_personal_bootstrap_v1() as payload;
reset role;
select is(
    (select pg_catalog.jsonb_array_length(payload -> 'teaching_contexts') from bootstrap_teacher_b),
    0,
    'active teaching-capable member without assignment receives no teaching context'
);

update public.app_users
set enabled = false
where id = '10000000-0000-0000-0000-000000000003';
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000003', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000003","role":"authenticated"}', true);
set local role authenticated;
select throws_ok(
    $$ select public.get_personal_bootstrap_v1() $$,
    'P0001',
    'XQ_ACTOR_DISABLED',
    'disabled application actor cannot obtain PersonalBootstrap'
);
reset role;

select set_config('request.jwt.claim.sub', 'ffffffff-ffff-ffff-ffff-ffffffffffff', true);
select set_config('request.jwt.claims', '{"sub":"ffffffff-ffff-ffff-ffff-ffffffffffff","role":"authenticated"}', true);
set local role authenticated;
select throws_ok(
    $$ select public.get_personal_bootstrap_v1() $$,
    'P0001',
    'XQ_ACTOR_NOT_FOUND',
    'unknown authenticated subject is rejected instead of receiving an empty projection'
);
reset role;

select * from finish();
rollback;
