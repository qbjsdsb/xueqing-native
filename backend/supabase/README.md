# Backend / Supabase

Development/reference backend for Xueqing Native. PostgreSQL semantics are authoritative; Supabase is the current development provider, not permission to leak provider SDK types into client Domain/ViewModel code.

## Current Phase 1 slice

The backend now carries the first two real teaching verticals.

### Observation

- AppUser / IdentityLink;
- Organization / Membership;
- Student / StudentSubjectProfile / StudentTeacherAssignment;
- append-only Observation;
- idempotent operation receipt;
- PersonalBootstrap and recent-Observation projections.

### Learning Case + Primary Action

The active slice adds only the minimum formal Case model required by the accepted domain invariant:

- LearningCase;
- Primary Action;
- append-only Case event;
- optional same-scope Observation link;
- atomic `CreateLearningCase v1`.

A successful new Case begins in `new` state, is bound to the current legal responsible-teacher assignment, and atomically receives exactly one pending primary Action.

Evidence/Intervention/Assessment, Case transitions/close/reopen, Today projection, attachments, realtime and generic sync cursors remain outside this first Case command.

The Git migrations are schema truth. `seed.sql` contains deterministic fictional fixtures only. Database tests live under `tests/` and run through `supabase test db` / pgTAP.

## Security boundary

All application tables are in the exposed `public` schema, have RLS enabled, and grant **no direct table privileges** to `anon` or `authenticated`.

Authoritative command RPCs are narrow `SECURITY DEFINER` boundaries. They:

- set `search_path=''`;
- schema-qualify referenced relations/functions;
- resolve the application-owned actor through the active `IdentityLink`;
- lock/re-read live Membership/Profile/Assignment authority;
- serialize stable user intents by `operation_id`;
- commit domain side effects and operation receipt atomically;
- grant execute only to `authenticated`.

`CreateObservation v1` appends a low-risk teaching fact after the live Teaching Fact Gate.

`CreateLearningCase v1` is an online-only formal command. It derives responsibility from the caller's live assignment and atomically creates Case + pending primary Action + provenance event + optional Observation link + receipt. It never trusts a client-supplied actor/responsible-teacher id.

Do not widen table grants merely to avoid command boundaries.

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
