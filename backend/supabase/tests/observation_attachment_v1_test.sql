begin;
set local search_path = public, extensions;

select plan(26);

select has_table(
    'public',
    'observation_attachments',
    'Observation Attachment metadata table exists'
);

select ok(
    (
        select relation.relrowsecurity
        from pg_catalog.pg_class as relation
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = relation.relnamespace
        where namespace.nspname = 'public'
          and relation.relname = 'observation_attachments'
    ),
    'Observation Attachment metadata has RLS enabled'
);

select is(
    (
        select count(*)
        from information_schema.role_table_grants
        where table_schema = 'public'
          and table_name = 'observation_attachments'
          and grantee in ('anon', 'authenticated', 'PUBLIC')
    ),
    0::bigint,
    'clients receive no direct Observation Attachment table CRUD'
);

select is(
    (
        select public::text || ':' || file_size_limit::text
        from storage.buckets
        where id = 'teaching-attachments-v1'
    ),
    'false:6291456'::text,
    'attachment bucket is private with V1 size limit'
);

select is(
    (
        select array_to_string(allowed_mime_types, ',')
        from storage.buckets
        where id = 'teaching-attachments-v1'
    ),
    'image/jpeg,image/png,image/webp'::text,
    'attachment bucket accepts only V1 image MIME types'
);

select is(
    (
        select count(*)
        from pg_catalog.pg_policies
        where schemaname = 'storage'
          and tablename = 'objects'
          and policyname in (
              'xq_observation_attachment_insert_v1',
              'xq_observation_attachment_select_v1'
          )
    ),
    2::bigint,
    'Storage INSERT and SELECT policies are installed'
);

select ok(
    command.prosecdef,
    'CommitObservationAttachment is SECURITY DEFINER'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'commit_observation_attachment';

select ok(
    command.proconfig @> array['search_path=""']::text[],
    'CommitObservationAttachment pins an empty search_path'
)
from pg_catalog.pg_proc as command
join pg_catalog.pg_namespace as namespace on namespace.oid = command.pronamespace
where namespace.nspname = 'public'
  and command.proname = 'commit_observation_attachment';

select ok(
    not pg_catalog.has_function_privilege(
        'anon',
        'public.commit_observation_attachment(uuid,uuid,uuid,uuid,uuid,uuid,uuid)',
        'EXECUTE'
    ),
    'anon cannot commit Observation Attachment metadata'
);

select ok(
    pg_catalog.has_function_privilege(
        'authenticated',
        'public.commit_observation_attachment(uuid,uuid,uuid,uuid,uuid,uuid,uuid)',
        'EXECUTE'
    ),
    'authenticated may call the authoritative attachment command'
);

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo"}',
    true
);

set local role authenticated;

select lives_ok(
    $$
    select public.create_observation(
        '99000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        '附件回归：文字先独立成为权威 Observation。',
        '2026-09-20T03:00:00Z'::timestamptz,
        '{"fixture":true,"source":"attachment-pgtap"}'::jsonb
    )
    $$,
    'fixture Observation is created through authoritative command'
);

reset role;

select set_config(
    'xq.attachment.observation_id',
    (
        select result_payload ->> 'observation_id'
        from public.operation_receipts
        where operation_id = '99000000-0000-0000-0000-000000000001'::uuid
    ),
    true
);

select set_config(
    'xq.attachment.object_name',
    xq_internal.observation_attachment_object_name_v1(
        '20000000-0000-0000-0000-000000000001'::uuid,
        '30000000-0000-0000-0000-000000000001'::uuid,
        '40000000-0000-0000-0000-000000000001'::uuid,
        current_setting('xq.attachment.observation_id')::uuid,
        '99100000-0000-0000-0000-000000000001'::uuid
    ),
    true
);

insert into storage.objects (
    id,
    bucket_id,
    name,
    owner_id,
    metadata
) values (
    '99200000-0000-0000-0000-000000000001'::uuid,
    'teaching-attachments-v1',
    current_setting('xq.attachment.object_name'),
    'a0000000-0000-0000-0000-000000000001',
    '{"size":1234,"mimetype":"image/png"}'::jsonb
);

set local role authenticated;

select lives_ok(
    $$
    select public.commit_observation_attachment(
        '99000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.attachment.observation_id')::uuid,
        '99100000-0000-0000-0000-000000000001'
    )
    $$,
    'uploaded object can be committed as immutable Observation Attachment'
);

reset role;

select is(
    (
        select count(*)
        from public.observation_attachments
        where id = '99100000-0000-0000-0000-000000000001'::uuid
    ),
    1::bigint,
    'attachment commit appends exactly one metadata row'
);

select is(
    (
        select content_type || ':' || byte_size::text
        from public.observation_attachments
        where id = '99100000-0000-0000-0000-000000000001'::uuid
    ),
    'image/png:1234'::text,
    'content type and byte size come from Storage metadata'
);

select is(
    (
        select object_name
        from public.observation_attachments
        where id = '99100000-0000-0000-0000-000000000001'::uuid
    ),
    current_setting('xq.attachment.object_name'),
    'server-derived canonical object name is committed'
);

select is(
    (
        select result_payload ->> 'command'
        from public.operation_receipts
        where operation_id = '99000000-0000-0000-0000-000000000002'::uuid
    ),
    'commit_observation_attachment_v1'::text,
    'attachment metadata and operation receipt commit together'
);

set local role authenticated;

select lives_ok(
    $$
    select public.commit_observation_attachment(
        '99000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.attachment.observation_id')::uuid,
        '99100000-0000-0000-0000-000000000001'
    )
    $$,
    'same attachment commit operation replays committed receipt'
);

