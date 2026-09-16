# Legacy Migration Contract

## Principle

Migrate Xueqing domain meaning, not the legacy Flutter app or its historical migration chain.

## Canonical migration format

The migration tool should export versioned, deterministic fictional/production-authorized domain records such as organization, identity mapping, memberships, students, enrollments, profiles, assignments, Cases, Actions, append-only facts/events and an attachment manifest.

The physical format may be JSONL/manifest-based; it must include schema/export version, source identifier, counts and hashes sufficient for reconciliation.

## Process

1. export legacy source into canonical form;
2. validate format and referential/domain invariants;
3. dry-run import into disposable v2 environment;
4. reconcile counts, relationships, versions/history and attachment checksums;
5. run authorization/domain tests on imported data;
6. repeat until deterministic;
7. at cutover, freeze legacy writes/read-only as required and perform final export/import/verification.

## Forbidden

Do not replay/copy the old migration chain into the new database merely because it exists. Do not allow both legacy and v2 to keep accepting authoritative writes after cutover without an explicit dual-write protocol (none is planned for V1).
