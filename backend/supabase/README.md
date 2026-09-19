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

### Organization business date + personal read models

The active read-model slice adds:

- an explicit validated Organization timezone;
- server-side Organization business-date calculation;
- `StudentLearningFocus v1` — at most three recent-active open Cases with their authoritative pending primary Action;
- `PersonalTodayActions v1` — explicit pending primary Actions across the actor's own live teaching assignments.

Today bucketing is server-authoritative per Organization:

```text
overdue → today → undated → future
```

Clients must not recompute overdue/today/future from the device timezone.

Organization management authority alone never causes another teacher's Case or Action to appear in personal projections.

Evidence/Intervention/Assessment, Case transitions/close/reopen, Action completion/reschedule, attachments, realtime and generic sync cursors remain outside these read models.

The Git migrations are schema truth. `seed.sql` contains deterministic fictional fixtures only. Database tests live under `tests/` and run through `supabase test db` / pgTAP.

## Security boundary

All application tables are in the exposed `public` schema, have RLS enabled, and grant **no direct table privileges** to `anon` or `authenticated`.

Authoritative command/projection RPCs are narrow `SECURITY DEFINER` boundaries. They:

- set `search_path=''`;
- schema-qualify referenced relations/functions;
- resolve the application-owned actor through the active `IdentityLink`;
- re-check the relevant live Membership/Profile/Assignment scope;
- expose only versioned domain contracts rather than direct table access.

Write commands additionally serialize stable user intents by `operation_id` and commit domain side effects plus receipts atomically.

`CreateObservation v1` appends a low-risk teaching fact after the live Teaching Fact Gate.

`CreateLearningCase v1` is an online-only formal command. It derives responsibility from the caller's live assignment and atomically creates Case + pending primary Action + provenance event + optional Observation link + receipt.

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
