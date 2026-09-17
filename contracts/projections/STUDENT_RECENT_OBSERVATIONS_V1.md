# Student recent Observations v1

Bounded read projection for the Windows Student workspace, following ADR-0013.
This is the independently testable backend prerequisite of
`windows-student-recent-observation-v1`; it does not complete Windows wiring.

## Request

Reference-provider RPC: `get_student_recent_observations_v1`.

Required UUID arguments: `p_organization_id`, `p_student_id`,
`p_subject_profile_id`. These select a context; they do not confer authority.
The actor is derived exclusively from the authenticated provider subject.

## Envelope

- `contract = student_recent_observations_v1`
- `generated_at_server`: server statement timestamp, not a sync cursor or lease
- `actor_app_user_id`: application-owned identity of the reader
- `organization_id`, `student_id`, `student_display_name`
- `subject_profile_id`, `subject_key`, `assignment_id`: current reader context
- `observations[]`: at most 20 items
- `has_more`: additional history exists; this snapshot is not full history

Each item contains `observation_id`, `actor_app_user_id` (original author),
`raw_text` (unchanged), nullable `client_captured_at`, and
`created_at_server`. Order is `created_at_server DESC, observation_id DESC`.
Client timestamps cannot promote a queued historical observation.
Operation receipts, provider identities and arbitrary capture metadata are excluded.

## Authority and consistency

Require an enabled AppUser, active teaching-capable membership, active Student,
active Subject Profile, and the reader's active matching teaching assignment.
Owner/admin status alone grants nothing in this personal projection.
Every request re-evaluates these rows; clients must never use a prior bootstrap
or projection as authorization.

A current assigned teacher reads authoritative history within that exact
organization/student/profile, including earlier authors and retired assignments.
The historical author's assignment is not the reader's current authorization.
Organization supervision is a separate, unimplemented policy.

The STABLE read function uses the calling statement's MVCC snapshot for both
authority and facts. Revocation committed before a subsequent statement denies
that statement; an already-running read may finish on its earlier snapshot.
This is not a linearizable revocation barrier or a production live-session guard.
Provider Session/Auth conformance remains a separate gate.

Authorized empty history is a successful empty array with `has_more = false`.
A nonexistent or inaccessible scope raises the same
`P0001 / XQ_TEACHING_CONTEXT_UNAVAILABLE` error without revealing existence.
Missing scope raises `XQ_TEACHING_CONTEXT_REQUIRED`; existing actor errors
`XQ_AUTH_REQUIRED`, `XQ_ACTOR_NOT_FOUND`, `XQ_ACTOR_DISABLED` are preserved.
Anonymous execution and direct table access remain denied.

## Client obligations and evidence

Validate contract, reader identity, selected scope, item types, unique IDs,
maximum count and ordering before publishing a response. Ignore additive fields.
Discard responses from an obsolete account/context/request generation.
An error must not be presented as successful empty history. Clear prior visible
facts when access is denied or the selected account/context changes.
This unit adds no offline cache or offline access lease.

The backend pgTAP suite must prove real CreateObservation-to-projection identity,
bounds/order, and organization/profile/assignment/revocation isolation.
Existing `backend-api` CI runs the database suite against an isolated seeded
reference provider. Windows adapter, ViewModel, UI and cross-client E2E are
follow-up work; database tests do not claim those gates.
