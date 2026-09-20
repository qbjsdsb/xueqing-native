begin;
set local search_path = public, extensions;

select plan(24);

select ok(
    (
        select is_nullable = 'YES'
          from information_schema.columns
         where table_schema = 'public'
           and table_name = 'app_users'
           and column_name = 'auth_subject'
    ),
    'legacy AppUser auth_subject is nullable for provider-neutral onboarding'
);

select ok(
    acceptance.prosecdef,
    'AcceptOrganizationInvitation is SECURITY DEFINER'
)
from pg_catalog.pg_proc as acceptance
join pg_catalog.pg_namespace as namespace on namespace.oid = acceptance.pronamespace
where namespace.nspname = 'public'
  and acceptance.proname = 'accept_organization_invitation_v1';

select ok(
    acceptance.proconfig @> array['search_path=""']::text[],
    'AcceptOrganizationInvitation pins an empty search_path'
)
from pg_catalog.pg_proc as acceptance
join pg_catalog.pg_namespace as namespace on namespace.oid = acceptance.pronamespace
where namespace.nspname = 'public'
  and acceptance.proname = 'accept_organization_invitation_v1';

select ok(
    not pg_catalog.has_function_privilege(
        'anon',
        'public.accept_organization_invitation_v1(uuid,uuid,text)',
        'EXECUTE'
    ),
    'anon cannot accept Organization invitations'
);

select ok(
    pg_catalog.has_function_privilege(
        'authenticated',
        'public.accept_organization_invitation_v1(uuid,uuid,text)',
        'EXECUTE'
    ),
    'authenticated may call the guarded acceptance command'
);

select pg_catalog.set_config(
    'xq.test.assignment_count',
    (select pg_catalog.count(*)::text from public.student_teacher_assignments),
    true
);

-- Owner creates a pending invitation for a not-yet-linked external identity.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo","email":"owner@example.com","is_anonymous":false}',
    true
);
set local role authenticated;

select pg_catalog.set_config(
    'xq.test.invitation_id',
    public.create_organization_invitation_v1(
        '93000000-0000-0000-0000-00000000a001'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'new.teacher@example.com',
        'teacher',
        true
    ) ->> 'invitation_id',
    true
);
reset role;

-- New invitee accepts using an authenticated external identity + matching email.
select set_config('request.jwt.claim.sub', 'b0000000-0000-0000-0000-00000000a001', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"b0000000-0000-0000-0000-00000000a001","role":"authenticated","iss":"supabase-demo","email":"NEW.TEACHER@example.com","is_anonymous":false}',
    true
);
set local role authenticated;

select is(
    public.accept_organization_invitation_v1(
        '94000000-0000-0000-0000-00000000a001'::uuid,
        pg_catalog.current_setting('xq.test.invitation_id')::uuid,
        '  新教师  '
    ) ->> 'command',
    'accept_organization_invitation_v1',
    'unlinked authenticated invitee accepts through the formal command'
);

select is(
    public.accept_organization_invitation_v1(
        '94000000-0000-0000-0000-00000000a001'::uuid,
        pg_catalog.current_setting('xq.test.invitation_id')::uuid,
        '新教师'
    ) ->> 'membership_role',
    'teacher',
    'same operation replay returns the authoritative membership role'
);

select pg_catalog.set_config(
    'xq.test.accepted_app_user_id',
    public.accept_organization_invitation_v1(
        '94000000-0000-0000-0000-00000000a001'::uuid,
        pg_catalog.current_setting('xq.test.invitation_id')::uuid,
        '新教师'
    ) ->> 'actor_app_user_id',
    true
);

select throws_ok(
    $$
    select public.accept_organization_invitation_v1(
        '94000000-0000-0000-0000-00000000a001'::uuid,
        pg_catalog.current_setting('xq.test.invitation_id')::uuid,
        '不同名字'
    )
    $$,
    'P0001',
    'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD',
    'same operation cannot be replayed with different acceptance payload'
);
reset role;

select is(
    (
        select app_user.auth_subject is null
          from public.app_users as app_user
         where app_user.id = pg_catalog.current_setting('xq.test.accepted_app_user_id')::uuid
    ),
    true,
    'new application-owned AppUser does not require legacy auth_subject'
);

select is(
    (
        select pg_catalog.count(*)
          from public.identity_links as identity_link
         where identity_link.app_user_id = pg_catalog.current_setting('xq.test.accepted_app_user_id')::uuid
           and identity_link.provider_key = 'supabase'
           and identity_link.issuer = 'supabase-demo'
           and identity_link.external_subject = 'b0000000-0000-0000-0000-00000000a001'
           and identity_link.active
    ),
    1::bigint,
    'acceptance creates exactly one active provider-neutral IdentityLink'
);

