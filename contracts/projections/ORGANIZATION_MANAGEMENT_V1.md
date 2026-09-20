# OrganizationManagement v1

## Purpose

`OrganizationManagement v1` is the first server-authoritative read projection for the Organization workspace.

It answers only:

- whether the current application-owned AppUser may enter management for one organization;
- the actor's current organization membership role;
- the current member roster and governance-relevant member state;
- narrow invite capabilities used to shape client actions.

It is a snapshot for presentation and command composition. It is **not** authorization proof for a later invite, role, disable, transfer, or teaching command.

## Server entry point

Reference-provider RPC:

```text
get_organization_management_v1(p_organization_id uuid)
```

The actor is derived from the authenticated provider identity through `IdentityLink`. The client never supplies an actor AppUser id or role.

## Management authority

Only an **active** organization membership whose `membership_role` is `owner` or `admin` may read this projection.

A teacher-only, disabled, missing, or cross-organization membership receives:

```text
XQ_ORGANIZATION_MANAGEMENT_REQUIRED
```

This deliberately avoids exposing whether a different organization exists or which role it contains.

## Envelope

```text
contract = organization_management_v1
generated_at_server

actor
  app_user_id
  display_name
  membership_role

organization
  organization_id
  name
  time_zone

capabilities
  can_invite_owner
  can_invite_admin
  can_invite_teacher

members[]
  app_user_id
  display_name
  app_user_enabled
  membership_role
  membership_status
  can_teach
```

The first capability policy is intentionally narrow:

- owner: may invite admin and teacher;
- admin: may invite owner and teacher;
- neither role receives a client-side implication that it may mutate roles or disable members.

Invite/role mutation commands are a later gate and must re-read live authority, enforce invariants, and write an operation receipt.

## Separation from teaching responsibility

Organization management visibility never:

- creates or changes a `StudentTeacherAssignment`;
- makes an owner/admin the actor for a teaching fact;
- changes Case responsibility or Action assignment;
- grants `can_teach`.

Teaching commands continue to enforce the independent Teaching Fact Gate.

## Client rules

- Unknown contract versions or malformed cross-scope payloads fail closed.
- Clients must not synthesize management access from PersonalBootstrap membership presence.
- The Organization workspace entry is visible only after this projection (or a future equivalent management-capability bootstrap) proves access.
- Disabled members remain visible to managers so governance history is not hidden.

## Non-goals

V1 does not implement:

- invitation creation/acceptance;
- role mutation;
- owner transfer;
- member disable/remove;
- teaching assignment changes;
- organization-wide Student/Learning projections;
- audit-event history.
