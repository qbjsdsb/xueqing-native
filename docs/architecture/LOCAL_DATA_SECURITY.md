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

The Windows production-shaped Durable Intent SQLite boundary is accepted when PR #8's exact-head gates pass:

- `Microsoft.Data.Sqlite.Core` + `SQLite3MC.PCLRaw.bundle` 2.4.0;
- whole-database encryption with a random 256-bit master key;
- DPAPI `CurrentUser` wrapping for the production Windows key slot;
- no plaintext SQLite fallback;
- missing/corrupt/unusable wrapped keys fail closed;
- raw DB/WAL marker scans and wrong-key tests;
- encrypted Outbox reopen, claim/lease/retry/ACK/dead-letter semantics remain unchanged;
- MSIX native-library packaging and v1-to-v2 encrypted-data survival are independently exercised by the packaging gate.

This closes the Windows **Durable Intent database encryption provider** decision. It does not authorize real data by itself and does not imply that Projection Cache, attachments, offline authorization, backup/restore or account-switch lifecycle policies are complete.

## Android encryption

Android still requires its own maintained Room-compatible encryption + Android Keystore Spike and lifecycle/recovery evidence. Windows acceptance must not be copied mechanically because the OS key-management and packaging boundaries differ.

## Offline Access Lease

Cached sensitive data is usable only while a finite authorization lease can be trusted. The lease binds at minimum user, organization, environment/issuer, installation/device scope and expiry/validity evidence. When expired or trust is lost, encrypted unsynced intent may be preserved according to policy but the cached student workspace is locked until reauthorization.

A server-side permission reduction cannot remotely erase an offline device instantly; document this limitation and reduce exposure through finite leases, encryption, minimal cache scope and mandatory purge/lockout after validation.

## Scope and lifecycle

Encryption does not replace scope isolation. Real app composition must bind local state at minimum to environment + app user + organization + installation/device scope. Account, environment and organization switches must never reopen another scope's Projection Cache or drafts by accident.

Projection Cache may be purged/rebuilt after authorization or schema-generation changes. Encrypted Durable Intent requires a non-destructive recovery/migration policy and cannot be silently discarded merely because cache state is stale.

## Backup

Sensitive Projection Cache, Durable Intent and attachment staging must not silently participate in OS cloud backup/device migration unless a specific encrypted restore design is approved. A restored old backup must not immediately expose sensitive cache without authorization revalidation.
