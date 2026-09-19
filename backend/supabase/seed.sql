-- Fictional deterministic fixtures only. Never place real teacher/student data here.

insert into public.app_users (id, auth_subject, display_name, enabled) values
    ('10000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', '虚构教师甲', true),
    ('10000000-0000-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000002', '虚构教师乙', true),
    ('10000000-0000-0000-0000-000000000003', 'a0000000-0000-0000-0000-000000000003', '虚构停用教师', true);

insert into public.identity_links (
    app_user_id,
    provider_key,
    issuer,
    external_subject,
    active
) values
    ('10000000-0000-0000-0000-000000000001', 'supabase', 'supabase-demo', 'a0000000-0000-0000-0000-000000000001', true),
    ('10000000-0000-0000-0000-000000000002', 'supabase', 'supabase-demo', 'a0000000-0000-0000-0000-000000000002', true),
    ('10000000-0000-0000-0000-000000000003', 'supabase', 'supabase-demo', 'a0000000-0000-0000-0000-000000000003', true);

insert into public.organizations (id, name, time_zone) values
    ('20000000-0000-0000-0000-000000000001', '虚构机构甲', 'Asia/Shanghai'),
    ('20000000-0000-0000-0000-000000000002', '虚构机构乙', 'America/New_York');

insert into public.memberships (
    organization_id,
    app_user_id,
    membership_role,
    status,
    can_teach
) values
    ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'teacher', 'active', true),
    -- Teacher A is deliberately also a valid member of Org B so the cross-org
    -- test proves entity isolation rather than merely "not a member" rejection.
    ('20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 'teacher', 'active', true),
    ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002', 'teacher', 'active', true),
    ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000003', 'teacher', 'disabled', true);

insert into public.students (id, organization_id, display_name, active) values
    ('30000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', '虚构学生甲', true),
    ('30000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000002', '虚构学生乙', true),
    ('30000000-0000-0000-0000-000000000003', '20000000-0000-0000-0000-000000000001', '虚构无学科档案学生', true);

insert into public.student_subject_profiles (
    id,
    organization_id,
    student_id,
    subject_key,
    active
) values
    ('40000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001', 'chinese', true),
    ('40000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000002', '30000000-0000-0000-0000-000000000002', 'chinese', true);

insert into public.student_teacher_assignments (
    id,
    organization_id,
    student_id,
    subject_profile_id,
    teacher_app_user_id,
    active
) values
    ('50000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', true);
