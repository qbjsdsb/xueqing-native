begin;
set local search_path = public, extensions;

select plan(25);

select ok(
    (
        select class.relrowsecurity
          from pg_catalog.pg_class as class
          join pg_catalog.pg_namespace as namespace on namespace.oid = class.relnamespace
         where namespace.nspname = 'public'
           and class.relname = 'organization_invitation_deliveries'
    ),
    'delivery table has RLS enabled'
);

select ok(
    delivery.prosecdef,
    'begin delivery is SECURITY DEFINER'
)
from pg_catalog.pg_proc as delivery
join pg_catalog.pg_namespace as namespace on namespace.oid = delivery.pronamespace
where namespace.nspname = 'public'
  and delivery.proname = 'begin_organization_invitation_delivery_v1';

select ok(
    delivery.proconfig @> array['search_path=""']::text[],
    'begin delivery pins an empty search_path'
)
from pg_catalog.pg_proc as delivery
join pg_catalog.pg_namespace as namespace on namespace.oid = delivery.pronamespace
where namespace.nspname = 'public'
  and delivery.proname = 'begin_organization_invitation_delivery_v1';

select ok(
    pg_catalog.has_function_privilege(
        'authenticated',
        'public.begin_organization_invitation_delivery_v1(uuid,uuid)',
        'EXECUTE'
    ),
    'authenticated managers may call guarded begin delivery'
);

select ok(
    not pg_catalog.has_function_privilege(
        'anon',
        'public.begin_organization_invitation_delivery_v1(uuid,uuid)',
        'EXECUTE'
    ),
    'anon cannot begin invitation delivery'
);

select ok(
    not pg_catalog.has_function_privilege(
        'authenticated',
        'public.complete_organization_invitation_delivery_v1(uuid,uuid)',
        'EXECUTE'
    ),
    'authenticated clients cannot mark delivery sent'
);

select ok(
    not pg_catalog.has_function_privilege(
        'authenticated',
        'public.fail_organization_invitation_delivery_v1(uuid,uuid,text)',
        'EXECUTE'
    ),
    'authenticated clients cannot mark delivery failed'
);

select pg_catalog.set_config(
    'xq.test.membership_count',
    (select pg_catalog.count(*)::text from public.memberships),
    true
);
select pg_catalog.set_config(
    'xq.test.assignment_count',
    (select pg_catalog.count(*)::text from public.student_teacher_assignments),
    true
);

-- Owner creates the authoritative invitation.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo","email":"owner@example.com","is_anonymous":false}',
    true
);
set local role authenticated;

select pg_catalog.set_config(
    'xq.test.delivery_invitation_id',
    public.create_organization_invitation_v1(
        '95000000-0000-0000-0000-00000000d001'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'delivery.teacher@example.com',
        'teacher',
        true
    ) ->> 'invitation_id',
    true
);

select is(
    public.begin_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d001'::uuid,
        pg_catalog.current_setting('xq.test.delivery_invitation_id')::uuid
    ) ->> 'should_dispatch',
    'true',
    'first claim authorizes exactly one provider dispatch'
);

select is(
    public.begin_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d001'::uuid,
        pg_catalog.current_setting('xq.test.delivery_invitation_id')::uuid
    ) ->> 'state',
    'dispatching',
    'same-operation replay resolves current dispatching state'
);

select is(
    public.begin_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d001'::uuid,
        pg_catalog.current_setting('xq.test.delivery_invitation_id')::uuid
    ) ->> 'should_dispatch',
    'false',
    'same-operation replay never dispatches a second email'
);

select pg_catalog.set_config(
    'xq.test.delivery_id',
    public.begin_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d001'::uuid,
        pg_catalog.current_setting('xq.test.delivery_invitation_id')::uuid
    ) ->> 'delivery_id',
    true
);

select throws_ok(
    $$
    select public.begin_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d002'::uuid,
        pg_catalog.current_setting('xq.test.delivery_invitation_id')::uuid
    )
    $$,
    'P0001',
    'XQ_INVITATION_DELIVERY_IN_PROGRESS',
    'different operation cannot duplicate an in-flight delivery'
);

reset role;

select is(
    (
        select pg_catalog.count(*)
          from public.organization_invitation_deliveries
         where invitation_id = pg_catalog.current_setting('xq.test.delivery_invitation_id')::uuid
    ),
    1::bigint,
    'one invitation owns exactly one v1 delivery attempt'
);

