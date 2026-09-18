# Xueqing UX Documentation Index

This directory separates durable platform principles, the existing **Native UX & Visual Foundation v1 Candidate**, and the new **v2 Native Reference RFC** that is being explored through executable native prototypes.

## Durable platform principles

- `WINDOWS_UX.md` — Windows is **Organize + Think**: desktop workspace, list/detail, keyboard/mouse efficiency, adaptive layout, accessibility.
- `ANDROID_UX.md` — Android is **Capture**: Today, students, Quick Capture, evidence/photo entry, one-handed use, resilient input.

These remain the short platform principle documents.

## Native UX & Visual Foundation v1 Candidate

The current detailed candidate is split by responsibility:

1. `NATIVE_UX_FOUNDATION.md`
   - cross-platform semantic/visual contract;
   - platform-native typography/geometry/theming direction;
   - interaction-state, accessibility, dark/high-contrast and anti-pattern rules;
   - values that remain Prototype Required.

2. `CORE_SCREEN_ARCHITECTURE.md`
   - Personal vs Organization workspace separation;
   - Today;
   - Students / Student Detail;
   - Learning Case;
   - Quick Capture;
   - Organization Learning;
   - Organization Management;
   - context preservation and failure/conflict presentation.

3. `INTERACTION_STATE_CONTRACT.md`
   - protected local draft, queueable low-risk intent, authoritative online command, projection refresh/rebuild;
   - user-visible distinctions between editing, locally safe, waiting to sync, committed, unknown-result, conflict, rejection and permission/scope changes;
   - Back/navigation, account/organization switch and attachment-state rules.

4. `PROTOTYPE_ACCEPTANCE_MATRIX.md`
   - executable WinUI/Compose evidence required before visual/layout values are accepted;
   - Windows width/DPI/text/theme/keyboard/focus matrix;
   - Android phone/adaptive/IME/predictive-back/process-death/accessibility matrix;
   - hostile deterministic fictional fixtures and explicit pass/fail conditions.

## Native UX Foundation v2 — Reference RFC

- `NATIVE_UX_FOUNDATION_V2_RFC.md`

This RFC is the current redesign proposal. It does **not** change domain/security/sync contracts and it does **not** supersede the v1 candidate merely because a mockup looks better.

Its main direction is:

- one domain, two native applications;
- Windows **Teaching Desk**: native shell + compact navigation + Roster + Chronicle + optional Inspector;
- Android **Teaching Pocket**: Agenda + Roster + Chronicle + Capture;
- account, workspace and destination remain separate concepts;
- Evidence Thread is the main product-specific visual language;
- platform conventions take precedence over cross-platform pixel consistency;
- OS-level native capabilities such as Windows taskbar integration or Android shortcuts/widgets are follow-up productivity layers, not first-prototype blockers.

The RFC requires two executable reference prototypes before broader rollout:

1. Windows Students / Chronicle;
2. Android Student / Quick Capture.

## Status

The v1 detailed native foundation remains **Candidate — Prototype Required**.

The v2 document is **RFC — executable native reference prototypes required**.

Do not freeze final breakpoint values, pane widths, Chronicle reading width, management column-collapse thresholds, accent values, Offline Access Lease UX, unauthorized-draft recovery behavior or other prototype/protocol-sensitive constants from documentation or generated images alone.

Acceptance requires exact-head executable prototype evidence as defined in `PROTOTYPE_ACCEPTANCE_MATRIX.md` and semantic-state evidence from `INTERACTION_STATE_CONTRACT.md`.

## Authority order

UX documents never override domain/security invariants. If there is a conflict, the relevant product/architecture contract wins, especially:

- `../product/PRODUCT.md`
- `../architecture/DOMAIN_MODEL.md`
- `../architecture/COMMANDS.md`
- `../architecture/DRAFT_ENGINE.md`
- `../architecture/LOCAL_STATE_MODEL.md`
- `../architecture/AUTHORIZATION.md`

GitHub remains the durable engineering source of truth; chat-only design conclusions should not be treated as accepted project state until they are reflected here and reviewed.
