# Offline Access Lease v1 — Spike Contract

Status: **Phase 1 spike candidate — executable evidence required**

## Purpose

Offline projection access is finite. Encryption protects bytes at rest, but it does not prove that a teacher still has current authority to read cached student data.

This contract defines the smallest lease needed to bound offline exposure without confusing cached read authority with server authorization.

The lease is **not** a credential and is never sufficient for an authoritative server write. Every queued or online teaching command still re-runs the live server-side authorization and Teaching Fact Gate.

## Threat model

The v1 gate must handle:

- a membership or assignment being revoked while a device is offline;
- a user moving between app-user / organization / environment scopes;
- a user moving the device wall clock backwards to extend cached access;
- monotonic-clock reset / device reboot;
- an expired lease;
- malformed or overlong lease data;
- preservation of encrypted unsynced teacher work when cached projection access is locked.

Instant remote deletion of an already-offline device is impossible. Exposure is reduced through finite leases, encryption, minimum cache scope and mandatory online revalidation when lease trust is lost.

## Scope binding

A lease is bound at minimum to:

- environment / provider trust domain;
- application-owned AppUser;
- organization;
- installation / device-local boot session.

A lease must never authorize a different scope by fallback.

## Candidate policy

For the Phase 1 spike:

- maximum lease duration: **72 hours**;
- tolerated small wall-clock backward adjustment: **5 minutes**;
- device reboot / boot-session change while offline: **fail closed and require online revalidation**.

The 72-hour value is a candidate safety/usability bound for executable validation, not a permanent product promise. Any later change requires explicit contract review and tests.

## Lease record

The client records, after a successful online authorization/projection refresh:

- `issued_at_server`;
- `expires_at_server`;
- `wall_clock_at_validation`;
- `monotonic_at_validation`;
- `boot_session_id`;
- bound environment / AppUser / organization scope.

`expires_at_server - issued_at_server` must be positive and no greater than the client policy maximum.

## Offline evaluation

For an offline projection read:

1. scope must match exactly;
2. lease fields must be structurally valid;
3. current boot session must equal the validation boot session;
4. monotonic time must not move backwards;
5. wall time must not move backwards beyond the rollback tolerance;
6. derive trusted lease time from server issuance plus monotonic elapsed time:
   `trusted_now = issued_at_server + (monotonic_now - monotonic_at_validation)`;
7. allow cached projection access only while `trusted_now < expires_at_server`.

A forward wall-clock jump does not grant extra offline time. Lease expiry is driven by the monotonic delta during the validated boot session.

After a reboot, the v1 spike deliberately refuses to reconstruct trusted elapsed time from the user-adjustable wall clock alone. Online revalidation is required.

## Expiry / trust-loss behavior

When a lease expires or cannot be trusted:

- cached student/organization projection surfaces are locked until online reauthorization;
- the client must not present a generic empty state that looks like “no students”;
- encrypted Durable Intent (drafts, queued Observation intents and recovery metadata) is preserved according to its own policy;
- Projection Cache may be invalidated or rebuilt;
- no formal online-only lifecycle/governance command may be represented as successful offline.

Recommended user-facing meaning:

> 需要联网确认当前权限后继续。你的本机草稿仍然安全保存。

## Durable Intent boundary

Lease denial is a **read-authority/cache** decision. It is not permission to destroy teacher-authored unsynced work.

Account/environment/organization switching may require scope-specific lock/purge policy, but encrypted Durable Intent must follow an explicit non-destructive recovery rule and must never silently cross scopes.

## Acceptance evidence

Before this gate is accepted, executable tests must prove at minimum:

- valid same-boot lease access;
- exact expiry rejection;
- overlong/malformed lease rejection;
- scope mismatch rejection;
- boot-session change rejection;
- monotonic rollback rejection;
- wall-clock rollback beyond tolerance rejection;
- small tolerated clock correction;
- forward wall-clock jumps cannot extend the monotonic expiry window.

Production integration of Projection Cache remains a later step; this spike freezes the safety semantics first.
