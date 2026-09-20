# Observation Attachment v1

Status: **Phase 1 candidate — backend + Android executable evidence required**

## Purpose

Add a private image attachment to an already-authoritatively-committed Observation without coupling attachment failure to the safety of the Observation text.

An Attachment is media/provenance attached to a teaching fact. It is **not automatically a finalized Evidence item** and must not silently change Learning Case state, Assessment, Verification or Action.

## Canonical product flow

```text
teacher text
→ protected local draft
→ optional image selected
→ image copied into app-owned protected staging
→ Observation intent safely queued/submitted
→ authoritative Observation receipt supplies observation_id
→ staged image upload
→ CommitObservationAttachment
→ authoritative attachment metadata
```

The text path and attachment path are intentionally independent after staging.

A staging/upload/attachment-commit failure must never destroy safely captured text or invalidate an already committed Observation.

## Local attachment identity

The client generates one stable `attachment_id` UUID when staging begins.

The same attachment id survives:

- process death;
- Observation queue/retry;
- upload retry;
- attachment commit retry.

The final metadata command has its own stable `operation_id`.

Do not mint a replacement attachment id or operation id merely because an upload or response result is unknown.

## V1 media scope

V1 accepts still images only.

Candidate upload MIME types:

- `image/jpeg`;
- `image/png`;
- `image/webp`.

V1 clients must stage an application-controlled derivative rather than depending on long-lived access to an external picker URI.

The Android implementation must remove unneeded source metadata such as GPS/device EXIF from the staged upload derivative while preserving useful orientation/content.

Exact dimension/compression policy remains device-evidence-driven and must not silently make teacher-visible text unreadable.

## Protected local staging

“Selected” is not “safe”.

The UI may say the attachment is locally safe only after the bytes and staging metadata are durably stored inside the exact environment/AppUser/organization/student/profile draft scope.

Protected text and staged image metadata/bytes have separate durability state.

Required failure rule:

> Attachment staging failure preserves the current text and leaves the teacher able to save/submit text without the image.

## Parent Observation

V1 uploads begin only after the parent Observation has an authoritative receipt and therefore a server-generated `observation_id`.

The parent Observation must match:

- organization;
- Student;
- subject profile;
- assignment;
- application actor.

V1 does not append a new attachment to an Observation originally authored by another teacher, even if the current teacher can read that historical Observation.

A current teacher may instead create a new Observation/Evidence fact with their own provenance.

## Storage object

Reference-provider bucket:

`teaching-attachments-v1`

The bucket is private.

Canonical immutable object name:

```text
v1/org/{organization_id}/student/{student_id}/profile/{subject_profile_id}/observation/{observation_id}/attachment/{attachment_id}
```

Properties:

- application-owned UUIDs only;
- no Student/teacher display names;
- no original filename;
- no provider subject in the object name;
- `upsert = false`;
- committed object paths are immutable in V1.

The server re-derives the object name from authoritative IDs. A client-supplied arbitrary object path is never authoritative.

## Upload authorization

Storage upload must fail closed unless the live provider identity resolves through IdentityLink to an enabled AppUser that currently has:

- active teaching-capable organization Membership;
- active Student;
- active Subject Profile;
- active exact StudentTeacherAssignment;
- an existing parent Observation in that exact scope;
- parent Observation actor equal to the resolved AppUser.

Organization owner/admin status alone is insufficient.

## Read authorization

A current legal teacher assignment for the Student/subject scope may read committed historical attachments in that scope even when an older attachment was authored by another teacher.

Historical actor/assignment provenance is preserved and does not become current write authority.

## Commit boundary

Uploading bytes is **not** authoritative attachment completion.

After upload, the client calls `CommitObservationAttachment v1`. The server:

1. re-runs live identity + Teaching Fact Gate;
2. locks/re-reads the parent Observation;
3. verifies the Observation belongs to the current actor in V1;
4. derives the canonical private Storage object name;
5. verifies the exact Storage object metadata exists;
6. validates allowed MIME type and positive bounded size;
7. appends immutable `observation_attachments` metadata;
8. commits an operation receipt atomically.

Only then may the UI present the attachment as remotely committed.

## Failure / recovery

Local staging failed:
- text remains visible/safe;
- no upload is attempted.

Observation pending:
- image remains staged locally;
- upload waits for authoritative Observation id.

Upload failed:
- Observation remains accepted;
- staged image remains retryable;
- same object name is used.

Upload succeeded, commit result unknown:
- keep the same attachment `operation_id`;
- retry/resolve the same commit;
- never upload to a replacement path merely to “try again”.

Upload succeeded, server deterministically rejects commit:
- preserve staged local content according to retention policy;
- surface the business reason;
- do not create a fake committed attachment.

## Orphan object policy

A Storage object with no committed `observation_attachments` row is not domain-visible.

Authenticated clients receive no general delete/update capability in V1. A privileged retention/cleanup mechanism for stale uncommitted objects is required before production release and belongs to the provider-storage / backup-retention gate.

## Evidence relationship

Observation Attachment v1 deliberately does not create a finalized Evidence domain fact.

Later Evidence commands may reference an Observation and/or committed Attachment while preserving the original actor/time/provenance.

No UI may infer “Evidence verified” from the existence of an image.
