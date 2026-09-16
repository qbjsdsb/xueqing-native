# Migration from Legacy Xueqing

Legacy repository: `qbjsdsb/xueqing`.

The legacy project remains intact and serves as a read-only evidence base. Xueqing Native migrates validated semantics, not Flutter source.

## Asset matrix

### Preserve / re-audit

- product positioning and teaching loop;
- canonical Student + historical Enrollment/Assignment approach;
- Learning Case lifecycle and Next Action invariant;
- actor/supervisor/responsibility separation;
- Teaching Fact Gate;
- application-owned identity + external identity-link direction;
- RLS isolation and old-token negative-test requirements;
- `operation_id`, expected-version and exactly-once command semantics;
- append-only historical meaning/provenance;
- organization timezone rules;
- disaster-recovery mindset and production gates;
- UX lessons from Personal vs Organization workspace and Quick Capture.

### Rewrite cleanly

- PostgreSQL migrations: write clean v2 migrations from final semantics, not the whole patch history;
- client repositories/adapters;
- authentication/session storage implementation;
- release packaging;
- local persistence and sync.

### Do not migrate

- Flutter widgets/layout/routing/state implementation;
- cross-platform responsive branching accumulated around a single UI codebase;
- legacy client-specific provider calls;
- obsolete migrations/temporary compatibility structures solely required by the old implementation.

## Data migration

No real-data migration is authorized in Phase 0. Later migration requires a mapping specification, dry run, reconciliation report, rollback plan and explicit production Go/No-Go.