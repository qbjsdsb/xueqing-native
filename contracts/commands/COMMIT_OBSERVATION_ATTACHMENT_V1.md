# CommitObservationAttachment v1

Status: **Phase 1 command candidate — executable backend evidence required**

## Purpose

Atomically register immutable application metadata for one already-uploaded private image attached to an authoritative Observation.

This command does not upload bytes and does not create a formal Evidence item.

## RPC

```text
commit_observation_attachment(
  p_operation_id,
  p_organization_id,
  p_student_id,
  p_subject_profile_id,
  p_assignment_id,
  p_observation_id,
  p_attachment_id
)
```

The bucket id and object name are server-derived.

## Authorization

The server resolves the current external identity through IdentityLink and requires:

- enabled AppUser;
- active teaching-capable Membership;
- active Student;
- active Subject Profile;
- exact active StudentTeacherAssignment owned by the actor;
- parent Observation matching the exact scope/assignment;
- parent Observation actor equal to the current actor.

V1 therefore does not mutate a historical Observation created by a previous teacher.

## Storage verification

Canonical bucket:

`teaching-attachments-v1`

Canonical object name:

```text
v1/org/{organization_id}/student/{student_id}/profile/{subject_profile_id}/observation/{observation_id}/attachment/{attachment_id}
```

The command verifies the Storage metadata row for that exact object.

Allowed V1 MIME types:

- `image/jpeg`;
- `image/png`;
- `image/webp`.

The server reads MIME type and byte size from Storage metadata; those values are not client-authoritative command inputs.

The object must have a positive size and must satisfy the bucket size policy.

## Idempotency

The command uses the normal Xueqing high-risk write shape:

- stable `operation_id`;
- advisory serialization by operation id;
- same actor + semantically equal request → replay committed receipt;
- operation id reuse with another payload → `XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD`.

`attachment_id` is also unique. A committed attachment id cannot be rebound through a new operation.

## Atomic database mutation

The command atomically:

1. inserts one immutable `observation_attachments` row;
2. inserts one operation receipt.

A forced receipt failure must roll back the metadata row.

The Storage object itself is external to that PostgreSQL transaction. If database commit fails, the uploaded object remains uncommitted/orphaned and is handled by the attachment retention protocol.

## Receipt

```text
command = "commit_observation_attachment_v1"
operation_id
attachment_id
observation_id
actor_app_user_id
organization_id
student_id
subject_profile_id
subject_key
assignment_id
bucket_id
object_name
content_type
byte_size
server_committed_at
```

The receipt is the authoritative signal that the uploaded media is now a committed Observation attachment.
