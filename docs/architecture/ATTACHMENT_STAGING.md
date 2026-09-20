# Attachment Staging and Upload v1 Candidate

## Scope

This architecture note defines the first native attachment slice. It intentionally targets Android Quick Capture + Observation Attachment rather than a generic document subsystem.

## Why attachment is separate from Observation submission

The existing Observation path already proves a strong guarantee:

```text
protected Draft
→ atomic Draft retirement + Observation Outbox insert
→ WorkManager submission
→ authoritative Observation receipt
```

Adding file bytes to that transaction would make text reliability depend on picker/file/network behavior. V1 therefore preserves the proven text path and adds a second durable attachment path.

## Android local model

A staged attachment must eventually have durable metadata equivalent to:

```text
attachment_id
scope_key
draft_epoch / parent capture identity
local_encrypted_path
content_type
byte_size
state
parent_observation_operation_id?
authoritative_observation_id?
remote_object_name?
attachment_commit_operation_id?
last_error_class?
created_at
updated_at
```

Exact Room table naming is implementation detail.

The file bytes must live in app-owned storage and require at-rest protection appropriate for educational data. Do not persist only a Photo Picker URI and assume it remains readable after process death.

## State machine

```text
Selected
→ Staging
→ Staged

Staged
→ WaitingForObservation
→ UploadPending
→ Uploading
→ UploadedUncommitted
→ CommitPending
→ Committed
```

Retryable exits:

```text
Staging -> StagingFailed
Uploading -> UploadRetryable
CommitPending -> CommitResultUnknown / CommitRejected
```

Text draft state remains independent.

## Submit barrier

When the teacher submits Quick Capture:

- latest text must first be durably safe;
- Observation Outbox insertion / draft retirement keeps its existing atomic guarantee;
- staged attachment rows are linked to the exact Observation operation id without deleting their bytes;
- attachment processing waits for the Observation receipt.

This is the point where the current QuickCaptureViewModel durability seam may be extracted into a CaptureCoordinator/repository boundary because attachment staging becomes a real second durability use case.

## Remote processing

After Observation acknowledgement:

1. bind the staged row to authoritative `observation_id`;
2. derive the immutable object name;
3. upload with no upsert;
4. persist upload acknowledgement locally;
5. call CommitObservationAttachment with one stable commit operation id;
6. on receipt, mark committed and permit staged-byte cleanup according to local retention policy.

A process restart at any numbered step must reconstruct the same identities and continue without duplicate business side effects.

## Provider boundary

Storage SDK/HTTP types belong in Infrastructure only.

Presentation sees provider-neutral states such as:

- staging;
- locally safe;
- waiting to upload;
- uploading;
- upload failed;
- confirming remote attachment;
- committed.

## First executable gates

Backend:
- private bucket policy;
- upload authority isolation;
- no overwrite path;
- strict CommitObservationAttachment authorization;
- idempotency;
- atomic metadata+receipt;
- historical read/current-write distinction.

Android:
- picker return;
- protected local file staging;
- text survives staging failure;
- process death after staging;
- Observation accepted while attachment upload fails;
- same attachment id/object name/commit operation survives retry;
- scope isolation across AppUser/organization/student/profile;
- staged bytes encrypted at rest;
- cleanup only after authoritative attachment receipt.

## Not in this slice

- generic Evidence classification;
- video/audio/PDF;
- arbitrary document browser;
- multi-file bulk import;
- public URLs;
- client-side delete of committed media;
- generic resumable-upload framework;
- image AI/OCR;
- cross-provider Storage abstraction beyond the narrow adapter contract required by this slice.
