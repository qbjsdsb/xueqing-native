# Backup / Restore v1

Status: **active Phase 1 contract**
Tracker: **#76**

## Purpose

Define the provider-portable backup representation and fail-closed acceptance rules before any protected production backup execution is introduced.

This contract is intentionally stricter than "pg_dump succeeded".

A recoverable Xueqing backup is:

```text
logical PostgreSQL archive
+ private Attachment object bytes
+ integrity manifest
+ consistency boundary
+ encryption / key-separation evidence
```

## Archive manifest

The machine-readable contract is:

`contracts/schemas/BACKUP_ARCHIVE_MANIFEST_V1.schema.json`

The repository validator is:

`tools/backup/verify_backup_manifest.py`

The manifest itself is production data when created from production. Production manifests and archives MUST NOT be committed or uploaded as ordinary public CI artifacts.

## PostgreSQL representation

V1 uses logical PostgreSQL custom-format export:

```text
pg_dump --format=custom --data-only --schema=public --no-owner --no-acl
```

The manifest records:

- tool + version;
- archive-relative path;
- byte length;
- SHA-256;
- exact repository source commit;
- exact migration list/count;
- schema fingerprint;
- all public business-table row counts used for restore reconciliation;
- all public business-table canonical content SHA-256 fingerprints.

Git migrations remain schema truth. The dump is authoritative state, not schema-source history.

## Private Storage representation

Database backup does not contain Attachment bytes.

The accepted object backup must preserve each private object byte-for-byte and record:

- Xueqing logical object locator;
- private bucket id;
- archive-relative object path;
- byte length;
- MIME type;
- SHA-256.

The manifest requires `teaching-attachments-v1` to remain private.

Duplicate locators or archive paths are invalid. Absolute/traversal paths are invalid.

## Consistency strategy

V1 accepts exactly one declared strategy per archive:

- `quiesced-window`; or
- `deterministic-reconciliation`.

The manifest records database/object start and finish timestamps plus a measured maximum data-loss window.

Do not claim RPO=0 unless evidence proves it.

## Security boundary

Every accepted production archive must state:

- archive encryption is enabled;
- encryption scheme is explicit;
- decryption key/recipient material is stored separately;
- production backup material is not placed in public CI.

The manifest must not contain provider admin secrets, database passwords, access/refresh tokens or backup credentials.

The validator rejects secret-like fields and values.

## Provider identity recovery

Provider-owned Auth users, provider sessions and access/refresh tokens are **not** canonical Xueqing business identity and are not restored as authoritative application state.

The backup preserves application-owned:

- `AppUser`;
- `IdentityLink` history and active/revoked state;
- Membership / teaching authority;
- historical actor ids and operation receipts.

A fresh provider environment may issue a different `(provider, issuer, external_subject)` tuple for the same human. That new provider identity MUST NOT silently inherit an existing AppUser.

Fresh-target recovery must prove this sequence:

1. a newly created provider identity is denied before an authorized relink;
2. the recovery operation explicitly deactivates/supersedes the old active provider link as required by the migration policy;
3. the new provider tuple is linked to the same canonical AppUser through an auditable recovery/onboarding operation;
4. exactly one intended active provider link remains for that recovered identity scope;
5. only after relink may the fresh provider session exercise the restored AppUser authority;
6. previously revoked/disabled links remain revoked/disabled and are never reactivated merely because a backup was restored.

A provider subject is therefore recoverable **identity evidence**, not the business primary key. A changed provider subject after disaster recovery does not rewrite historical `actor_app_user_id`, Membership, Assignment, Case, Action, Observation or operation receipt identity.

## Restore acceptance

A manifest-valid archive is only **backup-valid**. It is not yet **restore-accepted**.

Fresh restore MUST reproduce the exact public business-table set, row counts and canonical row-content fingerprints before provider identity relink or product replay is attempted. Equal row counts with different ids/content are a failed restore.

Issue #76 closes only after a fresh isolated target proves:

- migrations + logical restore succeed;
- all committed Attachment metadata resolves to exactly one restored object;
- byte length and SHA-256 match;
- authorization negative tests remain denied;
- same-operation replay remains idempotent;
- same operation id + different payload remains rejected;
- sent Invitation Delivery does not send again;
- provider/product conformance remains green;
- measured backup/restore durations and consistency window are recorded.

## Provider portability

The manifest uses Xueqing logical identity and portable PostgreSQL/object concepts. It must not make Supabase subjects or provider-internal object ACLs canonical business identity.

Provider-specific export/import tooling may exist in operations code, but the archive contract remains reusable by the later CloudBase conformance/migration gate.

## Non-goals

- no active-active replication;
- no dual-write;
- no public production backup artifact;
- no real-data fixture in repository;
- no claim that a valid manifest proves a successful restore;
- no weakening of authorization/idempotency to simplify recovery.
