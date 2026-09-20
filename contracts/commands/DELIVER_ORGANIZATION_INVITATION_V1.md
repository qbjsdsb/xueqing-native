# DeliverOrganizationInvitation v1

## Purpose

`DeliverOrganizationInvitation v1` sends the already-authoritative Organization invitation through the configured reference-provider email channel without turning email delivery into Organization-domain truth.

The durable invitation created by `CreateOrganizationInvitation v1` remains authoritative. Delivery is a provider side effect with its own explicit, recoverable state.

## Reference-provider flow

The trusted delivery adapter accepts only:

```text
operation_id
invitation_id
```

The actor is derived from the caller's authenticated external identity. The server re-reads the invitation and live Organization management Membership. The client never supplies recipient email, Organization id, role, teaching capability, provider credentials, or redirect authority.

The server first calls:

```text
begin_organization_invitation_delivery_v1(
  p_operation_id uuid,
  p_invitation_id uuid
)
```

A successful first claim returns a server-derived recipient and `should_dispatch = true`. The provider adapter then sends the email. Only a trusted server-side adapter may call the service-only completion functions.

## Durable delivery state

The delivery record is separate from the invitation:

```text
dispatching -> sent
           \-> failed
```

`dispatching` means the provider side effect may be in flight or its result may be unknown. It must never be treated as safe-to-send-again automatically.

`sent` means the provider accepted the send request and the trusted adapter subsequently committed that fact.

`failed` is reserved for a provider rejection that the adapter knows did not accept the send request.

V1 allows one delivery attempt record per invitation. Resend/reissue is a separate command and is deliberately not smuggled into automatic retry behavior.

## Authorization

At claim time all of the following are re-evaluated under locks:

- current external identity resolves through an active IdentityLink;
- AppUser remains enabled;
- invitation exists, is still `pending`, and is unexpired;
- actor has an active Organization Membership;
- Membership role is `owner` or `admin`.

Management authority never creates or changes `StudentTeacherAssignment`.

## Idempotency and result-unknown behavior

`operation_id` is globally reserved through the existing operation-receipt boundary when the dispatch claim commits.

Exact same-operation replay returns the current delivery state without sending again.

Different-payload operation reuse fails with:

```text
XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD
```

A second operation for an invitation that already has a delivery attempt fails closed. The caller must not invent a new operation merely because a network response was lost.

If the provider request may have been accepted but the adapter cannot prove the result, the delivery remains `dispatching`. Retrying the same operation resolves the durable state and **does not dispatch another email**.

## Provider adapter

The Supabase reference adapter runs server-side only.

For v1 it uses Supabase Auth email OTP / magic-link delivery so both an existing Auth identity and a brand-new Auth identity can converge on the same application-owned invitation acceptance command.

The email redirect carries only the opaque invitation id. Role, Organization, teaching capability and Membership are never trusted from a link or client parameter; Acceptance re-reads those values from the locked invitation.

Provider publishable/secret credentials are server configuration and must never be committed or returned to Windows/Android.

## Completion boundary

Trusted server-only RPCs:

```text
complete_organization_invitation_delivery_v1(
  p_operation_id uuid,
  p_delivery_id uuid
)

fail_organization_invitation_delivery_v1(
  p_operation_id uuid,
  p_delivery_id uuid,
  p_failure_code text
)
```

They cannot create Membership, accept/revoke the invitation, or mutate teaching scope.

## V1 non-goals

- resend/reissue;
- invitation revoke;
- owner transfer or role mutation;
- client-side SMTP/provider credentials;
- sending email inside a PostgreSQL transaction;
- treating successful email delivery as successful invitation acceptance;
- provider-specific email template management.
