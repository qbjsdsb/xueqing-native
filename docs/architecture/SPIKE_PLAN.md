# Architecture Spike Plan

The Spike is a risk-reduction product, not a prototype that silently becomes production. Use only fictional deterministic data and cloud-reproducible workflows.

## Fixture scale

Target at least 10 organizations, 100 teachers, 1,000 students, 10,000 Learning Cases and 50,000 timeline facts/events.

## Windows gate

Validate .NET 10 + stable WinUI 3 toolchain, startup/navigation, list-detail/virtualization, 1,000-student search/filter, large timeline without eager XAML explosion, centralized breakpoints, repeated resize/480px-height stress, 100/150/200% DPI, light/dark/high-contrast, keyboard-only journey, Chinese IME composition, SQLite/local-write coordination, Projection Cache generation replacement, crash-safe Durable Outbox, encryption/key-storage candidates, MSIX install/upgrade/uninstall and redacted diagnostics export.

WinUI GUI automation on GitHub-hosted Windows is a Spike itself; do not make an unproven fragile GUI runner the only blocking correctness gate.

## Android gate

Validate Compose/Room/WorkManager, Today/Student/Quick Capture, Room-backed UI source of truth, offline intent → process death → restart → retry, projection replacement, photo/draft lifecycle, account/environment cleanup, offline-lease lockout, Chinese IME/predictive Back, large text/TalkBack/dark mode and Macrobenchmark/Baseline Profile. Use GitHub-hosted Linux emulator for routine gates; cloud physical-device testing may be added for release candidates.

## Cross-client gate

Simulate append-compatible facts, stale lifecycle commands, timeout after server commit, membership/assignment reduction while offline, old token after disable/reset and duplicate retry with the same `operation_id`.

Expected: no duplicate formal side effects, no LWW corruption, explicit conflicts and scope/cache reconciliation after authorization validation.

## Backend/provider gate

Prove organization isolation, assignment/scope denial, manager-supervision responsibility separation, old-token/live-session security, transaction rollback/idempotency, versioned Projection contracts, candidate `core/private/api` schema boundaries, private Storage access, RLS performance/explain baselines and backup+Storage restore plan.

## Cloud-only acceptance

Every Spike must publish reproducible GitHub CI evidence for its exact SHA. A local-only success is not sufficient evidence. Manual/visual exceptions must be explicitly named rather than silently assumed.
