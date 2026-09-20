# Provider Storage Conformance v1

## Purpose

Provider Storage Conformance freezes the provider-neutral authorization semantics of Xueqing's accepted Observation Attachment storage path.

A storage/auth provider may own transport, bucket/container implementation and provider-internal object metadata. It does not own Xueqing business identity, teaching scope, Attachment commit truth or teacher-handoff policy.

## Covered V1 path

The V1 matrix covers the existing private Observation Attachment flow:

```text
authenticated external identity
  -> IdentityLink
  -> application-owned AppUser
  -> live Teaching Fact / Assignment authority
  -> immutable private object upload
  -> CommitObservationAttachment v1
  -> committed Attachment read authority
```

No new Attachment product capability is introduced by this gate.

## Provider-identity equivalence fixture

The executable reference-provider test:

1. creates two fictional real Auth users and obtains two provider-issued sessions;
2. maps both external identity tuples to the same application-owned teacher AppUser;
3. deliberately changes legacy `app_users.auth_subject` to a decoy value;
4. creates one authoritative Observation through identity A;
5. uploads two canonical immutable objects, one through identity A and one through identity B;
6. commits those objects cross-identity through `CommitObservationAttachment v1`;
7. proves both sessions can read the committed objects because they resolve to the same live AppUser/Assignment;
8. disables only identity A's `IdentityLink`;
9. proves identity A immediately loses Storage read/upload authority while identity B remains valid.

All data are fictional and local/CI-only.

## Authorization semantics

Storage authorization is derived from application-owned business facts, never from a provider subject becoming a teacher id.

For Observation Attachment V1:

- upload requires an enabled AppUser, active teaching-capable Membership, exact active Student Teacher Assignment, and a parent Observation authored by that same AppUser in the same scope;
- read requires current live teaching authority for the committed Attachment's Student/subject scope, preserving the accepted teacher-handoff policy;
- provider identity revocation affects only that external identity link;
- management role alone does not create teaching authority;
- unauthenticated/private-object access remains denied.

The existing `observation_attachment_storage_conformance.sh` remains the authoritative matrix for different-AppUser denial, immutable overwrite denial, teacher handoff, former-teacher read revoke and database receipt atomicity. This provider gate composes with it rather than duplicating it.

## Final migrated database boundary

Conformance is evaluated against the final migrated PostgreSQL definitions, not superseded historical migration text.

The active Storage authorization helpers:

- `xq_internal.can_upload_observation_attachment_v1(text)`;
- `xq_internal.can_read_observation_attachment_v1(text)`;

must not call reference-provider helpers such as `auth.jwt()` or `auth.uid()` directly. Provider identity extraction belongs behind `xq_internal.current_external_identity_v1()`.

## Storage locator semantics

The current logical namespace is:

- bucket / namespace: `teaching-attachments-v1`;
- object key:
  `v1/org/{organization_id}/student/{student_id}/profile/{subject_profile_id}/observation/{observation_id}/attachment/{attachment_id}`.

These values identify Xueqing Attachment objects and are not authorization tokens. A future provider adapter may map the logical namespace/key to different physical storage primitives without changing Domain/ViewModel identity or teaching rules.

Provider endpoint URLs, service credentials, provider user subjects and provider-internal ownership metadata are Infrastructure concerns and must not become application business identity.

## Commit boundary

Successful byte upload is not Xueqing Attachment commit truth.

`CommitObservationAttachment v1` remains the authoritative database boundary that validates the canonical object, reads provider Storage metadata, appends immutable application metadata and writes the operation receipt atomically.

Upload ResultUnknown may leave an uncommitted object. Clients must preserve the stable Attachment/object identity and must not invent a second logical object merely because transport success was uncertain.

## Non-goals

This gate does not:

- select a second or production Storage provider;
- make the bucket public;
- add signed/public URL product surfaces;
- add generic file management;
- redesign Evidence;
- change Android Attachment UX;
- choose production region/data residency;
- replace the existing Attachment retention/orphan policy.
