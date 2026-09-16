# Windows Real-App LocalState + MSIX Integration

Status: implementation gate in progress.

This gate moves the packaging and encrypted-local-data evidence from an isolated probe into the real `Xueqing.Windows` application without adding product features.

## Scope

The real Windows application must prove that it can:

- build as an x64 WinUI 3 MSIX using the already-proven self-contained Windows App SDK path;
- reference the production-shaped `Xueqing.Windows.Infrastructure` project;
- obtain real package identity and `ApplicationData.Current.LocalFolder` at installed runtime;
- place Durable Intent under an explicit scope containing environment, app user, organization, and installation dimensions;
- avoid exposing raw user or organization identifiers in filesystem path segments;
- open the accepted SQLite3MC + DPAPI `SqliteDurableOutboxStore` from that scoped LocalState path;
- persist one stable fictional Outbox operation;
- survive an in-place MSIX `1.0.0.0` to `1.0.1.0` update with the same package family, installation id, encrypted database path, DPAPI-wrapped key, and `operation_id`;
- reject accidental duplicate durable intent by observing the existing idempotent Outbox semantics after upgrade;
- keep a known fictional payload marker absent from raw database/WAL bytes;
- remove package LocalState on uninstall under the currently tested direct-MSIX lifecycle.

## Integration probe boundary

The real application does **not** create fictional data during normal launches.

CI opts into a packaged-app integration probe through the `XUEQING_WINDOWS_INTEGRATION_PROBE=1` process environment variable. Only that path uses the fictional scope:

- environment: `integration`;
- app user: `user-fictional-001`;
- organization: `org-fictional-001`;
- installation: a generated package-local installation id that must survive an in-place update.

Raw identity values are hashed before becoming directory names. This validates the composition shape without pretending that authentication or production scope selection already exists.

## Not frozen by this gate

- production package identity, certificate, publisher, Store/direct-distribution choice, or release channel;
- production authentication or organization selection;
- Offline Access Lease duration or rollback resistance;
- Projection Cache lifetime and purge policy;
- attachment staging or backup/restore policy;
- Android local storage;
- business UI or real backend commands.

The package identity and reused probe artwork in this gate are development-only packaging inputs, not product branding decisions.

## Lock discipline

Adding Infrastructure to the real app changes its NuGet graph. The first PR run may use a narrowly scoped Windows-runner bootstrap workflow to regenerate only `apps/windows/src/Xueqing.Windows/packages.lock.json`. Before acceptance, the workflow must return to read-only permissions and `--locked-mode`, and the exact final head must pass the real install/upgrade gate.
