# Command Contracts

Formal request/result contracts live in this directory.

Current Phase 1 commands:

- `CREATE_OBSERVATION_V1.md` — queueable low-risk classroom Observation; server still re-runs live authority.
- `CREATE_LEARNING_CASE_V1.md` — authoritative online creation of Learning Case + exactly one pending primary Action + provenance event.
- `RESCHEDULE_PRIMARY_ACTION_V1.md` — online due-date mutation with expected Case/Action versions.
- `RECORD_VERIFICATION_AND_NEXT_ACTION_V1.md` — online immutable Verification + atomic current-Action completion + next pending primary Action.

All high-risk/formal commands preserve these semantics:

```text
operation_id
expected versions/current relationship snapshot
server-side authorization
deterministic lock/re-read
atomic mutation
operation-bound events/audit
idempotent committed result
```

Creation commands without an existing aggregate version still require the relevant current relationship snapshot (for example the exact active teaching assignment id) and re-validate it inside the transaction.

Mutable Case/Action commands must reject stale expected versions. Last-write-wins is not a valid domain conflict strategy.

Client retries must reuse the same `operation_id`. Unknown-result recovery must never create a replacement intent.
