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
pg_dump --format=custom --no-owner --no-acl
```

The manifest records:

- tool + version;
- archive-relative path;
- byte length;
- SHA-256;
- exact repository source commit;
- exact migration list/count;
- schema fingerprint;
- representative row counts used for restore reconciliation.

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

## Restore acceptance

A manifest-valid archive is only **backup-valid**. It is not yet **restore-accepted**.

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
