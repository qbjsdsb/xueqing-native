# Command Contracts

Formal command request/result schemas will live here.

All high-risk commands preserve these semantics:

```text
operation_id
expected versions/current relationship snapshot
server-side authorization
atomic mutation
operation-bound events/audit
idempotent committed result
```

Client retries must reuse `operation_id`.