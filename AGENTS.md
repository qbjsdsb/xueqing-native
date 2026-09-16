# AGENTS.md — Xueqing Native Engineering Constitution

This file is the primary execution guide for coding agents and human contributors working in this repository.

## 1. Product identity

Xueqing Native is a teacher and tutoring-organization judgment/action workspace. Its core loop is:

```text
Student → Subject Profile → Learning Case → Evidence → Intervention → Assessment → Next Action → Stable/Closed
```

It is not an ERP, CRM, full scheduling product, billing system, generic todo app, spreadsheet clone, or AI chatbot.

## 2. Current phase

The project is in **Phase 0: foundation and risk discovery**.

Until Phase 0 and platform spikes are accepted:

- do not implement broad production business features;
- do not migrate real data;
- do not create provider-coupled production schema assumptions;
- do not add dependencies without a demonstrated need;
- do not copy Flutter UI/source from the legacy project.

## 3. Platform decisions

Windows: C# + .NET 10 LTS + WinUI 3 + stable Windows App SDK + CommunityToolkit.Mvvm.

Android: Kotlin + Jetpack Compose + Material 3 + ViewModel + Coroutines/Flow + Room + WorkManager.

Backend: PostgreSQL-first. Supabase is the default development/reference provider; production provider remains a gate until real compatibility/network/security evidence exists.

Android and Windows share **contracts and semantics, not UI code**.

## 4. Sources of truth

In priority order for implementation:

1. accepted ADRs under `docs/adr/`;
2. domain/command/auth/sync contracts under `docs/architecture/` and `contracts/`;
3. Git migrations and database tests once backend implementation begins;
4. application code;
5. screenshots/mockups.

Never silently contradict an accepted ADR. Change it explicitly with a new/superseding ADR.

## 5. Domain invariants inherited from legacy Xueqing

Preserve unless a new ADR explicitly changes them:

- one canonical Student per real student within an organization;
- enrollment/assignment/responsibility changes preserve history;
- Learning Case lifecycle is `new → confirmed → intervening → pending_verification → stable → closed`;
- reopen is a command/event, not a seventh state;
- assessment passed does not automatically mean stable or closed;
- active-profile formal open Cases require a legal responsible owner and exactly one pending primary Action;
- finalized teaching facts/history are append-only in meaning; corrections are explicit, not silent rewrites;
- high-risk commands are atomic and idempotent;
- organization supervision does not imply teaching responsibility.

## 6. Actor, supervisor, responsibility

Always distinguish:

- **Actor** — the member who executed the command;
- **Supervisor** — a member with organization-level supervision capability;
- **Responsible Teacher** — a member with a legal active teaching relationship to the student+subject.

An owner/admin can supervise without being the responsible teacher. Management role must never fabricate a teaching assignment or silently take Case/Action ownership.

## 7. Teaching Fact Gate

Teaching Evidence, Intervention, Assessment, Quick Capture/new teaching Case and teacher Lesson behavior require, as applicable:

```text
live valid session
+ active organization membership
+ teacher capability
+ matching active teaching subject scope
+ active target Student Subject Profile
+ legal active Student Teacher Assignment
+ operation-specific permission
```

UI state is not authorization. The server must re-evaluate authority.

## 8. High-risk command contract

Lifecycle/governance commands must not be assembled from multiple client CRUD calls.

Use the semantic contract:

```text
operation_id
+ expected aggregate versions/current-relation snapshot
+ server authorization
+ deterministic locks/re-read
+ final invariant validation
+ operation-bound event/audit
+ atomic commit
+ reusable committed result/receipt
```

On timeout/unknown result, retry/query using the same `operation_id`; do not create a new intent and do not guess completion from partial state.

## 9. Local-first boundaries

The target model is **Local-first Command Sync**, not general CRDT.

Local databases are client UX/cache/outbox stores. PostgreSQL remains authoritative for formal business state.

Initially offline-friendly:

- drafts;
- Quick Capture/observations designed for queued submission;
- explicitly approved low-risk append operations.

Initially online-only:

- Case lifecycle transitions such as formal confirm/stabilize/close/reopen unless later proven safe;
- membership/credential changes;
- teacher handoff/reassignment governance;
- student merge;
- other commands whose correctness depends on fresh authority/current relationships.

Never use client timestamps or Last Write Wins to resolve domain conflicts.

## 10. Offline authorization and local privacy

Offline access cannot be indefinite. A future Offline Access Lease must bind cached access to a recent successful authorization validation. Exact duration is a security Spike decision.

Local data scope must be minimized. Local database encryption, sensitive-field encryption, attachment retention, key storage, logout/account-switch cleanup, disabled-account behavior and purge semantics must be validated before production data is allowed.

## 11. Provider isolation

View/ViewModel/Domain code must not call Supabase SDK directly.

Expected direction:

```text
UI → ViewModel → application/domain service → Repository → Local store
                                              ↕
                                           Sync engine
                                              ↕
                                      Remote/Provider Adapter
```

Provider auth identifiers are external identities. Business facts reference application-owned stable IDs.

## 12. UI principles

Android = Capture. Optimize for one-handed, fast classroom entry and obvious next actions.

Windows = Organize + Think. Optimize for keyboard/mouse, list-detail workspaces, high information clarity, search/filtering, accessibility and stable desktop breakpoints.

Never force one platform's navigation/layout model onto the other.

## 13. Testing authority

Use the lowest layer that can prove the invariant:

- RLS/authorization → PostgreSQL tests;
- command atomicity/idempotency → database/integration tests;
- sync retry/conflict → sync engine tests;
- UI projection → ViewModel/UI tests;
- critical real behavior → small end-to-end/device smoke suite.

Do not use UI tests as proof of database security.

## 14. Data and secrets

This is a public repository. Only fictional or irreversibly anonymized data is allowed.

Never commit tokens, passwords, service-role keys, DB passwords, signing private keys/certificates, real personal data, production attachments/exports or credential-bearing logs.

## 15. Open-source references

Borrow ideas deliberately. Before introducing code/patterns from an external project:

1. record the project and relevant file/pattern;
2. explain why it applies to Xueqing;
3. explain what is intentionally not copied;
4. verify license compatibility before copying non-trivial code;
5. prefer small re-implementations over importing a project's architectural complexity.

## 16. Scope discipline

Prefer one auditable outcome per PR. Do not bundle unrelated refactors, architecture changes, UI polish and database migrations.

Avoid architecture for architecture's sake. Introduce interfaces/layers because they enforce a real boundary, support testing/provider replacement, or protect domain rules—not because a diagram looks cleaner.
