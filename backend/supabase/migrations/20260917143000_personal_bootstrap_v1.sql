-- PersonalBootstrap v1: minimal authorization-scoped read projection for the
-- first Android Quick Capture -> CreateObservation product slice.
--
-- This function is intentionally narrow. It is not an authorization cache and
-- does not replace the live Teaching Fact Gate inside CreateObservation.

create or replace function public.get_personal_bootstrap_v1()
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_auth_subject uuid;
    v_actor_id uuid;
    v_actor_display_name text;
    v_actor_enabled boolean;
    v_generated_at timestamptz;
    v_organizations jsonb;
    v_teaching_contexts jsonb;
begin
    v_auth_subject := auth.uid();
    if v_auth_subject is null then
        raise exception using errcode = 'P0001', message = 'XQ_AUTH_REQUIRED';
    end if;

    select app_user.id, app_user.display_name, app_user.enabled
      into v_actor_id, v_actor_display_name, v_actor_enabled
      from public.app_users as app_user
     where app_user.auth_subject = v_auth_subject;

    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_NOT_FOUND';
    end if;
    if not v_actor_enabled then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_DISABLED';
    end if;

    v_generated_at := pg_catalog.clock_timestamp();

    select coalesce(
        pg_catalog.jsonb_agg(
            pg_catalog.jsonb_build_object(
                'organization_id', organization.id,
                'name', organization.name,
                'can_teach', membership.can_teach
            )
            order by organization.name, organization.id
        ),
        '[]'::jsonb
    )
      into v_organizations
      from public.memberships as membership
      join public.organizations as organization
        on organization.id = membership.organization_id
     where membership.app_user_id = v_actor_id
       and membership.status = 'active';

    select coalesce(
        pg_catalog.jsonb_agg(
            pg_catalog.jsonb_build_object(
                'organization_id', assignment.organization_id,
                'student_id', student.id,
                'student_display_name', student.display_name,
                'subject_profile_id', profile.id,
                'subject_key', profile.subject_key,
                'assignment_id', assignment.id
            )
            order by assignment.organization_id, student.display_name, profile.subject_key, assignment.id
        ),
        '[]'::jsonb
    )
      into v_teaching_contexts
      from public.student_teacher_assignments as assignment
      join public.memberships as membership
        on membership.organization_id = assignment.organization_id
       and membership.app_user_id = assignment.teacher_app_user_id
      join public.students as student
        on student.organization_id = assignment.organization_id
       and student.id = assignment.student_id
      join public.student_subject_profiles as profile
        on profile.organization_id = assignment.organization_id
       and profile.student_id = assignment.student_id
       and profile.id = assignment.subject_profile_id
     where assignment.teacher_app_user_id = v_actor_id
       and assignment.active
       and membership.status = 'active'
       and membership.can_teach
       and student.active
       and profile.active;

    return pg_catalog.jsonb_build_object(
        'contract', 'personal_bootstrap_v1',
        'generated_at_server', v_generated_at,
        'actor', pg_catalog.jsonb_build_object(
            'app_user_id', v_actor_id,
            'display_name', v_actor_display_name
        ),
        'organizations', v_organizations,
        'teaching_contexts', v_teaching_contexts
    );
end;
$$;

revoke execute on function public.get_personal_bootstrap_v1() from public;
revoke execute on function public.get_personal_bootstrap_v1() from anon;
grant execute on function public.get_personal_bootstrap_v1() to authenticated;

comment on function public.get_personal_bootstrap_v1()
is 'PersonalBootstrap v1: current actor, active organizations, and active teaching contexts. Snapshot only; never an authorization proof.';
