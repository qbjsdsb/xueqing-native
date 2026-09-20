begin;
set local search_path = public, extensions;

select plan(19);

select pg_catalog.set_config(
    'xq.test.assignment_count',
    (select pg_catalog.count(*)::text from public.student_teacher_assignments),
    true
);

select ok(
    command.prosecdef,
    'CreateOrganizationInvitation is an explicit SECURITY DEFINER boundary'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'create_organization_invitation_v1';

select ok(
    command.proconfig @> array['search_path=""']::text[],
    'CreateOrganizationInvitation pins an empty search_path'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'create_organization_invitation_v1';

select ok(
    not pg_catalog.has_function_privilege(
        'anon',
        'public.create_organization_invitation_v1(uuid,uuid,text,text,boolean)',
        'EXECUTE'
    ),
    'anon cannot create Organization invitations'
);

select ok(
    pg_catalog.has_function_privilege(
        'authenticated',
        'public.create_organization_invitation_v1(uuid,uuid,text,text,boolean)',
        'EXECUTE'
    ),
    'authenticated may call the guarded invitation command'
);

select ok(
    not pg_catalog.has_table_privilege(
        'authenticated',
        'public.organization_invitations',
        'SELECT'
    ),
    'authenticated clients cannot read the invitation table directly'
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
    public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c001'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        '  Invite.Admin@Example.COM  ',
        'admin',
        false
    ) ->> 'command',
    'create_organization_invitation_v1',
    'owner creates an admin invitation through the formal command'
);

select is(
    public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c001'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'invite.admin@example.com',
        'admin',
        false
    ) ->> 'invited_email',
    'invite.admin@example.com',
    'invited email is normalized before receipt comparison'
);

select is(
    public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c001'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'invite.admin@example.com',
        'admin',
        false
    ) ->> 'target_role',
    'admin',
    'owner receives an authoritative admin target receipt'
);

select pg_catalog.set_config(
    'xq.test.invitation_id',
    public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c001'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'invite.admin@example.com',
        'admin',
        false
    ) ->> 'invitation_id',
    true
);

select is(
    public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c001'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'invite.admin@example.com',
        'admin',
        false
    ) ->> 'invitation_id',
    pg_catalog.current_setting('xq.test.invitation_id'),
    'same operation and payload replays the original invitation receipt'
);

select throws_ok(
    $$
    select public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c001'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'different@example.com',
        'admin',
        false
    )
    $$,
    'P0001',
    'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD',
    'operation id cannot be reused for a different invitation payload'
);

select throws_ok(
    $$
    select public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c002'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'owner-by-owner@example.com',
        'owner',
        false
    )
    $$,
    'P0001',
    'XQ_INVITATION_ROLE_NOT_ALLOWED',
    'owner cannot invite another owner under v1 capability policy'
);

select throws_ok(
    $$
    select public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c003'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'INVITE.ADMIN@example.com',
        'teacher',
        true
    )
    $$,
    'P0001',
    'XQ_INVITATION_ALREADY_PENDING',
    'a second operation cannot create a competing pending invitation for the same normalized address'
);

select is(
    public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c004'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'teacher@example.com',
        'teacher',
        true
    ) #>> '{target_can_teach}',
    'true',
    'teaching capability is explicit invitation intent and remains separate from assignment'
);
reset role;

select is(
    (select pg_catalog.count(*) from public.student_teacher_assignments),
    pg_catalog.current_setting('xq.test.assignment_count')::bigint,
    'creating invitations never creates teaching assignments'
);

select is(
    (
        select pg_catalog.count(*)
          from public.operation_receipts
         where operation_id = '91000000-0000-0000-0000-00000000c001'::uuid
           and command_name = 'create_organization_invitation_v1'
    ),
    1::bigint,
    'the successful invitation has exactly one durable operation receipt'
);

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated","iss":"supabase-demo"}',
    true
);
set local role authenticated;

select is(
    public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c005'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'future.owner@example.com',
        'owner',
        true
    ) ->> 'target_role',
    'owner',
    'admin may invite an owner'
);

select throws_ok(
    $$
    select public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c006'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'another.admin@example.com',
        'admin',
        false
    )
    $$,
    'P0001',
    'XQ_INVITATION_ROLE_NOT_ALLOWED',
    'admin cannot invite another admin under v1 capability policy'
);
reset role;

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo"}',
    true
);
set local role authenticated;
select throws_ok(
    $$
    select public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c007'::uuid,
        '20000000-0000-0000-0000-000000000002'::uuid,
        'teacher-denied@example.com',
        'teacher',
        true
    )
    $$,
    'P0001',
    'XQ_ORGANIZATION_MANAGEMENT_REQUIRED',
    'teacher-only membership cannot create Organization invitations'
);
reset role;

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000003', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000003","role":"authenticated","iss":"supabase-demo"}',
    true
);
set local role authenticated;
select throws_ok(
    $$
    select public.create_organization_invitation_v1(
        '91000000-0000-0000-0000-00000000c008'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'disabled-denied@example.com',
        'teacher',
        true
    )
    $$,
    'P0001',
    'XQ_ORGANIZATION_MANAGEMENT_REQUIRED',
    'disabled membership cannot create Organization invitations'
);
reset role;

select * from finish();
rollback;
