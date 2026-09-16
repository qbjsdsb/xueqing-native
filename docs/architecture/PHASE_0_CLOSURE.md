# Phase 0 Closure / Architecture Baseline v1

**Status: Accepted**

Phase 0 is closed. Architecture Baseline v1 is the accepted pre-implementation baseline. Broad architecture research stops here; remaining uncertainty must be resolved by executable Spikes or explicit production gates.

This matrix is the research stop rule. Every material pre-implementation question is classified as FROZEN, SPIKE_REQUIRED, LATER or REJECTED.

## FROZEN

- Windows = C#/.NET 10 + WinUI 3 stable.
- Android = Kotlin + Compose/Material 3.
- PostgreSQL-first; Supabase is development/reference provider.
- Actor / Supervisor / Responsible Teacher remain distinct.
- Teaching Fact Gate is server-authoritative.
- high-risk commands use stable `operation_id` + expected version/current relation + atomic server validation/commit.
- V1 uses versioned Projection Snapshots for reads and Durable Outbox for approved queued writes.
- Projection Cache is disposable; Durable Intent is not.
- no client-timestamp/LWW conflict resolution.
- provider SDKs are Adapter-only.
- business time is server-projected; device runtime time is separate.
- Android = Capture; Windows = Organize + Think.
- Cloud-first CI and GitHub exact-SHA evidence are engineering requirements.
- GitHub is engineering truth for AI handoff; memory is auxiliary.
- public repository contains fictional/anonymized data only.

## SPIKE_REQUIRED

- WinUI startup/list-detail/virtualization/resize/DPI/keyboard/IME/MSIX/GUI-test feasibility.
- Android Room/WorkManager/process-death/IME/emulator/performance.
- physical PostgreSQL `core/private/api` schema and Projection implementation.
- provider Adapter conformance and old-token behavior.
- Windows/Android local encryption/key storage.
- Offline Access Lease duration and clock-rollback/backup-restore behavior.
- production provider/region/data residency/network.
- signing/recovery/distribution.
- backup + Storage restore rehearsal.

## LATER

- generic change-list/cursor incremental sync, only after measured Snapshot cost demands it;
- large-history Chinese full-text search;
- Realtime freshness;
- AI product features;
- Native AOT;
- advanced screen-capture policy.

## REJECTED FOR V1

- generic CRDT or arbitrary multi-master merge;
- Last Write Wins for domain conflicts;
- generic Event Sourcing platform;
- heavyweight OpenAPI-generated client architecture;
- custom updater before platform update channels are proven insufficient;
- large feature-flag platform;
- deep stacked PR chains as normal workflow.

## Research stop rule

Broad architecture research remains stopped while this matrix has no `UNKNOWN` category. Future direction changes require new evidence and a superseding ADR, not conversational drift.

## Phase 0 exit evidence

Phase 0 exit conditions are satisfied:

- Architecture Baseline v1 consistency/foundation CI passed on the exact merged `main` SHA;
- the repository has an active `Protect main` ruleset targeting the default branch;
- pull requests are required for `main` changes;
- deletion and non-fast-forward/force-push are restricted;
- `repository-foundation` is a required status check with strict up-to-date enforcement;
- no bypass actor is configured.

The project now proceeds through executable Architecture Spikes. Spike evidence may supersede provisional implementation details through ADRs without reopening broad framework research.
