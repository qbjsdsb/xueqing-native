# ADR-0005 — No general CRDT or Last Write Wins

**Status: Accepted**

## Decision

Xueqing v2 does not build a generic multi-master CRDT, arbitrary object merge engine or client-time Last Write Wins system.

## Why

Teaching lifecycle/responsibility changes have domain invariants and authorization that require server context. `expected_version`, server re-read/locks and explicit conflict UX are safer and simpler.

Append-only compatible facts may sync independently where explicitly allowed; mutable lifecycle conflicts remain explicit.