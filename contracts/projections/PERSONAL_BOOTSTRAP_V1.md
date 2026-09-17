# PersonalBootstrap v1

## Purpose

`PersonalBootstrap v1` is the minimal authorization-scoped read projection required to replace Android Quick Capture fixture identifiers with real current teaching context.

It is a snapshot for product composition, **not** an authorization proof. `CreateObservation v1` still re-runs the full live Teaching Fact Gate when a queued observation reaches the server.

## Server entry point

Reference-provider RPC:

```text
get_personal_bootstrap_v1()
```

The actor is derived from the authenticated provider subject. The client supplies no AppUser, organization, student, profile or assignment selector to this RPC.

## Envelope

```text
contract = personal_bootstrap_v1
generated_at_server

actor
  app_user_id
  display_name

organizations[]
  organization_id
  name
  can_teach

teaching_contexts[]
  organization_id
  student_id
  student_display_name
  subject_profile_id
  subject_key
  assignment_id
```

## Inclusion rules

- `actor` is the enabled application-owned AppUser mapped from the live authenticated subject.
- `organizations` contains active memberships only.
- `teaching_contexts` contains only contexts where the current actor has an active teaching-capable membership, active Student, active Student Subject Profile and active Student Teacher Assignment.
- An active membership without a legal assignment may appear in `organizations` while contributing no `teaching_contexts`.
- Provider Auth UUIDs never become business identity in this projection; clients receive the stable application-owned `app_user_id`.

## Client interpretation

Clients must treat an unknown contract version or malformed cross-scope payload as a protocol failure. A teaching context must reference an organization present in the same envelope with `can_teach = true`.

Clients may use this projection to choose a Draft/submit scope. They must never infer that an old cached context still grants permission to append a teaching fact.

## Non-goals

V1 intentionally does not include Today items, Cases, Actions, attachments, organization management, generic pagination, a change cursor or a full session/auth implementation.
