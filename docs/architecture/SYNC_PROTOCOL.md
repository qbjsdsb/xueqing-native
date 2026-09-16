# Local-first Command Sync Protocol

## Goal

Give teachers immediate resilient UX without turning Xueqing into a generic multi-master database. PostgreSQL is authoritative for formal business state.

## Push / Durable Outbox

A queued command envelope contains at least:

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

`created_at_local` is never a conflict resolver.

Retry transient failures with the same `operation_id`. On unknown result, resolve the existing operation receipt/result before creating any new user intent. Domain/authorization conflicts require explicit handling rather than blind retry.

## Pull / V1 Projection Snapshots

V1 fetches bounded, versioned, authorization-scoped read projections. It does not require a generic global change cursor.

Examples: PersonalBootstrap, StudentDetail, paged CaseTimeline, OrganizationSupervision and ManagementSnapshot.

Each refresh revalidates current authorization/scope. Scope reduction, account/environment change or incompatible projection generation may invalidate/purge disposable Projection Cache.

A generic change-list/cursor protocol is a later optimization only if measured production-like Snapshot cost justifies its additional ordering/deletion/migration complexity.

## Local state split

Projection Cache is server-derived and disposable. Durable Intent holds unsafely-uncommitted user work and cannot be destructively rebuilt.

## Conflict model

Lifecycle/governance conflicts use server versions/current snapshots. No generic Last Write Wins. A stale lifecycle command returns an explicit conflict/current projection for a new user decision.

## Realtime

Realtime may later improve freshness but is never required for correctness, authorization revocation or exactly-once semantics.

## Offline authorization

Sensitive cached data is available only under the finite Offline Access Lease policy. Lease design must account for wall-clock rollback and restored old backups; a simple device-wall-clock comparison is insufficient.
