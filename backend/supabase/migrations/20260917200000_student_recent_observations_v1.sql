-- Bounded personal-teaching read side of the Observation vertical slice.
-- STABLE keeps actor, authority and facts on the calling statement's snapshot.
create index observations_recent_subject_scope
    on public.observations (
        organization_id, student_id, subject_profile_id,
        created_at_server desc, id desc
    );

create function public.get_student_recent_observations_v1(
    p_organization_id uuid,
    p_student_id uuid,
    p_subject_profile_id uuid
)
returns jsonb
language plpgsql
stable
security definer
set search_path = ''
as $$
declare
    v_actor_id uuid;
    v_actor_enabled boolean;
    v_context record;
    v_items jsonb;
    v_has_more boolean;
begin
    if auth.uid() is null then
        raise exception using errcode = 'P0001', message = 'XQ_AUTH_REQUIRED';
    end if;
    select actor.id, actor.enabled into v_actor_id, v_actor_enabled
      from public.app_users as actor
     where actor.auth_subject = auth.uid();
    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_NOT_FOUND';
    end if;
    if not v_actor_enabled then
        raise exception using errcode = 'P0001', message = 'XQ_ACTOR_DISABLED';
    end if;
    if p_organization_id is null or p_student_id is null or p_subject_profile_id is null then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_REQUIRED';
    end if;

    -- Management role alone never grants access to the personal workspace.
    -- A single non-disclosing result covers nonexistent and inaccessible scopes.
    select assignment.id as assignment_id, student.display_name, profile.subject_key
      into v_context
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
     where assignment.organization_id = p_organization_id
       and assignment.student_id = p_student_id
       and assignment.subject_profile_id = p_subject_profile_id
       and assignment.teacher_app_user_id = v_actor_id
       and assignment.active
       and membership.status = 'active'
       and membership.can_teach
       and student.active
       and profile.active;
    if not found then
        raise exception using errcode = 'P0001', message = 'XQ_TEACHING_CONTEXT_UNAVAILABLE';
    end if;

    -- Read one sentinel row to report truncation without counting full history.
    -- Server commit time, never client capture time, determines recency.
    with bounded as materialized (
        select observation.id, observation.actor_app_user_id,
               observation.raw_text, observation.client_captured_at,
               observation.created_at_server
          from public.observations as observation
         where observation.organization_id = p_organization_id
           and observation.student_id = p_student_id
           and observation.subject_profile_id = p_subject_profile_id
         order by observation.created_at_server desc, observation.id desc
         limit 21
    ), visible as (
        select * from bounded
         order by created_at_server desc, id desc
         limit 20
    )
    select coalesce(
               (select pg_catalog.jsonb_agg(
                   pg_catalog.jsonb_build_object(
                       'observation_id', item.id,
                       'actor_app_user_id', item.actor_app_user_id,
                       'raw_text', item.raw_text,
                       'client_captured_at', item.client_captured_at,
                       'created_at_server', item.created_at_server
                   ) order by item.created_at_server desc, item.id desc
               ) from visible as item),
               '[]'::jsonb
           ),
           (select pg_catalog.count(*) > 20 from bounded)
      into v_items, v_has_more;

    return pg_catalog.jsonb_build_object(
        'contract', 'student_recent_observations_v1',
        'generated_at_server', pg_catalog.statement_timestamp(),
        'actor_app_user_id', v_actor_id,
        'organization_id', p_organization_id,
        'student_id', p_student_id,
        'student_display_name', v_context.display_name,
        'subject_profile_id', p_subject_profile_id,
        'subject_key', v_context.subject_key,
        'assignment_id', v_context.assignment_id,
        'observations', v_items,
        'has_more', v_has_more
    );
end;
$$;

revoke execute on function public.get_student_recent_observations_v1(uuid, uuid, uuid) from public;
revoke execute on function public.get_student_recent_observations_v1(uuid, uuid, uuid) from anon;
grant execute on function public.get_student_recent_observations_v1(uuid, uuid, uuid) to authenticated;

comment on function public.get_student_recent_observations_v1(uuid, uuid, uuid)
is 'Personal teaching scope: latest 20 authoritative Observations for one active assigned Student Subject Profile; snapshot, not cached authority.';
