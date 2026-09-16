# Local Data Security

Local-first functionality introduces sensitive educational data onto devices and is a first-class security boundary.

## Production hard questions

Before real data, freeze:

- fields/projections allowed offline;
- finite Offline Access Lease duration and reauthorization behavior;
- lease resistance to wall-clock rollback and restored old backups;
- whole-DB vs selected-field encryption and OS-backed key storage;
- account/environment/organization switch cleanup;
- attachment staging/cache TTL;
- diagnostics redaction;
- system cloud-backup/device-migration exclusion;
- export and privacy-deletion behavior.

## Local state split

Projection Cache is disposable and may be purged/rebuilt. Durable Intent contains drafts/outbox/pending attachment state/operation recovery and cannot use destructive migration fallbacks.

## Offline Access Lease

Cached sensitive data is usable only while a finite authorization lease can be trusted. The lease binds at minimum user, organization, environment/issuer, installation/device scope and expiry/validity evidence. When expired or trust is lost, encrypted unsynced intent may be preserved according to policy but the cached student workspace is locked until reauthorization.

A server-side permission reduction cannot remotely erase an offline device instantly; document this limitation and reduce exposure through finite leases, encryption, minimal cache scope and mandatory purge/lockout after validation.

## Encryption Spike

Android evaluates maintained Room-compatible encryption + Android Keystore. Windows compares maintained SQLCipher/SQLite3MC-style options against a minimal SQLite + Windows cryptography/DPAPI design, including CI/MSIX/migration/recovery/performance evidence.

## Backup

Sensitive Projection Cache, Durable Intent and attachment staging must not silently participate in OS cloud backup/device migration unless a specific encrypted restore design is approved. A restored old backup must not immediately expose sensitive cache without authorization revalidation.
