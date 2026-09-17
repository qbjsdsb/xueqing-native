begin;
set local search_path = public, extensions;
select plan(40);

-- Only fictional seeded identities; all mutations roll back.
select ok(p.prosecdef and p.provolatile = 's' and p.proconfig @> array['search_path=""']::text[], 'definer pins empty search_path and one statement snapshot') from pg_catalog.pg_proc p where p.oid = 'public.get_student_recent_observations_v1(uuid,uuid,uuid)'::regprocedure;
select is(pg_catalog.has_function_privilege('anon', 'public.get_student_recent_observations_v1(uuid,uuid,uuid)', 'EXECUTE'), false, 'anon execution privilege is explicit');
select is(pg_catalog.has_function_privilege('authenticated', 'public.get_student_recent_observations_v1(uuid,uuid,uuid)', 'EXECUTE'), true, 'authenticated execution privilege is explicit');
reset role;
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated"}', true);
set local role authenticated;
select is(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') ->> 'contract', 'student_recent_observations_v1', 'versioned envelope');
select is(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') -> 'observations', '[]'::jsonb, 'authorized empty history is successful empty data');
select is((public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') ->> 'has_more')::boolean, false, 'empty history is not truncated');
select is(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') ->> 'actor_app_user_id', '10000000-0000-0000-0000-000000000001', 'reader is application identity derived from subject');
select is(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') ->> 'assignment_id', '50000000-0000-0000-0000-000000000001', 'envelope identifies current reader assignment');
select throws_ok($call$ select * from public.observations $call$, '42501', 'permission denied for table observations', 'projection adds no direct table read grant');
-- Exercise the real authoritative command, not a fake read-model insert.
select set_config('xueqing.test_receipt', public.create_observation(
 '71000000-0000-0000-0000-000000000001',
 '20000000-0000-0000-0000-000000000001',
 '30000000-0000-0000-0000-000000000001',
 '40000000-0000-0000-0000-000000000001',
 '50000000-0000-0000-0000-000000000001',
 E'  虚构课堂观察\n原文保留  ', null, '{"private_fixture":"must not be projected"}'::jsonb
)::text, true);
select is(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') #>> '{observations,0,observation_id}', current_setting('xueqing.test_receipt')::jsonb ->> 'observation_id', 'command receipt and read projection identify the same authoritative fact');
select is((public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') #>> '{observations,0,created_at_server}')::timestamptz, (current_setting('xueqing.test_receipt')::jsonb ->> 'server_committed_at')::timestamptz, 'authoritative command commit time is preserved');
select is(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') #>> '{observations,0,raw_text}', E'  虚构课堂观察\n原文保留  ', 'original whitespace and text survive projection');
select is(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') #> '{observations,0,client_captured_at}', 'null'::jsonb, 'unknown client capture time stays null');
select ok(not ((public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') #> '{observations,0}') ?| array['client_capture_metadata','operation_id','auth_subject']), 'projection excludes metadata receipts and provider identifiers');
reset role;
-- Retired author, another subject, and another organization all have real facts.
insert into public.student_subject_profiles (id,organization_id,student_id,subject_key) values
 ('40000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','math');
insert into public.student_teacher_assignments
 (id,organization_id,student_id,subject_profile_id,teacher_app_user_id,active) values
 ('50000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000002',false),
 ('50000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000001',true),
 ('50000000-0000-0000-0000-000000000004','20000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000002','40000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001',true);
insert into public.observations
 (id,operation_id,organization_id,student_id,subject_profile_id,assignment_id,actor_app_user_id,raw_text,client_captured_at,created_at_server) values
 ('81000000-0000-0000-0000-000000000001','72000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000002','虚构历史教师记录','2999-01-01 00:00:00+00','2000-01-01 00:00:00+00'),
 ('81000000-0000-0000-0000-000000000002','72000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000003','50000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000001','其他学科秘密',null,'2099-01-01 00:00:00+00'),
 ('81000000-0000-0000-0000-000000000003','72000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000002','40000000-0000-0000-0000-000000000002','50000000-0000-0000-0000-000000000004','10000000-0000-0000-0000-000000000001','其他机构秘密',null,'2099-01-01 00:00:00+00');
set local role authenticated;
select is(jsonb_array_length(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') -> 'observations'), 2, 'same reader with two orgs and subjects receives only selected scope');
select is(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') #>> '{observations,1,actor_app_user_id}', '10000000-0000-0000-0000-000000000002', 'current assignment can read history by retired author');
select is(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') #>> '{observations,0,observation_id}', current_setting('xueqing.test_receipt')::jsonb ->> 'observation_id', 'far future client time cannot reorder older authoritative fact');
select throws_ok($call$ select public.get_student_recent_observations_v1(null,null,null) $call$, 'P0001', 'XQ_TEACHING_CONTEXT_REQUIRED', 'null selectors are not empty history');
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000002','40000000-0000-0000-0000-000000000002') $call$, 'P0001', 'XQ_TEACHING_CONTEXT_UNAVAILABLE', 'cross organization student selector is denied');
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000002') $call$, 'P0001', 'XQ_TEACHING_CONTEXT_UNAVAILABLE', 'cross student profile selector is denied');
select throws_ok($call$ select public.get_student_recent_observations_v1('ffffffff-ffff-ffff-ffff-ffffffffffff','ffffffff-ffff-ffff-ffff-ffffffffffff','ffffffff-ffff-ffff-ffff-ffffffffffff') $call$, 'P0001', 'XQ_TEACHING_CONTEXT_UNAVAILABLE', 'nonexistent scope has the same nondisclosing error');
reset role;
-- Eighteen tied facts plus the two earlier facts = exactly twenty.
insert into public.observations
 (id,operation_id,organization_id,student_id,subject_profile_id,assignment_id,actor_app_user_id,raw_text,created_at_server)
select ('82000000-0000-0000-0000-' || lpad(n::text,12,'0'))::uuid,
       ('73000000-0000-0000-0000-' || lpad(n::text,12,'0'))::uuid,
       '20000000-0000-0000-0000-000000000001'::uuid,
       '30000000-0000-0000-0000-000000000001'::uuid,
       '40000000-0000-0000-0000-000000000001'::uuid,
       '50000000-0000-0000-0000-000000000001'::uuid,
       '10000000-0000-0000-0000-000000000001'::uuid,
       '虚构排序记录 ' || n, '2099-01-01 00:00:00+00'::timestamptz
from generate_series(1,18) as n;
set local role authenticated;
select is(jsonb_array_length(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') -> 'observations'), 20, 'exactly twenty visible facts');
select is((public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') ->> 'has_more')::boolean, false, 'exactly twenty is not truncated');
select is(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') #>> '{observations,0,observation_id}', '82000000-0000-0000-0000-000000000018', 'descending UUID breaks equal server time ties');
reset role;
insert into public.observations
 (id,operation_id,organization_id,student_id,subject_profile_id,assignment_id,actor_app_user_id,raw_text,created_at_server) values
 ('82000000-0000-0000-0000-000000000019','73000000-0000-0000-0000-000000000019','20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','虚构第21条', '2099-01-01 00:00:00+00');
set local role authenticated;
select is(jsonb_array_length(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') -> 'observations'), 20, 'twenty-one facts remain bounded to twenty');
select is((public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') ->> 'has_more')::boolean, true, 'sentinel reports omitted history');
select is(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') #>> '{observations,19,observation_id}', current_setting('xueqing.test_receipt')::jsonb ->> 'observation_id', 'oldest fact is omitted rather than arbitrary truncation');
select is(public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') -> 'observations', public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') -> 'observations', 'unchanged snapshot has deterministic order');
reset role;
update public.app_users set enabled = false where id = '10000000-0000-0000-0000-000000000001';
set local role authenticated;
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') $call$, 'P0001', 'XQ_ACTOR_DISABLED', 'disabled actor rejects unchanged JWT claims');
reset role;
update public.app_users set enabled = true where id = '10000000-0000-0000-0000-000000000001';
set local role authenticated;
reset role;
update public.memberships set status = 'disabled' where app_user_id = '10000000-0000-0000-0000-000000000001' and organization_id = '20000000-0000-0000-0000-000000000001';
set local role authenticated;
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') $call$, 'P0001', 'XQ_TEACHING_CONTEXT_UNAVAILABLE', 'disabled membership rejects unchanged JWT claims');
reset role;
update public.memberships set status = 'active' where app_user_id = '10000000-0000-0000-0000-000000000001' and organization_id = '20000000-0000-0000-0000-000000000001';
set local role authenticated;
reset role;
update public.memberships set can_teach = false where app_user_id = '10000000-0000-0000-0000-000000000001' and organization_id = '20000000-0000-0000-0000-000000000001';
set local role authenticated;
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') $call$, 'P0001', 'XQ_TEACHING_CONTEXT_UNAVAILABLE', 'removed teaching capability rejects unchanged JWT claims');
reset role;
update public.memberships set can_teach = true where app_user_id = '10000000-0000-0000-0000-000000000001' and organization_id = '20000000-0000-0000-0000-000000000001';
set local role authenticated;
reset role;
update public.students set active = false where id = '30000000-0000-0000-0000-000000000001';
set local role authenticated;
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') $call$, 'P0001', 'XQ_TEACHING_CONTEXT_UNAVAILABLE', 'inactive student rejects unchanged JWT claims');
reset role;
update public.students set active = true where id = '30000000-0000-0000-0000-000000000001';
set local role authenticated;
reset role;
update public.student_subject_profiles set active = false where id = '40000000-0000-0000-0000-000000000001';
set local role authenticated;
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') $call$, 'P0001', 'XQ_TEACHING_CONTEXT_UNAVAILABLE', 'inactive profile rejects unchanged JWT claims');
reset role;
update public.student_subject_profiles set active = true where id = '40000000-0000-0000-0000-000000000001';
set local role authenticated;
reset role;
update public.student_teacher_assignments set active = false where id = '50000000-0000-0000-0000-000000000001';
set local role authenticated;
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') $call$, 'P0001', 'XQ_TEACHING_CONTEXT_UNAVAILABLE', 'revoked assignment rejects unchanged JWT claims');
reset role;
update public.student_teacher_assignments set active = true where id = '50000000-0000-0000-0000-000000000001';
set local role authenticated;
reset role;
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated"}', true);
set local role authenticated;
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') $call$, 'P0001', 'XQ_TEACHING_CONTEXT_UNAVAILABLE', 'unassigned teacher cannot read another teachers student');
reset role;
update public.memberships set membership_role = 'admin' where app_user_id = '10000000-0000-0000-0000-000000000002';
set local role authenticated;
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') $call$, 'P0001', 'XQ_TEACHING_CONTEXT_UNAVAILABLE', 'admin capability alone cannot read personal teaching projection');
reset role;
update public.memberships set membership_role = 'owner' where app_user_id = '10000000-0000-0000-0000-000000000002';
set local role authenticated;
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') $call$, 'P0001', 'XQ_TEACHING_CONTEXT_UNAVAILABLE', 'owner capability alone cannot read personal teaching projection');
reset role;
delete from public.memberships where app_user_id = '10000000-0000-0000-0000-000000000002' and organization_id = '20000000-0000-0000-0000-000000000002';
set local role authenticated;
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000002','40000000-0000-0000-0000-000000000002') $call$, 'P0001', 'XQ_TEACHING_CONTEXT_UNAVAILABLE', 'no organization membership cannot read an existing other org fact');
reset role;
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000009', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-0000-0000-000000000009","role":"authenticated"}', true);
set local role authenticated;
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') $call$, 'P0001', 'XQ_ACTOR_NOT_FOUND', 'unmapped subject is rejected');
reset role;
select set_config('request.jwt.claim.sub', '', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config('request.jwt.claims', '{"role":"authenticated"}', true);
set local role authenticated;
select throws_ok($call$ select public.get_student_recent_observations_v1('20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001') $call$, 'P0001', 'XQ_AUTH_REQUIRED', 'missing auth subject is rejected');
reset role;
select * from finish();
rollback;
