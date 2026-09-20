# Provider Projection Conformance v1

## Purpose

Provider Projection Conformance freezes the provider-neutral semantics of Xueqing's accepted read-model boundary.

A provider authenticates an external identity. It does not own Xueqing business identity, teaching scope, Organization authority, projection ordering, Case/Action semantics, or response contracts.

The reference conformance gate therefore proves that two independent real external Auth identities mapped through active `IdentityLink` rows to the same application-owned `AppUser` observe the same authoritative business projections.

## Covered v1 projections

The v1 matrix covers the currently accepted front-door projections:

- `PersonalBootstrap v1`;
- `Student Recent Observations v1`;
- `Student Learning Focus v1`;
- `Student Learning Cases v1`;
- `Personal Today Actions v1`;
- `OrganizationManagement v1`.

This matrix spans personal membership/assignment discovery, bounded teaching history, current aggregate state, cross-Organization Today semantics, bounded Case history and Organization governance visibility.

## Conformance fixture

The executable reference-provider test:

1. creates two fictional real Auth users through the local provider Admin API;
2. obtains two provider-issued authenticated sessions;
3. reads each session's real issuer + opaque subject;
4. maps both external identities to one existing fictional application-owned AppUser;
5. changes the legacy `app_users.auth_subject` column to a decoy value;
6. creates one authoritative Observation and one Learning Case through normal public commands;
7. reads all covered projections independently through both provider sessions.

The test uses only fictional local/CI data.

## Equivalence rule

For the same live application-owned AppUser and unchanged authoritative business state, both external identities must return semantically identical business payloads.

The comparison ignores only request-time fields whose value is expected to vary between two HTTP statements:

- `generated_at_server`;
- `organization_business_date`.

Organization business-date correctness itself remains covered by the dedicated projection/database tests. The conformance fixture deliberately uses an undated primary Action so removing business-date fields cannot hide a provider-dependent Action bucket difference.

Array ordering, IDs, display values, versions, Case/Action contents, Membership roles/capabilities, historical facts and all other payload fields remain part of the exact comparison.

## Identity and leakage rules

Every covered projection must resolve to the same application-owned AppUser regardless of which linked provider identity is used.

Projection payloads must not expose provider/session internals such as:

- `provider_key`;
- `issuer`;
- `external_subject`;
- `auth_subject`;
- access/refresh tokens;
- provider session ids.

The provider transport may use those values internally. They are not Xueqing business projection fields.

## Authorization boundary

This gate does not weaken the existing per-projection authorization rules.

Each projection still re-evaluates its own live authority:

- Personal teaching projections require the accepted live teaching context;
- OrganizationManagement requires live owner/admin Membership;
- management visibility never creates teaching responsibility;
- a projection snapshot is never write authorization.

Provider Session/Auth and IdentityLink revoke semantics remain covered by their dedicated conformance gates.

## Provider replacement rule

Supabase is the current development/reference provider, not the semantic owner of these contracts.

A future provider adapter is conformant only when equivalent fictional fixtures can satisfy the same projection contracts and business payload expectations without changing Domain/ViewModel code or application-owned IDs.

## Non-goals

This gate does not:

- select a production provider or region;
- prove Storage behavior;
- change projection schemas;
- add realtime or generic cursor sync;
- test email delivery;
- add product UI;
- make provider-issued identifiers durable Xueqing business identity.
