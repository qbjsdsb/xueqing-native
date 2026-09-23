# Client Compatibility + Supportability v1

Status: **active Phase 1 contract**

Tracker: **#82**

## Purpose

Make client/backend compatibility an explicit server-authoritative contract before the first public V1 release.

The client does not decide compatibility by comparing version strings locally:

```text
platform + app version + client contract version
                  ↓
      authoritative compatibility endpoint
                  ↓
supported | update_recommended | update_required | security_blocked
```

A transport/configuration failure produces a **local `unknown` state**, not a fabricated server decision.

## Request

The compatibility endpoint accepts only:

- platform: `android` or `windows`;
- exact application version carried by the installed client;
- positive integer client contract version.

Application version is intentionally opaque to the client contract. Provider/server policy owns any platform-specific version ordering.

No AppUser, Organization, Student, assignment or other business identifier is required to answer compatibility.

## Response

Machine-readable shape: `contracts/schemas/CLIENT_COMPATIBILITY_V1.schema.json`.

The response contains:

- `generated_at_server`;
- a stable `policy_revision`;
- the echoed client platform/app/contract versions;
- an authoritative decision;
- machine-readable reason code;
- minimum supported app version;
- recommended/current app version;
- minimum supported client contract version;
- current server contract version;
- optional HTTPS update guidance URI.

## Authoritative states

### supported

The client may continue normal supported operation.

### update_recommended

The client is still compatible and safe. Update guidance is non-blocking.

### update_required

The installed client is no longer contract-compatible/supported for consequential server writes.

Protected local Durable Intent remains readable/recoverable. The application must not destroy drafts/outbox/attachment staging merely because an update is required.

### security_blocked

A concrete security requirement forbids continued consequential server operation for this version.

This state is exceptional and must be backed by an explicit policy revision/reason. It is not a generic “latest version only” switch.

### local unknown

`unknown` is **not** an authoritative response value. It is a local state used when no valid compatibility response can be established.

While unknown:

- do not fabricate `supported`;
- protected drafts may continue within the already-accepted local/offline security boundary;
- consequential online writes fail closed;
- existing finite Offline Access Lease rules still apply;
- retry uses the same environment/session boundary.

## N-1 release window

The normal release policy remains a practical N-1 client window.

Prefer additive projection/command evolution. Incompatible semantics receive a new contract version.

A new client release alone is not justification for blocking the previous compatible release.

## Authority and provider isolation

Compatibility is an application/backend contract.

A provider adapter may transport the request/response, but:

- provider subjects are not compatibility identity;
- provider-specific version rules do not enter Domain;
- the client clock is not authority;
- UI state is not authority;
- changing environment/trust domain requires a fresh compatibility decision.

## Consequential write rule

Before a production client initiates a consequential server write, its current authenticated runtime must have a valid compatibility state allowing that operation.

`update_required`, `security_blocked` and local `unknown` must not initiate the write.

Already-created Durable Intent remains protected. Retrying after a successful client update must preserve the original formal `operation_id` where the accepted command protocol requires it.

## Diagnostics relationship

Compatibility state/reason/policy revision are safe bounded fields for the #82 privacy-safe diagnostics manifest.

The diagnostics export must never include teaching free text, attachment bytes, raw database files, credentials or unrestricted logs.

## Non-goals

- no generic feature-flag service;
- no client-side semantic-version authority;
- no forced update merely because a newer build exists;
- no app-store requirement;
- no custom updater in this contract;
- no provider-specific Domain rules.
