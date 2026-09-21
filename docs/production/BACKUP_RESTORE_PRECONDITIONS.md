# Backup + Storage Restore Gate — Preconditions

Status: **handoff contract only**.

The active execution line remains **Production Provider Region / Data Residency**.  
This document does not start or complete the Backup/Restore gate. It freezes what the next gate must prove once the production jurisdiction/provider topology is accepted.

## Why database restore is not enough

Xueqing has two authoritative cloud data planes:

1. PostgreSQL business / authorization / command state;
2. private Attachment object bytes.

A database dump can restore Attachment metadata while every image byte is still missing. Conversely, restoring objects without matching database metadata must not fabricate domain truth.

A successful disaster-recovery rehearsal therefore restores a **coherent pair**:

```text
PostgreSQL authoritative state
+ private Attachment object set
+ object manifest / integrity evidence
= recoverable Xueqing cloud state
```

## Restore target

The rehearsal must restore into a **fresh isolated environment**, not back into the source project.

The target starts from:

- empty provider project/environment;
- repository migrations from Git as schema truth;
- no production user sessions copied from a running client;
- no secret from the backup archive hard-coded into source or CI.

The restore procedure must be reproducible from repository-controlled instructions plus protected deployment/backup credentials.

## Business identity continuity

The restore MUST preserve application-owned identity rather than rebuilding identity from provider subjects.

At minimum, verify continuity for:

- Organization ids;
- AppUser ids;
- IdentityLink tuples and active/revoked state;
- Membership role/status/`can_teach`;
- Student ids;
- Student Subject Profile ids;
- StudentTeacherAssignment ids and active/inactive history.

A provider user recreated with a different external subject must not silently become the same Xueqing AppUser unless an authorized IdentityLink operation establishes that mapping.

Revoked/disabled identities must remain revoked/disabled after restore.

## Teaching-history continuity

Restore must preserve the authoritative teaching history and its actor/responsibility semantics.

At minimum, verify representative rows/relations for:

- Observation;
- Learning Case;
- Primary / Next Action state;
- Evidence / Verification state represented by the accepted schema;
- server versions used by optimistic concurrency;
- actor AppUser;
- responsible/assigned teacher relationships;
- closed/reopened Case history.

The rehearsal must include a historical handoff case so a new assigned teacher can read permitted history while a former teacher cannot regain append authority.

## Command/idempotency continuity

Operation identity is disaster-recovery state.

The backup/restore set must preserve every accepted command receipt/table required so that a retry after restore does not duplicate a side effect.

Representative verification must include:

- CreateObservation same-operation replay;
- Learning Case / Action mutation same-operation replay;
- Attachment commit same-operation replay;
- Organization Invitation create/accept semantics;
- Organization Invitation Delivery state.

A restored Invitation Delivery that was already `sent` MUST NOT send a second email merely because the environment was restored.

A restored operation with ResultUnknown/dispatching state must keep the same formal operation identity and follow the accepted recovery protocol.

## Attachment object integrity

The backup process must create a protected object manifest independent of database metadata.

For every backed-up private Attachment object, the manifest must contain at least:

- stable Xueqing logical object locator;
- byte length;
- MIME type;
- cryptographic digest computed from the backed-up bytes.

The restore rehearsal must verify:

- every committed Attachment metadata row resolves to exactly one restored object;
- the restored object digest and byte length match the backup manifest;
- no unexpected extra object is silently promoted into committed domain state;
- bucket/container remains private;
- restored authorization still uses Xueqing Teaching Fact / IdentityLink semantics rather than backup-system ACLs.

The backup manifest itself may contain sensitive object locators and is treated as production data.

## Database / object consistency point

The gate must define how the database backup and object backup are related in time.

Do not invent a claim of fully atomic cross-service snapshot if the provider does not support one.

An accepted strategy must document one of:

- a provider-supported coordinated snapshot; or
- an application quiescence/maintenance procedure; or
- a deterministic reconciliation protocol that identifies objects/metadata created around the backup boundary and restores to a documented consistency point.

RPO is measured from evidence; it is not assumed to be zero.

## Security of backup material

Production backup material must never be written to:

- the public Git repository;
- public GitHub Actions artifacts;
- issue/PR attachments;
- ordinary app diagnostics.

Requirements:

- encryption at rest for backup archives;
- backup decryption key stored separately from the archive;
- least-privilege backup/restore credentials;
- credentials revocable independently of production client credentials;
- integrity verification before restore;
- auditable backup and restore execution;
- documented deletion/retention handling.

The zero-paid-dependency goal does not permit an unencrypted consumer-cloud folder or public CI artifact to become the production backup system.

## Provider independence

Git migrations remain schema truth.

The restore gate should prefer logical, provider-portable backup formats where practical, while preserving PostgreSQL semantics Xueqing actually depends on.

Provider-specific commands are allowed inside Infrastructure/operations tooling, but the acceptance result is provider-neutral:

- identities remain correct;
- authority remains correct;
- history remains continuous;
- operation replay remains idempotent;
- Attachment bytes remain complete and private.

Changing production provider is not required by this gate.

## Required negative tests after restore

At minimum:

- external identity with no active IdentityLink is denied;
- disabled/revoked identity is denied;
- cross-Organization read is denied;
- teacher without live assignment cannot append a Teaching Fact;
- former teacher after handoff cannot append to the Student scope;
- anonymous private Attachment read is denied;
- an already-sent Invitation Delivery replay does not send another message;
- an operation id reused with a different payload remains rejected.

A restore that makes data readable but weakens authorization is a failed restore.

## Fresh-environment functional proof

After restore, run the accepted reference-provider/product conformance set against the fresh target, including:

- PersonalBootstrap / projection reads;
- Organization Management;
- Observation;
- Learning Case / Current Focus / Today;
- Action progression/lifecycle;
- Attachment read/commit authorization;
- Invitation create/delivery/acceptance.

Use fictional rehearsal data until production approval.

## RPO / RTO evidence

Do not invent final RPO or RTO budgets before a real rehearsal.

The first full rehearsal records:

- backup duration;
- object-backup duration;
- restore duration;
- integrity-validation duration;
- maximum measured data-loss window;
- manual steps;
- provider/tool limitations.

A later production policy may tighten those numbers.

## Retention and privacy

Backup retention must align with the accepted data-lifecycle policy.

Privacy deletion/erasure cannot be documented as "delete live row only." The procedure must state how the subject's data ages out of retained backups and when old backups expire.

Append-only teaching history and legal/privacy deletion are separate concerns and must not be conflated.

## Evidence required to close the future gate

The future Backup/Restore PR must record:

- exact source fixture/environment revision;
- exact repository commit/migration set;
- backup tool versions;
- database backup manifest;
- private object backup manifest with digests;
- fresh restore target;
- restore logs scrubbed of secrets/PII;
- post-restore row/object counts;
- digest verification;
- authorization negative tests;
- same-operation replay tests;
- product/provider conformance results;
- measured RPO/RTO evidence;
- known limitations.

## Preconditions before the future gate starts

Do not start a production-target restore rehearsal until the active residency gate has accepted:

- target jurisdiction;
- exact production primary region/provider;
- Attachment object-residency policy;
- trusted function/gateway transit policy;
- log/diagnostic policy;
- allowed backup destination/residency class.

Until then, only fictional/local rehearsal tooling may be developed.