select is(
    (
        select membership.membership_role
          from public.memberships as membership
         where membership.organization_id = '20000000-0000-0000-0000-000000000001'::uuid
           and membership.app_user_id = pg_catalog.current_setting('xq.test.accepted_app_user_id')::uuid
           and membership.status = 'active'
    ),
    'teacher',
    'acceptance creates authoritative active Membership'
);

select is(
    (
        select membership.can_teach
          from public.memberships as membership
         where membership.organization_id = '20000000-0000-0000-0000-000000000001'::uuid
           and membership.app_user_id = pg_catalog.current_setting('xq.test.accepted_app_user_id')::uuid
    ),
    true,
    'invitation teaching capability is copied to Membership without creating assignment'
);

select is(
    (
        select invitation.status
          from public.organization_invitations as invitation
         where invitation.id = pg_catalog.current_setting('xq.test.invitation_id')::uuid
    ),
    'accepted',
    'invitation is atomically marked accepted'
);

select is(
    (
        select invitation.accepted_by_app_user_id
          from public.organization_invitations as invitation
         where invitation.id = pg_catalog.current_setting('xq.test.invitation_id')::uuid
    ),
    pg_catalog.current_setting('xq.test.accepted_app_user_id')::uuid,
    'accepted invitation points at the application-owned AppUser'
);

select is(
    (select pg_catalog.count(*) from public.student_teacher_assignments),
    pg_catalog.current_setting('xq.test.assignment_count')::bigint,
    'acceptance never creates StudentTeacherAssignment'
);

select is(
    (
        select pg_catalog.count(*)
          from public.operation_receipts
         where operation_id = '94000000-0000-0000-0000-00000000a001'::uuid
           and command_name = 'accept_organization_invitation_v1'
    ),
    1::bigint,
    'acceptance writes exactly one durable operation receipt'
);

-- Wrong authenticated email cannot consume another person's invitation.
select set_config('request.jwt.claim.sub', 'b0000000-0000-0000-0000-00000000a002', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"b0000000-0000-0000-0000-00000000a002","role":"authenticated","iss":"supabase-demo","email":"wrong@example.com","is_anonymous":false}',
    true
);
set local role authenticated;

select throws_ok(
    $$
    select public.accept_organization_invitation_v1(
        '94000000-0000-0000-0000-00000000a002'::uuid,
        pg_catalog.current_setting('xq.test.invitation_id')::uuid,
        '错误用户'
    )
    $$,
    'P0001',
    'XQ_INVITATION_NOT_PENDING',
    'accepted invitation cannot be consumed again by another identity'
);
reset role;

-- Prepare a second pending invitation specifically for mismatch proof.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo","email":"owner@example.com","is_anonymous":false}',
    true
);
set local role authenticated;
select pg_catalog.set_config(
    'xq.test.mismatch_invitation_id',
    public.create_organization_invitation_v1(
        '93000000-0000-0000-0000-00000000a002'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'right@example.com',
        'teacher',
        false
    ) ->> 'invitation_id',
    true
);
reset role;

select set_config('request.jwt.claim.sub', 'b0000000-0000-0000-0000-00000000a002', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"b0000000-0000-0000-0000-00000000a002","role":"authenticated","iss":"supabase-demo","email":"wrong@example.com","is_anonymous":false}',
    true
);
set local role authenticated;
select throws_ok(
    $$
    select public.accept_organization_invitation_v1(
        '94000000-0000-0000-0000-00000000a003'::uuid,
        pg_catalog.current_setting('xq.test.mismatch_invitation_id')::uuid,
        '错误用户'
    )
    $$,
    'P0001',
    'XQ_INVITATION_EMAIL_MISMATCH',
    'provider email must exactly match normalized invitation email'
);
reset role;

-- Anonymous sessions cannot provide acceptance contact proof.
select set_config('request.jwt.claim.sub', 'b0000000-0000-0000-0000-00000000a003', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"b0000000-0000-0000-0000-00000000a003","role":"authenticated","iss":"supabase-demo","email":"right@example.com","is_anonymous":true}',
    true
);
set local role authenticated;
select throws_ok(
    $$
    select public.accept_organization_invitation_v1(
        '94000000-0000-0000-0000-00000000a004'::uuid,
        pg_catalog.current_setting('xq.test.mismatch_invitation_id')::uuid,
        '匿名用户'
    )
    $$,
    'P0001',
    'XQ_INVITATION_EMAIL_REQUIRED',
    'anonymous session cannot satisfy invitation email proof'
);
reset role;

