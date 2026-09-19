# Command Contracts

Formal request/result contracts live in this directory.

Current Phase 1 commands:

- `CREATE_OBSERVATION_V1.md` — queueable low-risk classroom Observation; server still re-runs live authority.
- `CREATE_LEARNING_CASE_V1.md` — authoritative online creation of Learning Case + exactly one pending primary Action + provenance event.

All high-risk/formal commands preserve these semantics:

```text
operation_id
expected versions/current relationship snapshot
server-side authorization
atomic mutation
operation-bound events/audit
idempotent committed result
```

For creation commands without an existing aggregate version, the relevant current relationship snapshot (for example the exact active teaching assignment id) is still required and re-validated inside the transaction.

Client retries must reuse the same `operation_id`. Unknown-result recovery must never create a replacement intent.
