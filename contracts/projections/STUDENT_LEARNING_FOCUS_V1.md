# Student Learning Focus v1

Status: **Phase 1 projection candidate — executable backend evidence required**

## Purpose

Return the current personal-teaching focus for one active Student Subject Profile without turning Student Detail into a full archive.

This is a read projection, never authorization proof.

## Entry point

Reference-provider RPC:

```text
get_student_learning_focus_v1(
  p_organization_id,
  p_student_id,
  p_subject_profile_id
)
```

## Authority boundary

The server resolves the current application-owned actor through the active `IdentityLink` and requires:

- enabled AppUser;
- active Membership with teaching capability;
- active Student;
- active Student Subject Profile;
- a current active Student Teacher Assignment for the actor.

Organization management authority alone does not make another teacher's Case part of this personal projection.

## Result shape

The projection returns:

- contract = `student_learning_focus_v1`;
- generated_at_server;
- actor_app_user_id;
- organization_id;
- organization_name;
- organization_time_zone;
- organization_business_date;
- student_id;
- student_display_name;
- subject_profile_id;
- subject_key;
- assignment_id;
- `cases`: at most three current open Cases;
- `has_more`: whether more than three current Cases exist.

Each Case item contains:

- case_id;
- title;
- state;
- case_version;
- responsible_teacher_app_user_id;
- owner_assignment_id;
- created_at_server;
- updated_at_server;
- primary_action:
  - action_id;
  - action_text;
  - due_on;
  - due_bucket;
  - action_version.

## Ordering

Until the product has an explicit teacher-controlled priority/rank field, this projection must **not** claim that it returns the “most important” Cases.

V1 ordering is deterministic recent-active order:

```text
updated_at_server DESC, case_id DESC
```

The first three are therefore the most recently updated open Cases, not an AI/system priority score.

## Primary Action invariant

For every open Case visible in the projection there must be exactly one pending primary Action.

The projection must fail closed with `XQ_CASE_PRIMARY_ACTION_INVARIANT` if privileged/manual data corruption violates that invariant. It must not silently hide an open Case or invent an Action.

## Business date

`organization_business_date` and `due_bucket` are computed on the server from the Organization IANA timezone.

Due buckets:

- `overdue`: due_on < organization business date;
- `today`: due_on = organization business date;
- `undated`: due_on is null;
- `future`: due_on > organization business date.

Clients must not recompute these buckets from device-local time.

## Non-goals

This projection does not:

- return closed Cases;
- return all historical Cases;
- expose organization-supervision data;
- mutate Case or Action state;
- infer severity, risk, priority or diagnosis;
- include Evidence/Intervention/Assessment until their contracts exist.
