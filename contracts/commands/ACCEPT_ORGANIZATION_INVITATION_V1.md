# AcceptOrganizationInvitation v1

## Purpose

`AcceptOrganizationInvitation v1` converts one still-valid Organization invitation into authoritative application identity and Membership state.

Acceptance is an application-domain transaction. The Auth provider proves the current external identity and the invitation-matching contact address; it does not own AppUser IDs, Membership rows, or invitation status.

## Reference-provider inputs

RPC:

```text
accept_organization_invitation_v1(
  p_operation_id uuid,
  p_invitation_id uuid,
  p_display_name text
)
```

The client never supplies:

- AppUser ID;
- external provider subject;
- email;
- organization ID;
- target role;
- teaching capability.

Those values are re-derived from the authenticated provider adapter and the locked invitation row.

## Provider boundary

The reference-provider adapter exposes two internal facts:

- current external identity tuple: provider key + issuer + opaque subject;
- current authenticated invitation email.

For Supabase, the second adapter reads the authenticated JWT email claim only for a non-anonymous authenticated session. Product/domain code never reads Supabase-specific JWT fields directly.

The adapter is replaceable. A different Auth provider may prove the invitation email differently.

## AppUser onboarding

If the current external identity already has an active IdentityLink:

- reuse the existing application-owned AppUser;
- require the AppUser to remain enabled;
- never rewrite its durable identity.

If no IdentityLink exists:

- create a new application-owned AppUser with a generated UUID;
- persist `auth_subject = null` in the legacy compatibility column;
- create one active IdentityLink from the external identity tuple to that AppUser;
- use the supplied display name only as initial AppUser presentation data.

An inactive pre-existing IdentityLink fails closed. Acceptance never silently creates a second AppUser for the same external identity.

## Acceptance invariants

At commit time:

- invitation exists;
- invitation status is `pending`;
- invitation has not expired;
- normalized provider email exactly matches invitation email;
- target AppUser does not already have a Membership in the invitation organization.

Acceptance then atomically:

1. creates/reuses AppUser + IdentityLink;
2. inserts active Membership using invitation role + `target_can_teach`;
3. marks invitation `accepted` with actor and server timestamp;
4. writes one operation receipt.

It never creates `StudentTeacherAssignment`.

## Idempotency and concurrency

Lock order:

```text
external identity advisory lock
→ operation advisory lock
→ existing IdentityLink/AppUser authority rows
→ invitation row
→ organization membership key
→ writes + receipt
```

The exact same `operation_id + invitation_id + display_name` replay returns the original receipt.

Different payload under the same operation fails:

```text
XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD
```

Competing acceptance attempts serialize on the invitation and identity locks.

## Receipt

```text
command = accept_organization_invitation_v1
operation_id
invitation_id
actor_app_user_id
organization_id
membership_role
can_teach
status = accepted
server_committed_at
```

## Frozen failures

```text
XQ_AUTH_REQUIRED
XQ_INVITATION_EMAIL_REQUIRED
XQ_INVITATION_EMAIL_MISMATCH
XQ_INVITATION_NOT_FOUND
XQ_INVITATION_NOT_PENDING
XQ_INVITATION_EXPIRED
XQ_IDENTITY_LINK_INACTIVE
XQ_ACTOR_DISABLED
XQ_MEMBERSHIP_ALREADY_EXISTS
XQ_DISPLAY_NAME_INVALID
XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD
```

## Delivery boundary

Acceptance does not send email and does not call an Auth admin API.

Delivery remains a separate provider adapter/outbox gate. This lets existing confirmed users and brand-new Auth users converge on the same Xueqing invitation acceptance transaction.

## Non-goals

- resend/revoke invitation;
- role mutation;
- owner transfer;
- member disable/remove;
- teaching assignment mutation;
- provider-specific email template management.
