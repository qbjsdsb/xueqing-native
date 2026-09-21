# Provider Portability v1

Status: **architecture contract — implementation remains gate-driven**

## Goal

Xueqing may support more than one infrastructure provider without changing product/domain meaning.

The target is:

```text
one Xueqing domain + one contract set
                ↓
      provider-neutral boundaries
          ↙               ↘
 Supabase adapters     CloudBase adapters
```

Provider portability exists to preserve deployment choice, regional compliance options and recoverability. It is not permission to build a multi-master database.

## Authority invariant

For one Xueqing environment at one moment there is exactly **one authoritative provider topology** for formal business state.

Allowed:

- Supabase as the authoritative provider for one environment;
- CloudBase as the authoritative provider for another environment;
- controlled migration from one accepted provider topology to another.

Not allowed:

- active-active writes to both providers for the same environment;
- client-side write fallback from one provider to another after timeout;
- merging conflicting authoritative states by timestamp;
- treating a provider switch as an ordinary UI preference;
- silently creating a second operation id when one provider outcome is unknown.

A transport timeout against the authoritative provider keeps the original Xueqing operation identity and follows the accepted same-operation recovery protocol.

## Stable application identity

Xueqing business identity remains application-owned.

```text
AppUser.id = Xueqing identifier
IdentityLink = provider identity → AppUser
```

Provider user ids, subjects, JWT claims or SDK account objects never become canonical Student, AppUser, Case, Action or Organization identifiers.

A future migration may revoke an old provider IdentityLink and establish a new provider IdentityLink for the same AppUser through an authorized migration/onboarding operation. That transition must preserve Membership, teaching assignment and historical actor identity.

## Deployment profile

A deployment profile selects the accepted infrastructure topology outside Presentation/Domain code.

At minimum it identifies:

- provider id;
- environment/trust-domain id;
- exact primary region;
- public client endpoint/configuration;
- enabled provider capabilities;
- accepted Storage/Delivery/backup policies.

Provider credentials and admin secrets are never part of a publishable client profile.

The provider profile is environment/deployment configuration. V1 does not expose a user-facing "switch cloud provider" toggle.

## Capability model

Provider capabilities describe infrastructure facts, not different Xueqing business rules.

Examples:

- exact-region support;
- PostgreSQL / transaction semantics;
- RLS or proven authorization-equivalent enforcement;
- provider Auth/session features;
- private object Storage authorization;
- CDN/cache controls;
- trusted server/function regional controls;
- backup/export/restore primitives;
- log/diagnostic residency controls.

A missing capability may block a provider or require a provider-specific Infrastructure implementation.

It must never produce rules such as:

```text
if provider == cloudbase:
    skip Teaching Fact Gate
```

Domain invariants are provider-independent.

## Required adapter surfaces

A production-capable second provider must prove the accepted semantics for every product surface it implements.

### Session / Auth

- authenticated provider subject resolves only through active IdentityLink;
- revoked/disabled identities fail closed;
- account/environment switch cannot expose old-scope state;
- sign-out/revocation semantics are mapped explicitly.

### Commands

- stable caller-supplied `operation_id`;
- expected-version/current-relation checks remain authoritative;
- server-side actor/Teaching Fact Gate resolution;
- deterministic transaction/lock semantics where the command requires them;
- exact committed receipt replay;
- same operation + different payload rejection;
- ResultUnknown never becomes a new intent.

### Projections

- provider-neutral projection payload;
- current authorization is evaluated server-side;
- no provider identity/session fields leak into domain payloads;
- missing rows are not interpreted as permission.

### Private Attachment Storage

- object path/identity remains Xueqing-controlled;
- upload/read authorization preserves Teaching Fact / IdentityLink semantics;
- object bytes remain private;
- CDN/signed-link/cache behavior is explicit;
- Attachment failure never rewrites Observation authority.

### Trusted delivery / side effects

- Invitation is authoritative business intent;
- delivery is provider/trusted-server infrastructure;
- already-sent delivery replay does not send twice;
- ambiguous delivery keeps the original operation identity;
- provider/admin credentials remain server-side.

## Shared conformance matrix

A provider is not considered supported because its API resembles another provider.

The same fictional business scenarios must be executed against each provider adapter where applicable:

- legal teacher succeeds;
- no live assignment is denied;
- cross-organization access is denied;
- revoked IdentityLink is denied;
- former teacher after handoff cannot append;
- same operation replay returns the committed result without duplicate side effects;
- same operation id with a different payload is rejected;
- ResultUnknown keeps the same operation/recovery record;
- projection payload remains provider-neutral;
- private Attachment authorization fails closed;
- already-sent Invitation Delivery is not duplicated.

Provider-specific tests may supplement this matrix but may not weaken it.

## Supabase reference status

Supabase remains the development/reference provider until superseded by an accepted decision.

Existing Supabase conformance evidence is reusable as a semantic baseline. It is not evidence that another provider passes the same contract.

## CloudBase second-provider rule

CloudBase may become a supported provider through a dedicated conformance spike.

The spike must use fictional data and must not begin a production cutover. It must first prove the smallest high-risk surfaces:

1. PostgreSQL / transaction compatibility required by Xueqing commands;
2. application-owned IdentityLink mapping from CloudBase Auth identity;
3. authorization / RLS-equivalent enforcement;
4. provider-neutral command and projection behavior;
5. private Attachment authorization and explicit cache/delivery policy;
6. trusted Invitation Delivery semantics;
7. export/restore primitives needed by the Backup/Restore contract.

If a required invariant cannot be proved without changing Xueqing domain semantics, the provider fails the spike. The domain is not weakened to make the provider fit.

## Migration between providers

Changing the authoritative provider is a migration/recovery operation.

A safe cutover requires:

```text
source authority
→ documented consistency point / write control
→ provider-portable DB export
+ protected Attachment object manifest and bytes
→ fresh target environment
→ migrations / restore
→ provider conformance + authorization negative tests
→ identity transition
→ authoritative cutover
→ source retirement / rollback handling
```

The migration must preserve Xueqing ids, command receipts, historical actors, assignments, Case/Action state and Attachment integrity.

Cross-provider migration is not implemented by dual-writing live traffic.

## Backup format requirement

The Backup/Restore gate should prefer logical PostgreSQL and object-manifest formats that do not unnecessarily bind recovery to one provider.

The first disaster-recovery gate may restore to the same provider when that is the accepted production topology. However, its archive/manifest design must not make a later Supabase ↔ CloudBase migration impossible without rewriting Xueqing domain data.

## CI strategy

When a second provider implementation exists, CI should expose provider-specific jobs behind one semantic matrix rather than duplicate product tests manually.

Conceptually:

```text
Provider Conformance Fixtures
          ├── Supabase
          └── CloudBase
```

A green Supabase job never substitutes for a missing CloudBase job, and vice versa.

## Explicit non-goals

- no active-active multi-provider writes;
- no generic distributed transaction across providers;
- no CRDT/LWW reconciliation;
- no client provider switch button;
- no speculative abstraction for providers that have no accepted use case;
- no production secrets or real personal data in public CI;
- no claim that feature compatibility equals legal/regulatory compliance.
