# Local Data Security

Local-first functionality introduces a local copy of sensitive educational data. This is a first-class security boundary.

## Phase 0 hard questions

Before production data is permitted, freeze:

- which fields are allowed to be cached offline;
- how long cached access may remain usable without reauthorization;
- whole-database encryption vs selected-field encryption;
- OS-backed key storage on Windows and Android;
- account switch / logout / membership disable / organization archive cleanup;
- attachment cache policy and TTL;
- diagnostics redaction;
- backup/export behavior.

## Offline Access Lease

Offline access must be finite. The lease binds cached access to a recent successful server authorization check and at minimum a user, organization, device/app installation and expiration.

When expired, the client may preserve encrypted unsynced user input according to policy but must not continue exposing the full cached student workspace indefinitely.

The exact duration is a Spike decision, not a product guess.

## Encryption Spike

Android should evaluate Room-compatible SQLCipher/current maintained options and Android Keystore-backed key material.

Windows must compare maintained SQLCipher/SQLite3MC-style options against a minimal SQLite + DPAPI/Windows cryptography design. The decision must account for WinUI/MSIX packaging, migrations, performance, recovery and CI—not only whether encryption can be made to compile.

## Purge

A server-side permission reduction cannot remotely erase an offline device instantly. Therefore the product must document this limitation and minimize exposure through finite leases, encryption, minimal cache scope and mandatory purge/lockout after the next validation.