# Phase 0 Execution Plan

Phase 0 freezes the minimum product/architecture contract and converts remaining uncertainty into executable Spikes or production gates.

## Exit deliverables

Phase 0 closure requires:

1. product scope/non-goals;
2. domain/state-machine and command/invariant contracts;
3. authorization/responsibility model;
4. Projection + Durable Outbox V1 sync contract;
5. Projection Cache vs Durable Intent local-state contract;
6. local-data/offline-access security Spike plan;
7. Windows/Android UX and IME/accessibility gates;
8. provider-neutral backend/Adapter boundary;
9. Cloud-first development and exact-SHA CI evidence contract;
10. AI bootstrap/handoff contract;
11. threat model, data lifecycle and environment isolation;
12. legacy canonical migration contract;
13. release compatibility/signing/backup-restore strategy;
14. open-source/license policy;
15. Architecture Spike acceptance matrix;
16. Phase 0 Closure Matrix with every item FROZEN, SPIKE_REQUIRED, LATER or REJECTED;
17. protected `main`/ruleset with required foundation checks before implementation begins.

## Work order

### 0A — Legacy extraction
Classify mature legacy rules `carry`, `re-audit`, `supersede` or `do-not-migrate`.

### 0B — Domain/contracts
Freeze names, state machines, responsibility, commands, versions, projections and stable error families.

### 0C — Local-first/security
Freeze Snapshot/Outbox boundaries, Durable Intent, finite offline lease requirements, cache/purge/backup behavior and diagnostics redaction.

### 0D — Cloud/AI continuity
Ensure a fresh agent can recover from GitHub and every formal gate can run in cloud CI without relying on one local machine.

### 0E — Platform/backend Spike specs
Build no broad production feature. Use deterministic fictional fixtures to validate WinUI, Android, provider and PostgreSQL risks.

## Hard gates

Phase 0 does not authorize real student/teacher/guardian data, production provider lock-in, production secrets in Git, broad feature implementation, copying Flutter source, generic CRDT/realtime dependence or destructive migration of Durable Intent.

## Decision discipline

Accepted decisions live in ADRs. A conflicting implementation must stop and propose a superseding ADR. Broad research stops under the rule in `PHASE_0_CLOSURE.md`; remaining uncertainty is answered by Spikes/evidence.
