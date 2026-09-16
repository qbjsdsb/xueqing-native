# ADR-0004 — Local-first Command Sync

**Status: Accepted**

## Decision

Both native clients may use local databases for responsive reads, protected drafts, queued commands and cached projections. Formal business state remains server-authoritative.

Queued writes use Outbox envelopes with stable operation identity. Pull uses a server-issued cursor/equivalent and current authorization-scope validation.

## Consequences

- offline UX does not imply offline authority;
- local persistence is not a second canonical business database;
- retry/timeout/conflict handling is part of the product contract, not UI error glue.