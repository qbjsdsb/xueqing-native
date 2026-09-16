# AGENTS.md — Xueqing Native Engineering Constitution

This is the primary execution guide for human and AI contributors.

## 1. Product identity

Xueqing Native is a teacher and tutoring-organization judgment/action workspace.

```text
Student → Subject Profile → Learning Case → Evidence → Intervention → Assessment → Next Action → Stable/Closed
```

It is not an ERP, CRM, billing system, generic todo app, spreadsheet clone or AI chatbot.

## 2. Current project state

Do not hard-code the current phase in this constitution. Read `docs/project/PROJECT_STATE.yaml` first. Dynamic facts such as main HEAD, open PRs and CI status must be queried from GitHub at handoff time rather than copied into durable state files.

## 3. Platform decisions

- Windows: C# + .NET 10 LTS + WinUI 3 + stable Windows App SDK + CommunityToolkit.Mvvm.
- Android: Kotlin + Jetpack Compose + Material 3 + ViewModel + Coroutines/Flow + Room + WorkManager.
- Backend: PostgreSQL-first. Supabase is the development/reference provider; production provider/region remains a production gate.
- Android and Windows share contracts and semantics, not UI code.

## 4. Sources of truth

For implementation decisions, use this order:

1. accepted ADRs under `docs/adr/`;
2. durable project state in `docs/project/PROJECT_STATE.yaml`;
3. architecture/contracts;
4. Git migrations and database tests once backend implementation begins;
5. application code;
6. screenshots/mockups.

Dynamic execution truth is GitHub itself: current branch/HEAD, active Draft PR and exact-HEAD CI evidence. Chat memory or an older handoff summary never overrides GitHub.

## 5. Domain invariants inherited from legacy Xueqing

Preserve unless a superseding ADR changes them:

- one canonical Student per real student within an organization;
- enrollment/assignment/responsibility changes preserve history;
- Learning Case lifecycle is `new → confirmed → intervening → pending_verification → stable → closed`;
- reopen is a command/event, not a seventh state;
- assessment passed does not automatically mean stable or closed;
- active-profile formal open Cases require a legal responsible owner and exactly one pending primary Action;
- finalized teaching facts/history are append-only in meaning; corrections are explicit;
- high-risk commands are atomic and idempotent;
- organization supervision does not imply teaching responsibility.

## 6. Actor, supervisor and responsibility

Always distinguish Actor, Supervisor and Responsible Teacher. Owner/admin authority may supervise but must never fabricate a teaching assignment or silently take Case/Action ownership.

## 7. Teaching Fact Gate

Teaching facts require server-side re-evaluation of the applicable live session, active membership, teacher capability, teaching scope, active Student Subject Profile, legal active Student Teacher Assignment and operation permission. UI state is never authorization.

## 8. High-risk command contract

Lifecycle/governance commands must not be assembled from multiple client CRUD calls. Use:

```text
operation_id
+ expected versions/current-relation snapshot
+ server authorization
+ deterministic lock/re-read
+ final invariant validation
+ operation-bound event/audit
+ atomic commit
+ reusable committed result/receipt
```

On timeout/unknown result, keep the same `operation_id` and resolve/retry that intent.

## 9. Local-first boundaries

The target is Local-first Command Sync, not a generic multi-master database. PostgreSQL is authoritative for formal business state.

V1 pull uses scoped Projection Snapshots. V1 push uses a Durable Outbox. A generic change-list/cursor protocol is a later optimization only if measured scale requires it.

Initially offline-friendly: protected drafts, Quick Capture/observations designed for queued submission and explicitly approved low-risk append operations.

Initially online-only: formal lifecycle transitions, membership/credential changes, teacher handoff/reassignment, student merge and other governance operations requiring fresh authority.

Never resolve domain conflicts with client timestamps or Last Write Wins.

## 10. Local state and privacy

Projection Cache is disposable and rebuildable. Durable Intent (drafts, outbox, pending attachment staging and operation metadata) must survive cache rebuilds and must not use destructive migration fallbacks.

Offline access is finite. Local encryption, system-backup exclusion, purge/account-switch rules, attachment retention and lease anti-clock-rollback behavior are Spike gates before production data.

## 11. Provider isolation and environments

View/ViewModel/domain code must not call provider SDKs directly. Use Remote/Provider Adapters.

Development, staging and production are separate trust domains. Ordinary PRs must never receive production secrets. A development client must not be able to connect to production by a casual URL edit.

## 12. UI principles

Android = Capture: one-handed, fast classroom entry, obvious next actions.

Windows = Organize + Think: keyboard/mouse productivity, list-detail workspaces, search/filtering, accessibility and one centralized desktop breakpoint source.

Chinese IME composition, large text, dark/high-contrast modes, keyboard focus and navigation are acceptance concerns, not polish extras.

## 13. Testing authority

Use the lowest layer that proves the invariant:

- RLS/authorization → PostgreSQL tests;
- command atomicity/idempotency → database/integration tests;
- sync retry/conflict → sync engine tests;
- projection/state → repository/ViewModel tests;
- critical platform behavior → small E2E/device smoke suites.

Do not use UI tests as proof of database security.

A green run applies only to the exact commit SHA that produced it. Any subsequent commit requires fresh evidence for affected gates.

## 14. Cloud reproducibility

Formal code must be buildable/testable from Git plus controlled secrets on standard cloud runners. Do not require a developer's local Visual Studio, Android Studio, Docker or signing machine as the only validation path.

Use small PRs, short-lived branches and GitHub CI evidence. Avoid deep stacked PR chains.

## 15. AI continuity

Follow `docs/ai/BOOTSTRAP.md` on every fresh handoff. Important work must never exist only in an agent scratch filesystem or chat. Push valuable work to a branch and use a Draft PR as the dynamic handoff record.

Memory is an accelerator, not an engineering source of truth.

## 16. Data and secrets

This repository is public. Only fictional or irreversibly anonymized data is allowed. Never commit tokens, passwords, service-role keys, DB passwords, signing private keys/certificates, real personal data, production attachments/exports or credential-bearing logs.

## 17. Scope discipline

Prefer one auditable outcome per PR. Do not bundle unrelated architecture, UI, database and release changes. Introduce abstractions only when they enforce a real boundary, improve testability/provider replacement or protect domain rules.
