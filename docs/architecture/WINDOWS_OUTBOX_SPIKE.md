# Windows Durable Outbox Spike

Status: Phase 1 architecture spike. No production data.

## Goal

Prove that Xueqing can persist a teacher's local command intent before network submission and recover it safely after process restart, timeout or concurrent local send attempts.

This SQLite database is a local transport/cache mechanism. PostgreSQL/server command execution remains authoritative for formal business state.

## Technology decision for this spike

- `Microsoft.Data.Sqlite` 10.0.12, the current stable .NET 10 line as of 2026-09-16.
- Raw ADO.NET-style SQL for Outbox metadata and transitions. This keeps the exactly-once/idempotency mechanics explicit and auditable.
- A separate `Xueqing.Windows.Infrastructure` project owns SQLite. `Xueqing.Windows.Core` remains free of provider packages and contains only sync contracts.
- WAL journal mode.
- `synchronous=FULL` on every connection because Outbox represents user intent that should survive OS crash/power loss, not merely application-process restart.
- Short immediate write transactions. Each operation opens its own `SqliteConnection`; connections/commands/readers are never shared concurrently.
- Provider busy/locked retry is bounded by a five-second default timeout.

The final local-encryption provider is intentionally **not** selected here. The encryption/security Spike remains a separate production gate and may change the SQLite native provider/bundle without changing the Outbox contract.

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

1. local intent is durable;
2. worker claims it;
3. server may commit;
4. process can crash before local ACK;
5. lease eventually expires;
6. client retries the same `operation_id`;
7. server returns the committed result instead of applying the mutation twice.

## Acceptance tests

The Spike must prove on GitHub Actions:

1. identical `operation_id` + identical durable intent is idempotent;
2. identical `operation_id` + different semantic intent is rejected;
3. intent survives reopening the database with a new store instance;
4. active lease prevents a second claim;
5. expired lease is reclaimable after reopen with the same operation id;
6. stale lease cannot ACK after reclaim;
7. retry is not claimable before its due time;
8. concurrent claimers obtain at most one live lease;
9. acknowledged and dead-letter rows are terminal;
10. existing WinUI cloud compilation remains green.

## Explicit non-goals

- no production student data;
- no backend command execution yet;
- no claim that SQLite encryption is solved;
- no generic CRDT or last-write-wins;
- no durable attachment/blob implementation yet;
- no final retry/backoff policy yet;
- no multi-device conflict-resolution claim.
