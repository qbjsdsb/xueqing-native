# Backend / Supabase

Development/reference backend for Xueqing Native. PostgreSQL semantics are authoritative; Supabase is the current development provider, not permission to leak provider SDK types into client Domain/ViewModel code.

## Current Phase 1 slices

### Observation

- AppUser / IdentityLink;
- Organization / Membership;
- Student / StudentSubjectProfile / StudentTeacherAssignment;
- append-only Observation;
- idempotent operation receipt;
- PersonalBootstrap and recent-Observation projections.

### Learning Case + Primary Action

The accepted formal Case creation path contains:

- LearningCase;
- Primary Action;
- append-only Case event;
- optional same-scope Observation link;
- atomic `CreateLearningCase v1`.

A successful new Case begins in `new` state, is bound to the current legal responsible-teacher assignment, and atomically receives exactly one pending primary Action.

### Action progression + Verification

The active slice adds two online-only formal commands:

- `ReschedulePrimaryAction v1` — optimistic Case/Action versions, live authority re-check, Action due-date change, aggregate version increment, provenance event and receipt;
- `RecordVerificationAndNextAction v1` — immutable Verification fact + atomic completion of the current Action + creation of exactly one next pending primary Action.

Verification outcome is descriptive (`met / partially_met / not_met / uncertain`). It does **not** implicitly mark a Case stable or closed.

The open-Case invariant remains:

```text
legal responsible teacher + exactly one pending primary Action
```

No client repair step is permitted between completion and next-Action creation; both are one database transaction.

### Organization business date + personal read models

- explicit validated Organization timezone;
- server-side Organization business-date calculation;
- `StudentLearningFocus v1`;
- `PersonalTodayActions v1`.

Today bucketing is server-authoritative per Organization:

```text
overdue → today → undated → future
```

Clients must not recompute overdue/today/future from the device timezone.

Organization management authority alone never causes another teacher's Case or Action to appear in personal projections.

Case stable/close/reopen, responsibility reassignment, attachments, realtime and generic sync cursors remain separate later slices.

The Git migrations are schema truth. `seed.sql` contains deterministic fictional fixtures only. Database tests live under `tests/` and run through `supabase test db` / pgTAP.

## Security boundary

All application tables are in the exposed `public` schema, have RLS enabled, and grant **no direct table privileges** to `anon` or `authenticated`.

Authoritative command/projection RPCs are narrow `SECURITY DEFINER` boundaries. They:

- set `search_path=''`;
- schema-qualify referenced relations/functions;
- resolve the application-owned actor through the active `IdentityLink`;
- re-check relevant live Membership/Profile/Assignment scope;
- use explicit expected aggregate versions for mutable Case/Action commands;
- expose only versioned domain contracts rather than direct table access.

Write commands additionally serialize stable user intents by `operation_id` and commit domain side effects plus receipts atomically.

Read projections fail closed with `XQ_CASE_PRIMARY_ACTION_INVARIANT` if privileged/manual corruption leaves a visible open Case without exactly one pending primary Action.

Do not widen table grants merely to avoid command/projection boundaries.

## Local / CI

The committed project lives at `backend/supabase`; use the repository root as working directory and pass `--workdir backend` to the Supabase CLI.

```text
supabase start --workdir backend
supabase db reset --workdir backend
supabase db lint --workdir backend --schema public --fail-on error
supabase test db --workdir backend
supabase stop --workdir backend
```

CI pins a stable Supabase CLI and starts the local stack with no hosted-project credentials. Ordinary PRs therefore receive no production/staging secrets.

Production provider/region remains a later gate. Realtime is not required for correctness.
