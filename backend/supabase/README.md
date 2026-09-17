# Backend / Supabase

Development/reference backend for Xueqing Native. PostgreSQL semantics are authoritative; Supabase is the current development provider, not permission to leak provider SDK types into client Domain/ViewModel code.

## Current Phase 1 slice

Only the minimum schema needed for `CreateObservation v1` exists:

- AppUser;
- Organization;
- Membership;
- Student;
- StudentSubjectProfile;
- StudentTeacherAssignment;
- Observation;
- operation receipt / idempotency.

Learning Case, Evidence, Assessment, Action, attachments, realtime and generic sync cursors are intentionally absent.

The Git migration is schema truth. `seed.sql` contains deterministic fictional fixtures only. Database tests live under `tests/` and run through `supabase test db` / pgTAP.

## Security boundary

All application tables are in the exposed `public` schema, have RLS enabled, and grant **no direct table privileges** to `anon` or `authenticated`. There are no permissive RLS policies in this first write slice, so accidental future grants still encounter deny-by-default RLS until an explicit read projection is designed.

`public.create_observation` is the one intentional `SECURITY DEFINER` command boundary. This exception is justified because the command must atomically read live Membership/Profile/Assignment rows that clients are not allowed to CRUD directly and then append Observation + receipt. It therefore:

- sets `search_path=''`;
- schema-qualifies all referenced relations/functions;
- derives actor from `auth.uid()` rather than trusting a client actor id;
- re-evaluates the Teaching Fact Gate inside the transaction;
- serializes by stable `operation_id`;
- grants execute only to `authenticated`;
- returns an authoritative committed receipt.

Do not widen table grants merely to avoid this command boundary.

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
