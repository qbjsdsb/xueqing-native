# Personal Today Actions v1

Status: **Phase 1 projection candidate — executable backend evidence required**

## Purpose

Return the current teacher's explicit pending primary Actions across their active personal teaching assignments.

Today is an Action queue. It is not a list of every open Case.

## Entry point

Reference-provider RPC:

```text
get_personal_today_actions_v1()
```

## Authority boundary

The server resolves the application-owned actor through the active `IdentityLink`.

An item is visible only when all of these remain live:

- enabled AppUser;
- active teaching-capable Membership in the item's Organization;
- active Student;
- active Student Subject Profile;
- active Student Teacher Assignment owned by the actor;
- open Learning Case whose responsible teacher is the actor;
- pending primary Action assigned to the actor.

Organization owner/admin visibility alone never inserts another teacher's Action into Personal Today.

## Organization business date

Every Organization has an explicit validated timezone identifier.

The server computes, per item:

```text
organization_business_date =
  instant interpreted in organization.time_zone
```

Clients must not use the device timezone to decide whether an Action is overdue/today/future.

Due buckets:

- `overdue`: due_on < organization business date;
- `today`: due_on = organization business date;
- `undated`: due_on is null;
- `future`: due_on > organization business date.

Different Organizations may therefore have different business dates at the same UTC instant.

## Result shape

The projection returns:

- contract = `personal_today_actions_v1`;
- generated_at_server;
- actor_app_user_id;
- `actions`: up to 200 pending primary Actions;
- `has_more`: whether more than 200 matching Actions exist.

Each item contains:

- organization_id;
- organization_name;
- organization_time_zone;
- organization_business_date;
- student_id;
- student_display_name;
- subject_profile_id;
- subject_key;
- assignment_id;
- case_id;
- case_title;
- case_state;
- case_version;
- action_id;
- action_text;
- due_on;
- due_bucket;
- action_version;
- case_updated_at_server.

## Ordering

Stable queue order:

1. overdue;
2. due today;
3. undated / needs scheduling;
4. future.

Within dated buckets, earlier due dates come first. Remaining ties are deterministic.

## Primary Action invariant

Every visible open Case must have exactly one pending primary Action.

If privileged/manual corruption creates zero or multiple pending primary Actions, the projection fails closed with `XQ_CASE_PRIMARY_ACTION_INVARIANT`. It must never silently fabricate, duplicate or hide the broken Case.

## Mutation boundary

This projection is read-only.

V1 deliberately does **not** expose a naive “complete Action” write because completing the only pending primary Action while leaving the Case open would violate the formal Case invariant.

Action reschedule/completion/verification/next-action semantics require their own atomic domain commands.
