# Android UX Principles

Android Xueqing is the classroom capture client.

## Role

**Capture**: Today, recent students, quick capture, evidence/photo entry, current case focus and next action.

## Core target

A teacher should be able to record a common classroom observation in roughly 10–20 seconds without navigating an organization-management hierarchy.

## Navigation

Keep top-level navigation small. Organization administration is secondary on mobile. Personal teaching projection is the default for a teacher who also has management capability.

## Interaction

- one-handed reach and clear tap targets;
- preserve typed input through process death/interruption;
- photo/evidence flow must show upload/sync state without blocking local save;
- avoid desktop tables and dense multi-column layouts;
- explicit offline/pending-sync/conflict states;
- Material 3 and system dark mode, but product hierarchy takes priority over decorative component variety.

## Performance/accessibility

Add macrobenchmark/baseline-profile work early for startup, Today, Student and Quick Capture. Validate common Chinese text scaling, TalkBack semantics, dark mode and low-end/typical Android performance during the spike.