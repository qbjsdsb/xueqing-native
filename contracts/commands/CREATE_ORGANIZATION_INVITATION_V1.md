# CreateOrganizationInvitation v1

## Purpose

`CreateOrganizationInvitation v1` creates the durable business invitation that precedes any provider-specific email delivery or account onboarding.

The invitation is an Organization-domain object. Supabase Auth, an email provider, or any future identity provider is an adapter and must not become the source of truth for membership intent.

## Command

Reference-provider RPC:

```text
create_organization_invitation_v1(
  p_operation_id uuid,
  p_organization_id uuid,
  p_invited_email text,
  p_target_role text,
  p_target_can_teach boolean
)
```

The actor is resolved from the authenticated provider identity through `IdentityLink`. Clients never supply an actor AppUser id or actor role.

## Frozen authority policy

The actor must have an **active** management membership at commit time.

- owner may invite: admin, teacher
- admin may invite: owner, teacher
- teacher may not create invitations
- disabled memberships may not create invitations

`target_can_teach` is independent of management role. It does not create a teaching assignment and cannot by itself satisfy the Teaching Fact Gate.

## Idempotency and concurrency

The command follows the formal mutation pattern:

```text
operation_id
+ current external identity → AppUser
+ operation advisory lock
+ exact payload replay check
+ live identity/AppUser + membership locks
+ normalized invite-address lock
+ invariant validation
+ invitation insert
+ operation receipt
+ atomic commit
```

Retrying the same `operation_id` with the exact same payload returns the original receipt.

Reusing an `operation_id` with a different payload fails with:

```text
XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD
```

Only one non-expired pending invitation may exist for the same organization + normalized email. A different operation attempting to create another pending invitation fails with:

```text
XQ_INVITATION_ALREADY_PENDING
```

Expired pending rows are retired before a new invitation for the same address is inserted.

## Email normalization

The server trims and lowercases the invited email before persistence and receipt comparison.

V1 performs deliberately narrow syntax validation. Provider-specific deliverability or mailbox verification is **not** a database-domain concern.

## Receipt

```text
command = create_organization_invitation_v1
operation_id
invitation_id
actor_app_user_id
organization_id
invited_email
target_role
target_can_teach
status = pending
expires_at
server_committed_at
```

The initial server expiry is seven days.

## Acceptance and delivery boundary

This command does **not** send email and does **not** create a membership.

A later acceptance gate must prove:

- the authenticated external identity is valid;
- the invitee contact matches the invitation through a provider adapter;
- application-owned AppUser / IdentityLink onboarding is explicit and provider-decoupled;
- the invitation is still pending and unexpired;
- membership creation is atomic with invitation acceptance;
- duplicate/replayed acceptance cannot create duplicate membership;
- accepting management visibility never creates `StudentTeacherAssignment`.

Until delivery + acceptance are implemented and proven, Windows keeps the Invite action disabled.

## Non-goals

- provider admin API calls;
- sending email from a database transaction;
- role mutation of existing members;
- owner transfer;
- member disable/remove;
- teaching assignment mutation.
