# Student Learning Cases v1

Status: **Phase 1 projection candidate — executable backend evidence required**

## Purpose

Return a bounded authoritative Case history for one Student Subject Profile so a teacher can review both current and closed Cases without turning Current Focus into an archive.

This projection complements, not replaces, `STUDENT_LEARNING_FOCUS_V1`.

## Entry point

Reference-provider RPC:

```text
get_student_learning_cases_v1(
  p_organization_id,
  p_student_id,
  p_subject_profile_id
)
```

## Authority boundary

The server resolves the live application-owned actor and requires:

- enabled AppUser;
- active teaching-capable Membership;
- active Student;
- active Student Subject Profile;
- one current active matching Student Teacher Assignment for the reader.

The current assignment authorizes the read. Historical Case ownership does not.

A currently assigned teacher may therefore read authoritative Case history within that exact Student/subject scope even when older Cases were created under an earlier teacher/assignment. This preserves handoff continuity in the same way as Student Recent Observations.

Organization-management role alone grants nothing in this personal projection.

This projection is never authorization proof for a lifecycle write. Every formal command must revalidate current responsibility and versions independently.

## Result shape

Envelope:

- `contract = student_learning_cases_v1`;
- `generated_at_server`;
- `actor_app_user_id`;
- organization/student/profile identity and display metadata;
- current reader `assignment_id`;
- `cases`: at most 50 Cases;
- `has_more`.

Each Case contains:

- `case_id`;
- `title`;
- `state`;
- `case_version`;
- `responsible_teacher_app_user_id`;
- `owner_assignment_id`;
- `is_current_actor_responsibility`: descriptive equality against the current actor/assignment only, never an authorization token;
- `created_at_server`;
- `updated_at_server`;
- `primary_action`:
  - for an open Case: the exact pending primary Action with id/text/due_on/due_bucket/action_version;
  - for a closed Case: `null`.

## Invariants

For every Case in the selected Student/subject scope:

- open Case => exactly one pending primary Action;
- closed Case => zero pending primary Actions.

If privileged/manual corruption violates this rule, fail closed with `XQ_CASE_PRIMARY_ACTION_INVARIANT`.

Do not silently omit the corrupted Case and do not invent an Action.

## Ordering and bounds

V1 is deterministic recent-history order:

```text
updated_at_server DESC, case_id DESC
```

Return at most 50 items and set `has_more` when additional history exists.

This is recency ordering, not severity or priority.

## Business date

The server computes `organization_business_date` from the Organization IANA timezone and derives pending Action due buckets using that date.

Clients must not recompute due buckets from device-local time.

## Client obligations

Clients must validate:

- contract name;
- actor and selected scope;
- unique Case IDs;
- max 50 items;
- deterministic ordering;
- valid state/version values;
- open/closed primary-Action shape.

A client may use `is_current_actor_responsibility` to decide presentation affordance, but must still treat server command authorization as authoritative.

## Non-goals

This V1 does not:

- return a full event timeline;
- return Evidence/Intervention/Assessment history;
- authorize reopen/close/transition;
- expose organization-supervision history;
- paginate with a generic cursor;
- infer risk/severity/priority.
