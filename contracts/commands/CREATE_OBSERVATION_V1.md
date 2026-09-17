# CreateObservation v1

`CreateObservation` is the first frozen write contract in Xueqing Native. It appends one low-risk teaching fact; it does **not** create a Learning Case, Evidence item, Assessment, Action, attachment, or realtime event.

## Request

The client sends one stable intent:

```text
operation_id             UUID, generated once and reused for retry/result-resolution
organization_id          UUID
student_id               UUID
subject_profile_id       UUID
assignment_id            UUID
raw_text                 1..10000 chars; original teacher text is preserved
client_captured_at       optional timestamp supplied as capture metadata only
client_capture_metadata  JSON object; provider-neutral capture metadata
```

`actor_app_user_id` is deliberately **not client-authoritative**. The server derives the application-owned actor from the authenticated provider subject, then records that actor in both the request fingerprint payload and authoritative result.

`subject_profile_id` identifies the subject context; the server re-reads its canonical `subject_key` and returns it in the receipt.

## Authoritative Teaching Fact Gate

Before any new Observation can commit, the server must re-read and lock the relevant live rows and prove:

1. authenticated provider subject maps to an enabled AppUser;
2. the AppUser has an active Membership for `organization_id`;
3. the Membership has teaching capability;
4. the Student is active and belongs to that organization;
5. the StudentSubjectProfile is active and belongs to that Student + organization;
6. `assignment_id` is an active StudentTeacherAssignment for exactly that actor + Student + profile + organization.

Owner/admin/supervision authority is not a substitute for step 6.

## Idempotency

The server serializes by `operation_id` and stores an operation receipt in the same database transaction as the Observation.

- same `operation_id` + same actor + semantically equal request payload → return the committed `result_payload` with no new side effect;
- same `operation_id` + different actor or payload → `XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD`;
- timeout or unknown client result → retry/resolve with the original `operation_id`; never mint a new operation merely because the response was lost.

JSON metadata is stored/compared as `jsonb`, so object key ordering does not make an otherwise equal retry a different payload.

## Result / receipt

```text
command               "create_observation_v1"
operation_id
observation_id         server-generated UUID
actor_app_user_id      server-derived application actor
organization_id
student_id
subject_profile_id
subject_key            server-read canonical subject key
server_committed_at    server timestamp
```

The receipt is authoritative evidence that the server accepted the command. It is semantically different from an Android local draft or a future local Outbox row.

## Transaction boundary

Observation append + operation receipt commit atomically in one PostgreSQL transaction. No client sequence of CRUD calls may emulate this command.

The initial Supabase implementation intentionally exposes no direct table CRUD to `anon` or `authenticated`. `public.create_observation` is the sole mutation RPC in this slice. It is an explicit `SECURITY DEFINER` exception because it must read authorization/assignment rows hidden from clients; it pins `search_path=''`, schema-qualifies all relations/functions, re-authorizes from `auth.uid()`, and grants execute only to `authenticated`.
