# Evolvability Boundaries

## Goal

Keep product/domain meaning stable while allowing Android and Windows surfaces to evolve independently.

These boundaries are intentionally small and executable. They protect only rules that are already true in the repository; they do not force speculative abstractions.

## Frozen dependency direction

### Windows

```text
Xueqing.Windows.Core
        ↑
Xueqing.Windows.Presentation

Xueqing.Windows.Core
        ↑
Xueqing.Windows.Infrastructure

Presentation + Infrastructure
        ↑
Xueqing.Windows (WinUI composition root)
```

Rules:

- Core does not reference WinUI, Infrastructure, Presentation, or Windows App SDK types.
- Presentation is pure .NET and may reference Core plus UI-independent helper packages only.
- Presentation does not reference WinUI or Infrastructure.
- Infrastructure does not become a View/ViewModel layer.
- WinUI converts presentation-neutral state such as `bool` into platform types such as `Visibility`.

### Android

The app remains a single Gradle application module for now. Package boundaries carry the architecture:

- `application`, `infrastructure`, and `durability` must not depend on `presentation`.
- `presentation` must not import provider SDK types.
- The current Quick Capture ViewModel still has a known durability composition seam. Do not broaden it. Evidence/Attachment is the planned point to extract the shared coordinator/repository boundary when a real second use case exists.

## UI evolution rule

A presentation-only redesign should not require backend, provider, authorization, sync, or durable-intent changes unless the product behavior itself changes.

Shared semantics do not require shared pixels:

- Android remains native Compose / Material 3.
- Windows remains native WinUI / Fluent.
- Typography/layout/component values are platform-specific.
- Domain terms and interaction-state meaning remain aligned.

## Feature evolution rule

New capabilities should enter as vertical slices:

```text
contract/domain rule
→ backend command/projection
→ provider-neutral application boundary
→ platform presentation
→ affected CI evidence
```

Do not create generic CRUD/service abstractions merely to reduce file count.

Removing a UI surface does not automatically delete historical data or an older compatible contract. Follow the N-1 compatibility policy before server/schema retirement.

## Enforcement

`tools/architecture/check-boundaries.py` runs in the Foundation workflow.

The guard is deliberately tightened only when the codebase has already established a cleaner boundary. Do not add broad exceptions just to make a new dependency pass.
