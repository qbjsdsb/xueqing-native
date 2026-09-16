# Windows Durable Outbox

Status: Phase 1 production-shaped infrastructure candidate. No production data.

## Goal

Prove that Xueqing can persist a teacher's local command intent before network submission and recover it safely after process restart, timeout or concurrent local send attempts without storing the Durable Intent database in plaintext.

This SQLite database is a local transport/recovery mechanism. PostgreSQL/server command execution remains authoritative for formal business state.

## Accepted Windows Durable Intent database boundary

The original plaintext storage Spike has now been migrated to the Windows local-data security candidate:

- `Microsoft.Data.Sqlite.Core` 10.0.12 rather than the default SQLite bundle;
- `SQLite3MC.PCLRaw.bundle` 2.4.0 as the only SQLitePCLRaw bundle in the production-shaped Infrastructure graph;
- SQLite3 Multiple Ciphers whole-database encryption with a random 256-bit database master key;
- Windows production key storage uses DPAPI `CurrentUser`; only the DPAPI-wrapped key is persisted beside the database;
- an existing database with a missing, corrupt or unusable wrapped key fails closed; no replacement key is silently minted;
- no plaintext SQLite fallback exists;
- test-only fixed-key injection is internal to the friend test assembly so Linux CI can exercise the same SQLite3MC encrypted store without pretending DPAPI is cross-platform;
- connection pooling is disabled for encrypted local-database connections so keyed connections are not retained in a process-wide pool;
- the previous five-second provider busy timeout is preserved;
- WAL journal mode and `synchronous=FULL` remain part of the Durable Intent durability contract.

The native runtime is verified through `sqlite3mc_version()`, and CI scans raw database/WAL bytes for known fictional teaching markers.

## Architecture

- Raw ADO.NET-style SQL keeps Outbox metadata and state transitions explicit and auditable.
- `Xueqing.Windows.Infrastructure` owns SQLite and key protection. `Xueqing.Windows.Core` remains provider-free and contains only sync contracts.
- Short immediate write transactions are preserved. Each operation opens its own encrypted `SqliteConnection`; connections, commands and readers are never shared concurrently.
- The encryption migration changes only the connection/security boundary. Outbox schema and domain semantics are intentionally unchanged.

## Schema semantics

The database uses `PRAGMA user_version = 1` as the local schema version gate.

`outbox_operations` stores:

- DB-generated `local_sequence` for deterministic local queue order;
- unique stable `operation_id`;
- command type, aggregate/scope, expected version/freshness binding and payload;
- diagnostic client timestamp;
- attempt count and queue status;
- retry/error metadata;
- finite claim lease id/expiry;
- acknowledgement time and optional server receipt.

Client timestamps never decide causal order. `local_sequence` is only a local queue ordering aid; the server still decides business concurrency using the command contract.

## Claim/lease model

Ready rows are `pending`, due `retry`, or expired `in_flight` rows. Claim runs inside an immediate transaction and changes exactly one row to `in_flight`, increments its attempt count and creates a new lease.

Only the current lease owner may move the row to retry, acknowledgement or dead letter. An old worker that finishes late after another worker reclaimed the row receives `OutboxLeaseLostException`.

This closes the common crash window:

1. local intent is durably encrypted;
2. worker claims it;
3. server may commit;
4. process can crash before local ACK;
5. lease eventually expires;
6. client retries the same `operation_id`;
7. server returns the committed result instead of applying the mutation twice.

## Acceptance gates

GitHub Actions must prove on the exact candidate head:

1. identical `operation_id` + identical durable intent is idempotent;
2. identical `operation_id` + different semantic intent is rejected;
3. encrypted intent survives reopening the database with the same key;
4. active lease prevents a second claim;
5. expired lease is reclaimable after reopen with the same operation id;
6. stale lease cannot ACK after reclaim;
7. retry is not claimable before its due time;
8. concurrent claimers obtain at most one live lease even with pooling disabled;
9. acknowledged and dead-letter rows are terminal;
10. SQLite runtime reports SQLite3MC 2.4.0;
11. a wrong database key cannot read the schema;
12. known Outbox payload markers are absent from raw database and WAL bytes;
13. Windows DPAPI concurrent first-key creation converges on one authoritative master key;
14. the real Windows Outbox reopens through DPAPI after process/store recreation;
15. corrupt or missing wrapped keys fail closed;
16. the Infrastructure dependency graph restores in `--locked-mode`;
17. existing WinUI cloud compilation and the independent MSIX install/upgrade gate remain green.

## Still open before real data

This acceptance does **not** complete the full local-data-security program. Still separate:

- finite Offline Access Lease and clock/old-backup rollback resistance;
- account/environment/organization switch cleanup and scope binding in the real app composition root;
- Projection Cache policy and authorization-driven purge/rebuild;
- attachment staging encryption/TTL;
- diagnostics redaction;
- OS cloud-backup/device-migration exclusion and approved restore behavior;
- Android local-data encryption and Keystore integration;
- production signing/recovery and backup/restore gates.

## Explicit non-goals

- no production student data;
- no backend command execution yet;
- no generic CRDT or last-write-wins;
- no durable attachment/blob implementation yet;
- no final retry/backoff policy yet;
- no multi-device conflict-resolution claim.
