# Phase 0 Execution Plan

Phase 0 freezes the minimum product/architecture contract before formal native implementation. It is intentionally documentation-, test-design- and Spike-heavy.

## Exit deliverables

Phase 0 is complete only when these are accepted:

1. product scope/non-goals;
2. v2 domain model and state machines;
3. command/invariant contract;
4. authorization/responsibility model;
5. Local-first sync protocol and error taxonomy;
6. local-data/offline-access security decision plan;
7. Windows and Android UX principles;
8. provider-neutral backend boundary;
9. legacy migration asset matrix;
10. release/signing/update direction;
11. open-source reference/license policy;
12. Architecture Spike acceptance matrix.

## Work order

### 0A — Legacy extraction

Review legacy README, architecture, data model, commands/invariants, auth/permissions, ADRs, security tests and recent product/UX fixes. Mark each rule `carry`, `re-audit`, `supersede` or `do-not-migrate`.

### 0B — Domain/contracts

Freeze names, state machines, responsibility semantics, command envelopes, expected-version rules and stable error families before client implementations diverge.

### 0C — Local-first/security

Define which operations are offline queueable, pull cursor/scope-version semantics, finite offline lease, local encryption candidates, purge/account-switch behavior and diagnostics redaction.

### 0D — Platform Spike specs

Build no production feature. Use deterministic fictional fixtures to validate WinUI 3 and Android architectural risks.

### 0E — Backend Spike

Prove PostgreSQL RLS/transaction/idempotency concepts with fictional data. Production provider/region remains gated.

## Hard gates

Phase 0 does **not** authorize:

- real student/teacher/guardian data;
- production provider lock-in;
- production credential/signing secrets in Git;
- broad feature implementation;
- copying legacy Flutter source;
- implementing a generic CRDT/realtime dependency.

## Decision discipline

Accepted decisions live in ADRs. An implementation that conflicts with an accepted ADR must stop and propose a superseding ADR rather than silently diverge.