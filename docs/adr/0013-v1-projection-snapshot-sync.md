# ADR-0013 — V1 Scoped Projection Snapshot + Durable Outbox

**Status: Accepted**

**Supersedes:** the V1 pull mechanism in ADR-0004.

## Context

A generic incremental change-list protocol adds cursor ordering, deletion/tombstone, authorization-scope reconciliation and client migration complexity before Xueqing has evidence that bounded read projections are too expensive. Xueqing already has explicit domain commands and server-authoritative responsibility rules.

## Decision

V1 reads synchronize versioned, scoped Projection Snapshots such as PersonalBootstrap, StudentDetail, paged CaseTimeline and OrganizationSupervision. V1 approved offline writes use a Durable Outbox with stable `operation_id` and server-authoritative command processing.

Projection Cache is disposable/rebuildable. Durable Intent (drafts/outbox/pending attachment staging/operation recovery metadata) is not.

A generic change-list/cursor protocol is deferred until measured production-like scale proves Snapshot cost unacceptable.

## Consequences

- fewer sync invariants before first useful vertical slice;
- authorization scope can be re-evaluated at projection fetch boundaries;
- large history uses paging rather than whole-database snapshots;
- clients still need projection generation/freshness and scope reconciliation;
- later incremental sync requires a new ADR and performance evidence.