-- Expired invitation fails closed.
update public.organization_invitations
   set expires_at = pg_catalog.clock_timestamp() - interval '1 minute'
 where id = pg_catalog.current_setting('xq.test.mismatch_invitation_id')::uuid;

select set_config('request.jwt.claim.sub', 'b0000000-0000-0000-0000-00000000a004', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"b0000000-0000-0000-0000-00000000a004","role":"authenticated","iss":"supabase-demo","email":"right@example.com","is_anonymous":false}',
    true
);
set local role authenticated;
select throws_ok(
    $$
    select public.accept_organization_invitation_v1(
        '94000000-0000-0000-0000-00000000a005'::uuid,
        pg_catalog.current_setting('xq.test.mismatch_invitation_id')::uuid,
        '过期用户'
    )
    $$,
    'P0001',
    'XQ_INVITATION_EXPIRED',
    'expired invitation cannot create application identity or membership'
);
reset role;

-- Existing member cannot consume an invitation into a duplicate Membership.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo","email":"owner@example.com","is_anonymous":false}',
    true
);
set local role authenticated;
select pg_catalog.set_config(
    'xq.test.existing_member_invitation_id',
    public.create_organization_invitation_v1(
        '93000000-0000-0000-0000-00000000a003'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'admin@example.com',
        'admin',
        true
    ) ->> 'invitation_id',
    true
);
reset role;

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated","iss":"supabase-demo","email":"admin@example.com","is_anonymous":false}',
    true
);
set local role authenticated;
select throws_ok(
    $$
    select public.accept_organization_invitation_v1(
        '94000000-0000-0000-0000-00000000a006'::uuid,
        pg_catalog.current_setting('xq.test.existing_member_invitation_id')::uuid,
        '虚构教师乙'
    )
    $$,
    'P0001',
    'XQ_MEMBERSHIP_ALREADY_EXISTS',
    'existing Organization member cannot receive a duplicate Membership'
);
reset role;

select is(
    (
        select pg_catalog.count(*)
          from public.app_users
         where display_name in ('错误用户', '匿名用户', '过期用户')
    ),
    0::bigint,
    'failed acceptance attempts leave no orphan AppUser rows'
);

select is(
    (
        select pg_catalog.count(*)
          from public.identity_links
         where external_subject in (
             'b0000000-0000-0000-0000-00000000a002',
             'b0000000-0000-0000-0000-00000000a003',
             'b0000000-0000-0000-0000-00000000a004'
         )
    ),
    0::bigint,
    'failed acceptance attempts leave no orphan IdentityLink rows'
);

-- An inactive existing link must fail closed rather than creating a replacement
-- AppUser/IdentityLink for the same external identity.
select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo","email":"owner@example.com","is_anonymous":false}',
    true
);
set local role authenticated;
select pg_catalog.set_config(
    'xq.test.inactive_link_invitation_id',
    public.create_organization_invitation_v1(
        '93000000-0000-0000-0000-00000000a004'::uuid,
        '20000000-0000-0000-0000-000000000001'::uuid,
        'inactive.link@example.com',
        'teacher',
        false
    ) ->> 'invitation_id',
    true
);
reset role;

insert into public.app_users (
    id,
    auth_subject,
    display_name,
    enabled
) values (
    '10000000-0000-0000-0000-00000000a099'::uuid,
    null,
    '停用身份链接用户',
    true
);

insert into public.identity_links (
    app_user_id,
    provider_key,
    issuer,
    external_subject,
    active
) values (
    '10000000-0000-0000-0000-00000000a099'::uuid,
    'supabase',
    'supabase-demo',
    'b0000000-0000-0000-0000-00000000a099',
    false
);

select set_config('request.jwt.claim.sub', 'b0000000-0000-0000-0000-00000000a099', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"b0000000-0000-0000-0000-00000000a099","role":"authenticated","iss":"supabase-demo","email":"inactive.link@example.com","is_anonymous":false}',
    true
);
set local role authenticated;
select throws_ok(
    $
    select public.accept_organization_invitation_v1(
        '94000000-0000-0000-0000-00000000a099'::uuid,
        pg_catalog.current_setting('xq.test.inactive_link_invitation_id')::uuid,
        '停用身份链接用户'
    )
    $,
    'P0001',
    'XQ_IDENTITY_LINK_INACTIVE',
    'inactive external identity link fails closed without replacement onboarding'
);
reset role;

select * from finish();
rollback;
