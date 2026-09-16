# Architecture Spike Plan

The spike is a risk-reduction product, not a prototype to grow into production accidentally. Use fictional deterministic data only.

## Fixture scale

Target at least:

- 10 organizations;
- 100 teachers;
- 1,000 students;
- 10,000 Learning Cases;
- 50,000 timeline facts/events.

## Windows gate

Validate:

- .NET 10 + WinUI 3 stable project/toolchain;
- startup and navigation stability;
- list/detail workspace with virtualization;
- 1,000-student search/filter;
- 10,000+ timeline dataset without eager XAML element explosion;
- centralized Compact/Regular/Wide layout source;
- repeated resize, 480px-height stress and selected breakpoint boundaries;
- 100/150/200% DPI, light/dark/high-contrast, keyboard-only journey;
- SQLite persistence, crash-safe Outbox and cursor recovery;
- local encryption options and secure key storage;
- MSIX install/upgrade/uninstall;
- redacted diagnostics export.

## Android gate

Validate:

- Compose/Room/WorkManager project/toolchain;
- Today/Student/Quick Capture interaction;
- Room-backed UI source of truth;
- offline queue → process death → restart → successful retry;
- photo/draft lifecycle using fictional images;
- account switch cleanup and offline-lease lockout behavior;
- text scaling/TalkBack/dark mode;
- Macrobenchmark/Baseline Profile for startup and core journeys.

## Cross-client sync gate

Simulate Windows and Android editing the same Case:

- append-only compatible facts;
- stale lifecycle command using old expected version;
- timeout after server commit;
- membership/assignment scope reduction while another device is offline;
- old token after disable/reset;
- duplicate retry using same operation_id.

Expected result: no duplicate formal side effects, no Last Write Wins corruption, explicit conflicts, and cache purge/lockout after scope validation.

## Backend gate

Prove with PostgreSQL tests:

- organization isolation;
- teacher scope without assignment denied;
- manager supervision does not fabricate teaching responsibility;
- old-token/live-session security in candidate provider implementation;
- transactional command rollback/idempotency;
- Storage/private-object access and restore plan.

Only after these gates should production vertical-slice implementation begin.