select throws_ok(
    $$
    select public.commit_observation_attachment(
        '99000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.attachment.observation_id')::uuid,
        '99100000-0000-0000-0000-000000000099'
    )
    $$,
    'P0001',
    'XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD',
    'operation id cannot be rebound to another attachment'
);

select throws_ok(
    $$
    select public.commit_observation_attachment(
        '99000000-0000-0000-0000-000000000003',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.attachment.observation_id')::uuid,
        '99100000-0000-0000-0000-000000000001'
    )
    $$,
    'P0001',
    'XQ_ATTACHMENT_ALREADY_COMMITTED',
    'committed attachment id cannot be rebound through a new operation'
);

select throws_ok(
    $$
    select public.commit_observation_attachment(
        '99000000-0000-0000-0000-000000000004',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.attachment.observation_id')::uuid,
        '99100000-0000-0000-0000-000000000004'
    )
    $$,
    'P0001',
    'XQ_ATTACHMENT_OBJECT_REQUIRED',
    'metadata command fails closed when bytes were not uploaded'
);

reset role;

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000002', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000002","role":"authenticated","iss":"supabase-demo"}',
    true
);

set local role authenticated;

select throws_ok(
    $$
    select public.commit_observation_attachment(
        '99000000-0000-0000-0000-000000000005',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.attachment.observation_id')::uuid,
        '99100000-0000-0000-0000-000000000005'
    )
    $$,
    'P0001',
    'XQ_TEACHER_ASSIGNMENT_REQUIRED',
    'active organization member without assignment cannot commit another teacher attachment'
);

reset role;

select set_config('request.jwt.claim.sub', 'a0000000-0000-0000-0000-000000000001', true);
select set_config(
    'request.jwt.claims',
    '{"sub":"a0000000-0000-0000-0000-000000000001","role":"authenticated","iss":"supabase-demo"}',
    true
);

select set_config(
    'xq.attachment.rollback_object_name',
    xq_internal.observation_attachment_object_name_v1(
        '20000000-0000-0000-0000-000000000001'::uuid,
        '30000000-0000-0000-0000-000000000001'::uuid,
        '40000000-0000-0000-0000-000000000001'::uuid,
        current_setting('xq.attachment.observation_id')::uuid,
        '99100000-0000-0000-0000-000000000006'::uuid
    ),
    true
);

insert into storage.objects (
    id,
    bucket_id,
    name,
    owner_id,
    metadata
) values (
    '99200000-0000-0000-0000-000000000006'::uuid,
    'teaching-attachments-v1',
    current_setting('xq.attachment.rollback_object_name'),
    'a0000000-0000-0000-0000-000000000001',
    '{"size":2048,"mimetype":"image/jpeg"}'::jsonb
);

create function pg_temp.fail_attachment_receipt()
returns trigger
language plpgsql
as $$
begin
    raise exception 'TEST_FORCED_ATTACHMENT_RECEIPT_FAILURE';
end;
$$;

create trigger force_attachment_receipt_failure
before insert on public.operation_receipts
for each row
when (new.operation_id = '99000000-0000-0000-0000-000000000006'::uuid)
execute function pg_temp.fail_attachment_receipt();

set local role authenticated;

select throws_ok(
    $$
    select public.commit_observation_attachment(
        '99000000-0000-0000-0000-000000000006',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.attachment.observation_id')::uuid,
        '99100000-0000-0000-0000-000000000006'
    )
    $$,
    'P0001',
    'TEST_FORCED_ATTACHMENT_RECEIPT_FAILURE',
    'receipt failure aborts attachment metadata transaction'
);

reset role;

select is(
    (
        select count(*)
        from public.observation_attachments
        where id = '99100000-0000-0000-0000-000000000006'::uuid
    ),
    0::bigint,
    'failed receipt leaves no half-written attachment metadata'
);

select is(
    (
        select count(*)
        from public.operation_receipts
        where operation_id = '99000000-0000-0000-0000-000000000006'::uuid
    ),
    0::bigint,
    'failed receipt leaves no half-written operation receipt'
);

select is(
    (
        select count(*)
        from storage.objects
        where bucket_id = 'teaching-attachments-v1'
          and name = current_setting('xq.attachment.rollback_object_name')
    ),
    1::bigint,
    'failed database commit leaves uploaded object as non-domain-visible orphan'
);

select set_config(
    'xq.attachment.invalid_mime_object_name',
    xq_internal.observation_attachment_object_name_v1(
        '20000000-0000-0000-0000-000000000001'::uuid,
        '30000000-0000-0000-0000-000000000001'::uuid,
        '40000000-0000-0000-0000-000000000001'::uuid,
        current_setting('xq.attachment.observation_id')::uuid,
        '99100000-0000-0000-0000-000000000007'::uuid
    ),
    true
);

insert into storage.objects (
    id,
    bucket_id,
    name,
    owner_id,
    metadata
) values (
    '99200000-0000-0000-0000-000000000007'::uuid,
    'teaching-attachments-v1',
    current_setting('xq.attachment.invalid_mime_object_name'),
    'a0000000-0000-0000-0000-000000000001',
    '{"size":100,"mimetype":"application/pdf"}'::jsonb
);

set local role authenticated;

select throws_ok(
    $$
    select public.commit_observation_attachment(
        '99000000-0000-0000-0000-000000000007',
        '20000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001',
        '50000000-0000-0000-0000-000000000001',
        current_setting('xq.attachment.observation_id')::uuid,
        '99100000-0000-0000-0000-000000000007'
    )
    $$,
    'P0001',
    'XQ_ATTACHMENT_CONTENT_TYPE_INVALID',
    'server refuses unsupported Storage MIME metadata'
);

reset role;

select * from finish();
rollback;
