# Local-first Command Sync Protocol

## Goal

Give teachers immediate, resilient UX without converting Xueqing into a generic multi-master database.

PostgreSQL is authoritative for formal business state. Local databases are authoritative only for local UI state, queued commands, protected drafts and cached server projections within a valid offline-access policy.

## Push / Outbox

A queued command envelope should contain at least:

```text
operation_id
command_type
aggregate_id / scope
expected_version or freshness binding
payload
created_at_local (diagnostic only)
attempt_count
queue_status
last_error_class
```

`created_at_local` is not a conflict resolver.

Retry rules:

- retry transient network/server failures with the same `operation_id`;
- on unknown result, query operation receipt/result first;
- domain conflicts are not automatically retried as new intent;
- authorization/entity-state denial is surfaced and may require local draft preservation or purge.

## Pull

Do not depend on wall-clock timestamp comparison alone. The backend spike must define a monotonic or opaque `change_cursor`/equivalent server-issued token.

Pull must also validate current authorization scope. A separate `access_scope_version`/equivalent is recommended so membership/assignment changes can trigger cache reconciliation/purge even when the affected business rows did not themselves mutate.

## Conflict model

Lifecycle/governance conflicts use server versions/current snapshots. No generic Last Write Wins.

Example: local close expected Case v17 while server is v18 → return version conflict with the current server projection; the user reviews/retries a new explicit intent.

## Realtime

Realtime may later improve freshness but must never be required for correctness, authorization revocation or exactly-once semantics.

## Offline authorization

Cached sensitive data is available only while an Offline Access Lease is valid. Lease semantics and duration are frozen only after security Spike. On next online validation, reduced scope must trigger local purge/lockout of no-longer-authorized cached records.