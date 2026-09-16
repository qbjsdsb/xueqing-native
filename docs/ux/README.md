# Xueqing UX Documentation Index

This directory separates durable platform principles from the current **Native UX & Visual Foundation v1 Candidate**.

## Durable platform principles

- `WINDOWS_UX.md` — Windows is **Organize + Think**: desktop workspace, list/detail, keyboard/mouse efficiency, adaptive layout, accessibility.
- `ANDROID_UX.md` — Android is **Capture**: Today, students, Quick Capture, evidence/photo entry, one-handed use, resilient input.

These remain the short platform principle documents.

## Native UX & Visual Foundation v1 Candidate

The current detailed candidate is intentionally split by responsibility:

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

3. `PROTOTYPE_ACCEPTANCE_MATRIX.md`
   - executable WinUI/Compose evidence required before v1 acceptance;
   - Windows width/DPI/text/theme/keyboard/focus matrix;
   - Android phone/adaptive/IME/predictive-back/process-death/accessibility matrix;
   - hostile deterministic fictional fixtures;
   - explicit pass/fail conditions.

## Status

The detailed native foundation is **Candidate — Prototype Required**.

Do not freeze final breakpoint values, pane widths, Case reading width, management column-collapse thresholds, accent values or other prototype-sensitive constants from documentation alone.

Acceptance requires exact-head executable prototype evidence as defined in `PROTOTYPE_ACCEPTANCE_MATRIX.md`.

## Authority order

UX documents never override domain/security invariants. If there is a conflict, the relevant product/architecture contract wins, especially:

- `../product/PRODUCT.md`
- `../architecture/DOMAIN_MODEL.md`
- `../architecture/COMMANDS.md`
- `../architecture/DRAFT_ENGINE.md`
- `../architecture/LOCAL_STATE_MODEL.md`

GitHub remains the durable engineering source of truth; chat-only design conclusions should not be treated as accepted project state until they are reflected here and reviewed.