# Local State Model

## Two lifetimes

### Projection Cache

Server-derived read models. Disposable, scope-bound and rebuildable. A cache generation may be invalidated wholesale after contract/schema/authorization changes.

### Durable Intent Store

User work not yet safely committed remotely:

- drafts;
- Outbox command envelopes;
- pending attachment staging metadata;
- stable `operation_id` and unknown-result state;
- minimum recovery metadata.

Durable Intent must not use destructive migration fallbacks.

## Scope

Local state is bound at minimum to environment + app user + organization + installation/device scope. Personal and Organization draft scopes must not restore into each other accidentally.

## Local save invariant

When the product says an offline-capable intent is saved locally, a single local transaction must have durably recorded the intent, stable operation identity and required queue/recovery state. UI success must not race ahead of durable persistence.

## Write coordination

Network waits never occur while holding a local DB transaction. Autosave, projection replacement, Outbox acknowledgement and attachment-state writes use short transactions coordinated to avoid uncontrolled SQLite writer contention.

## Cache replacement

Authorization reduction, account switch, environment switch or projection-generation incompatibility may invalidate/rebuild Projection Cache while preserving encrypted authorized Durable Intent according to policy.
