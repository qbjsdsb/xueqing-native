# Windows UX Principles

Windows Xueqing is a desktop teaching workspace, not a stretched mobile screen.

## Role

**Organize + Think**: deep student review, timeline, search/filter, case/action organization and organization supervision.

## Navigation

Use a stable top-level `NavigationView`/desktop shell for a small number of modules. Within student-heavy work, prefer list/detail and keyboard-friendly navigation over repeated full-page drill-down.

Personal teaching and organization supervision are separate workspaces/projections even for an owner who is also a teacher.

## Layout

Define a small explicit desktop breakpoint model (candidate: Compact/Regular/Wide) through one layout service/source of truth. Individual pages must not invent their own width source.

Phase 1 validates actual breakpoint values; do not freeze them from mockups alone.

## Interaction

- keyboard traversal and focus states are first-class;
- right-click/context commands only when discoverable alternatives exist;
- primary actions remain obvious without card soup;
- use whitespace/hierarchy before decorative borders;
- large lists must virtualize;
- empty/loading/error/offline/conflict states are designed, not bolted on.

## Accessibility matrix

At minimum: 100/150/200% DPI, light/dark/high-contrast, text scaling where applicable, narrow/short window combinations, keyboard-only core journey and screen-reader-friendly names for important controls.