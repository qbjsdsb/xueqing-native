# Local Data Security

Local-first functionality introduces sensitive educational data onto devices and is a first-class security boundary.

## Production hard questions

Before real data, freeze:

- fields/projections allowed offline;
- finite Offline Access Lease duration and reauthorization behavior;
- lease resistance to wall-clock rollback and restored old backups;
- account/environment/organization switch cleanup;
- attachment staging/cache TTL and protection;
- diagnostics redaction;
- system cloud-backup/device-migration exclusion;
- export and privacy-deletion behavior.

## Local state split

Projection Cache is disposable and may be purged/rebuilt. Durable Intent contains drafts/outbox/pending attachment state/operation recovery and cannot use destructive migration fallbacks.

## Windows Durable Intent database encryption

The Windows production-shaped Durable Intent SQLite boundary is accepted:

- `Microsoft.Data.Sqlite.Core` + `SQLite3MC.PCLRaw.bundle` 2.4.0;
- whole-database encryption with a random 256-bit master key;
- DPAPI `CurrentUser` wrapping for the production Windows key slot;
- no plaintext SQLite fallback;
- missing/corrupt/unusable wrapped keys fail closed;
- raw DB/WAL marker scans and wrong-key tests;
- encrypted Outbox reopen, claim/lease/retry/ACK/dead-letter semantics remain unchanged;
- MSIX native-library packaging and v1-to-v2 encrypted-data survival are independently exercised by the packaging gate.

This closes the Windows **Durable Intent database encryption provider** decision. It does not authorize real data by itself and does not imply that Projection Cache, attachments, offline authorization, backup/restore or account-switch lifecycle policies are complete.

## Android Durable Intent encryption

The Android encrypted Durable Intent baseline is accepted:

- Room 2.8.5;
- SQLCipher for Android Community 4.17.0;
- a random 32-byte SQLCipher database password;
- Android Keystore non-exportable AES-256 key wrapping that password through AES-GCM;
- wrapped envelope in `noBackupFilesDir`;
- no plaintext fallback;
- missing/corrupt envelope or missing Keystore key fails closed;
- explicit purge removes DB sidecars, wrapped envelope and Keystore alias;
- backup/device-transfer exclusion remains active until the dedicated backup/restore gate.

API 36 device evidence covers encrypted reopen, marker scans, purge and force-stop/relaunch draft recovery. Encryption acceptance still does not authorize cached projection reads indefinitely while offline.

## Offline Access Lease

Cached sensitive projection data is usable only while a finite authorization lease can be trusted. The active Phase 1 spike is specified in `contracts/security/OFFLINE_ACCESS_LEASE_V1.md`.

The lease binds at minimum:

- environment / provider trust domain;
- application-owned user;
- organization;
- installation / validated boot session;
- server-issued time and expiry evidence.

Phase 1 candidate policy:

- maximum lease duration: 72 hours;
- tolerate at most five minutes of small backward wall-clock correction;
- derive elapsed lease time from a monotonic clock during the validated boot session;
- boot-session change or monotonic reset while offline fails closed and requires online revalidation.

A server-side permission reduction cannot remotely erase an offline device instantly. The finite lease bounds exposure while encryption and minimum cache scope reduce the impact.

When lease trust is lost:

- cached student/organization projection surfaces are locked until reauthorization;
- the client must not disguise authorization loss as a harmless empty state;
- Projection Cache may be invalidated or rebuilt;
- encrypted unsynced Durable Intent is preserved according to its own scope/recovery policy.

The lease is never a server credential. CreateObservation and future domain commands still re-run live authorization and command-specific invariants.

## Scope and lifecycle

Encryption does not replace scope isolation. Real app composition must bind local state at minimum to environment + app user + organization + installation/device scope. Account, environment and organization switches must never reopen another scope's Projection Cache or drafts by accident.

Projection Cache may be purged/rebuilt after authorization or schema-generation changes. Encrypted Durable Intent requires a non-destructive recovery/migration policy and cannot be silently discarded merely because cache state is stale.

## Backup

Sensitive Projection Cache, Durable Intent and attachment staging must not silently participate in OS cloud backup/device migration unless a specific encrypted restore design is approved. A restored old backup must not immediately expose sensitive cache without authorization revalidation.