select is(
    (
        select pg_catalog.count(*)
          from public.operation_receipts
         where operation_id = '96000000-0000-0000-0000-00000000d001'::uuid
           and command_name = 'begin_organization_invitation_delivery_v1'
    ),
    1::bigint,
    'delivery claim reserves operation id through a durable receipt'
);

select is(
    (select pg_catalog.count(*) from public.memberships),
    pg_catalog.current_setting('xq.test.membership_count')::bigint,
    'delivery claim never creates Membership'
);

select is(
    (select pg_catalog.count(*) from public.student_teacher_assignments),
    pg_catalog.current_setting('xq.test.assignment_count')::bigint,
    'delivery claim never creates StudentTeacherAssignment'
);

set local role service_role;

select is(
    public.complete_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d001'::uuid,
        pg_catalog.current_setting('xq.test.delivery_id')::uuid
    ) ->> 'state',
    'sent',
    'trusted adapter may mark an accepted provider request sent'
);

select is(
    public.complete_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d001'::uuid,
        pg_catalog.current_setting('xq.test.delivery_id')::uuid
    ) ->> 'state',
    'sent',
    'delivery completion is idempotent'
);

reset role;
set local role authenticated;

select is(
    public.begin_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d001'::uuid,
        pg_catalog.current_setting('xq.test.delivery_invitation_id')::uuid
    ) ->> 'state',
    'sent',
    'same-operation replay resolves completed sent state'
);

select is(
    public.begin_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d001'::uuid,
        pg_catalog.current_setting('xq.test.delivery_invitation_id')::uuid
    ) ->> 'should_dispatch',
    'false',
    'sent operation replay cannot dispatch again'
);

select throws_ok(
    $$
    select public.begin_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d003'::uuid,
        pg_catalog.current_setting('xq.test.delivery_invitation_id')::uuid
    )
    $$,
    'P0001',
    'XQ_INVITATION_ALREADY_DELIVERED',
    'different operation cannot redeliver without an explicit resend command'
);

-- Prove a definite provider rejection has a distinct terminal state.
select pg_catalog.set_config(
    'xq.test.failed_invitation_id',
    public.create_organization_invitation_v1(
        '95000000-0000-0000-0000-00000000d002'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'failed.delivery@example.com',
        'teacher',
        false
    ) ->> 'invitation_id',
    true
);

select pg_catalog.set_config(
    'xq.test.failed_delivery_id',
    public.begin_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d004'::uuid,
        pg_catalog.current_setting('xq.test.failed_invitation_id')::uuid
    ) ->> 'delivery_id',
    true
);

reset role;
set local role service_role;

select is(
    public.fail_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d004'::uuid,
        pg_catalog.current_setting('xq.test.failed_delivery_id')::uuid,
        'XQ_PROVIDER_DELIVERY_REJECTED'
    ) ->> 'state',
    'failed',
    'trusted adapter records a definite provider rejection'
);

select is(
    public.fail_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d004'::uuid,
        pg_catalog.current_setting('xq.test.failed_delivery_id')::uuid,
        'XQ_PROVIDER_DELIVERY_REJECTED'
    ) ->> 'failure_code',
    'XQ_PROVIDER_DELIVERY_REJECTED',
    'same failure completion is idempotent'
);

reset role;
set local role authenticated;

select is(
    public.begin_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d004'::uuid,
        pg_catalog.current_setting('xq.test.failed_invitation_id')::uuid
    ) ->> 'state',
    'failed',
    'same operation resolves terminal provider rejection'
);

select throws_ok(
    $$
    select public.begin_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d005'::uuid,
        pg_catalog.current_setting('xq.test.failed_invitation_id')::uuid
    )
    $$,
    'P0001',
    'XQ_INVITATION_DELIVERY_RETRY_REQUIRES_RESEND',
    'failed delivery cannot silently become an automatic resend'
);

reset role;

-- A manager losing live authority cannot begin delivery.
update public.memberships
   set status = 'disabled'
 where organization_id = '20000000-0000-0000-0000-000000000001'::uuid
   and app_user_id = '10000000-0000-0000-0000-000000000001'::uuid;

set local role authenticated;

select throws_ok(
    $$
    select public.begin_organization_invitation_delivery_v1(
        '96000000-0000-0000-0000-00000000d006'::uuid,
        pg_catalog.current_setting('xq.test.failed_invitation_id')::uuid
    )
    $$,
    'P0001',
    'XQ_ORGANIZATION_MANAGEMENT_REQUIRED',
    'revoked live management authority cannot claim delivery'
);

select * from finish();
rollback;
