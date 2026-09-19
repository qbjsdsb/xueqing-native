# CreateLearningCase v1

Status: **Phase 1 command candidate — executable backend evidence required**

## Purpose

Create the first formal Learning Case for one active Student Subject Profile and establish the invariant that an open Case begins with a legal responsible teacher and exactly one pending primary Action.

This is an **authoritative online command**. It is not queueable offline in v1.

## Server entry point

Reference-provider RPC:

```text
create_learning_case(
  p_operation_id,
  p_organization_id,
  p_student_id,
  p_subject_profile_id,
  p_owner_assignment_id,
  p_title,
  p_primary_action_text,
  p_primary_action_due_on?,
  p_source_observation_id?
)
```

The client never supplies actor/responsible-teacher AppUser ids. The server resolves the current application-owned actor through the active `IdentityLink`, then proves that `p_owner_assignment_id` is the actor's live legal teaching assignment for the requested Student Subject Profile.

## Input

Required:

- stable `operation_id`;
- organization;
- Student;
- Student Subject Profile;
- current owner assignment id;
- Case title;
- primary Action text.

Optional:

- primary Action business due date;
- an existing Observation id from the same organization / Student / Subject Profile.

Text limits in v1:

- Case title: 1–500 trimmed characters;
- primary Action text: 1–1000 trimmed characters.

## Authority / responsibility

The command must lock and re-read, inside the same transaction:

- active provider identity link;
- enabled AppUser;
- active organization Membership with teaching capability;
- active Student;
- active Student Subject Profile;
- the exact active Student Teacher Assignment named by `p_owner_assignment_id`.

The initial responsible teacher and primary Action assignee are derived from that legal assignment. Organization visibility or management role alone never creates teaching responsibility.

## Atomic effects

One successful new intent atomically creates:

1. one `LearningCase`:
   - state = `new`;
   - version = 1;
   - responsible teacher bound to the verified assignment;
2. exactly one `PrimaryAction`:
   - role = `primary`;
   - status = `pending`;
   - assigned to the verified responsible teacher;
3. one append-only `case_created` event with actor/provenance;
4. optional source-Observation link when the Observation belongs to the same teaching scope;
5. one operation receipt.

No intermediate Case without its required pending primary Action may commit.

## Idempotency

`operation_id` represents one user intent.

- same actor + same command + semantically equal request → return the committed receipt;
- same `operation_id` with a different actor, command or payload → fail with `XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD`;
- retry must never create a second Case, Action, event or Observation link.

## Source Observation

When `p_source_observation_id` is supplied, the server must prove that the Observation belongs to the same:

- organization;
- Student;
- Student Subject Profile.

The Observation may have been created by a historical/different teacher; provenance remains on the Observation. Linking it does not rewrite its actor or teaching history.

## Result

Authoritative result contains at minimum:

- command = `create_learning_case_v1`;
- operation_id;
- case_id;
- case_state = `new`;
- case_version = 1;
- primary_action_id;
- case_event_id;
- responsible_teacher_app_user_id;
- owner_assignment_id;
- organization_id;
- student_id;
- subject_profile_id;
- subject_key;
- optional source_observation_id;
- server_committed_at.

## Errors

Stable domain errors include:

- `XQ_OPERATION_ID_REQUIRED`;
- `XQ_TEACHING_CONTEXT_REQUIRED`;
- `XQ_INVALID_CASE_TITLE`;
- `XQ_INVALID_PRIMARY_ACTION`;
- `XQ_AUTH_REQUIRED`;
- `XQ_ACTOR_NOT_FOUND`;
- `XQ_ACTOR_DISABLED`;
- `XQ_MEMBERSHIP_REQUIRED`;
- `XQ_MEMBERSHIP_DISABLED`;
- `XQ_TEACHING_CAPABILITY_REQUIRED`;
- `XQ_STUDENT_NOT_IN_ORG`;
- `XQ_SUBJECT_PROFILE_REQUIRED`;
- `XQ_TEACHER_ASSIGNMENT_REQUIRED`;
- `XQ_SOURCE_OBSERVATION_SCOPE_MISMATCH`;
- `XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD`.

## Non-goals

This command does not:

- confirm/transition/stabilize/close/reopen a Case;
- complete/reschedule/replace the primary Action;
- add Intervention/Assessment/Evidence facts;
- reassign responsibility;
- create a Case on behalf of another teacher;
- run offline;
- define Today or Chronicle projections.

Those are subsequent vertical slices and must preserve the invariants established here.
