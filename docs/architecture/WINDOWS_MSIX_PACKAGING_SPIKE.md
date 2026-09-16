# Windows MSIX Packaging Spike

Status: Phase 1 gate. This document records executable acceptance criteria; it does not freeze the production distribution channel.

## Why this spike exists

The Windows architecture spike proved WinUI 3 compilation and the local-data security spike proved SQLite3MC/DPAPI/AES-GCM behavior on Windows Server 2025. Neither proves that a real installed MSIX contains and can load the required native SQLite3MC runtime, nor that package upgrades preserve encrypted local state.

The production application must not switch its persistence provider or distribution model until those packaging boundaries are exercised.

## Current Microsoft guidance

As checked in September 2026:

- new WinUI 3 applications are packaged by default and MSIX is the recommended packaged model for new Windows applications;
- single-project MSIX is supported for C# WinUI applications and can be automated with `GenerateAppxPackageOnBuild=true`;
- development/test MSIX packages may use a self-signed certificate that is explicitly trusted on the test machine;
- updating an installed package requires the same package family identity and a higher applicable package version;
- uninstall normally removes package LocalState unless a separate persistence mechanism/persistent identity policy is deliberately used.

These statements are inputs to the spike, not substitutes for executable evidence.

## Isolation rule

This spike creates `Xueqing.Windows.PackagingProbe` instead of converting the production `Xueqing.Windows` project.

The probe:

- is a minimal WinUI 3 single-project MSIX;
- is x64-only for the first gate;
- uses self-contained Windows App SDK deployment for deterministic CI;
- references the already validated `Xueqing.Windows.LocalDataSecurity` spike;
- contains only fictional/probe data;
- is signed with an ephemeral self-signed CI certificate;
- never commits a private signing key.

The existing production-shaped WinUI bootstrap, durable Outbox and their lock files must remain unchanged and green.

## Installed-runtime proof

On first launch the packaged probe must:

1. obtain `ApplicationData.Current.LocalFolder`;
2. create a random 256-bit SQLite master key;
3. protect that key with DPAPI `CurrentUser` and a purpose string;
4. create/open an encrypted SQLite3MC database in LocalState;
5. enable WAL + `synchronous=FULL`;
6. create exactly one stable token in the encrypted database;
7. write a non-sensitive JSON report containing package identity/version, SQLite3MC runtime version and the stable token.

A missing package identity, missing native SQLite3MC library, broken DPAPI context or unusable encrypted database therefore prevents the report from being produced.

## Upgrade proof

CI builds two packages from the same source and package identity:

- v1: `1.0.0.0`
- v2: `1.0.1.0`

Acceptance requires:

- v1 installs and launches;
- the generated MSIX archive contains a SQLite3MC native DLL;
- the installed package directory contains a SQLite3MC native DLL;
- v1 reports SQLite3MC `2.4.0`;
- v2 installs as an update under the same package family;
- v2 launches using the DPAPI-wrapped key created by v1;
- the encrypted database survives the update;
- the stable token is byte-for-byte/string-for-string unchanged;
- the package version reported by the running installed app becomes `1.0.1.0`.

## Uninstall proof

After v2 validation, CI removes the package and verifies that the probe LocalState report is removed. This records the default packaged-app cleanup behavior and prevents accidental assumptions that uninstall is a backup strategy.

Production retention/export behavior remains a separate policy decision.

## Package-size evidence

CI records:

- v1 MSIX size;
- v2 MSIX size;
- installed package footprint.

The probe currently uses self-contained Windows App SDK deployment, so these numbers are an upper-bound-style reference for that deployment choice, not a final release target.

## Signing boundary

The CI certificate is generated at runtime and trusted only on the ephemeral test runner. The repository stores no PFX, private key or password.

Passing this spike does **not** authorize self-signed production distribution. Production signing/Store policy remains a later release gate.

## Exit criteria

The spike passes only when the same PR head proves:

1. Foundation workflow green;
2. existing Windows Core/Outbox tests green;
3. existing WinUI Release build green;
4. existing local-data security tests green;
5. packaging probe restore/build green;
6. MSIX is generated and signed;
7. SQLite3MC native library is present in the archive and installed location;
8. v1 installs and launches;
9. v1 -> v2 update preserves package family and encrypted LocalState token;
10. v2 launches and reports the expected package/runtime versions;
11. uninstall cleanup behavior is observed and documented;
12. no production data, production certificate or signing secret is introduced.

Only after this gate may the project consider migrating the real Windows Infrastructure/provider and application project to the packaged encrypted path.
