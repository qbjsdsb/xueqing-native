# Release Compatibility

## Compatibility window

Backend contracts and Durable Intent schemas must support a practical N-1 client window during staged upgrades unless a documented emergency security block requires otherwise.

Prefer additive API evolution. Incompatible Projection/Command changes receive a new contract version. Unknown enum values must have an explicit compatibility strategy rather than crashing old clients.

## Emergency client policy

Before production, define a minimal server-driven policy capable of communicating recommended version, minimum supported version and security-blocked versions. It is not a general feature-flag system.

## Rollback

A package downgrade does not guarantee local-data downgrade. Release gates must test Durable Intent schema compatibility and downgrade/recovery paths. Disposable Projection Cache may be rebuilt.

## Signing and provenance

Production signing keys require controlled storage plus a documented recovery/backup path. Build artifacts should be traceable to repository/workflow/commit; GitHub artifact attestation may be added at Release stage.
