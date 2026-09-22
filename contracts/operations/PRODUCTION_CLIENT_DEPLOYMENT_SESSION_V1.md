# Production Client Deployment + Session v1

Status: **active Phase 1 contract**

Tracker: **#75**

## Purpose

Connect signed/release-capable native clients to one accepted authoritative provider environment without making provider identity, credentials or SDK details part of Xueqing Domain.

The public deployment profile and the authenticated session are deliberately separate:

```text
public deployment profile
    + current authenticated provider session
    -> existing Infrastructure adapters
    -> application-owned IdentityLink / AppUser
    -> Xueqing Domain
```

## Public deployment profile

Machine-readable shape: `contracts/schemas/DEPLOYMENT_PROFILE_V1.schema.json`.

A v1 profile contains only:

- stable profile id;
- environment id;
- trust-domain id;
- provider id;
- HTTPS project origin;
- publishable client key;
- required Edge/function region;
- provider-neutral capability set;
- repository + exact source commit provenance.

The profile is allowed to ship in APK/MSIX and source history.

It MUST NOT contain:

- service-role/admin keys;
- database credentials;
- backup credentials or decryption material;
- access or refresh tokens;
- fixed provider subjects or AppUser ids;
- real student/teacher fixtures;
- a user-selectable provider fallback list.

Malformed or incomplete profile input fails closed.

## Origin and trust-domain rules

Production project origin:

- MUST be HTTPS;
- MUST be an origin only: no user-info, path, query or fragment;
- MUST NOT be loopback/local/private-development naming;
- MUST belong to exactly one deployment environment.

Changing `environment_id` or `trust_domain_id` changes the local security scope. Durable local state from one trust domain MUST NOT be reused by another.

## Session boundary

The application-owned session boundary supplies short-lived current access tokens to existing Infrastructure adapters.

It must distinguish at least:

- signed out;
- usable authenticated session;
- refresh required/transiently unavailable;
- revoked/invalid session.

Provider subject remains provider identity evidence only. Business identity continues to resolve through `IdentityLink`; clients do not substitute provider subject for `AppUser.id`.

Logout, revoked identity, unrecoverable refresh failure and environment change fail closed. Background workers must acquire a current session at execution time and must never persist an access token in Durable Intent / Outbox payloads.

## Platform composition

Windows production composition reuses accepted PostgREST readers/commands and their access-token provider seam. Local-reference loopback composition remains development/CI only.

Android release composition reuses `SessionTokenSource`, `HttpRpcTransport`, existing Supabase adapters, and the process-local WorkManager runtime factories. Release must keep insecure-loopback support disabled.

## Singapore V1 policy

The first accepted production topology is Supabase Hosted Singapore. Its production profile must require Edge region `ap-southeast-1`.

The generic contract remains provider-neutral; a later CloudBase profile must satisfy the same application/session semantics rather than introduce provider-specific Domain rules.

## Acceptance

#75 does not close from a valid JSON file alone. Final acceptance also requires fictional hosted E2E proving login, IdentityLink resolution, Bootstrap, Observation, Attachment, Today/Learning reads, Singapore Invitation Delivery, logout/revoke denial, account/environment isolation, and artifact secret scanning.
