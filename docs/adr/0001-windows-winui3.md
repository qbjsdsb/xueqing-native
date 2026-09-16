# ADR-0001 — Windows client uses WinUI 3

**Status: Accepted**

## Decision

The Windows client uses C#/.NET 10 LTS, WinUI 3 and the stable Windows App SDK. CommunityToolkit.Mvvm is the default MVVM utility.

## Why

Xueqing Windows is a new native line-of-business desktop workspace. WinUI 3 aligns with modern Windows navigation, Fluent controls, theme/accessibility integration and Microsoft's current new-app direction.

## Constraints

- stable channel only; no Preview/Experimental dependency for production-critical behavior without a new ADR;
- Windows UI is desktop-native, not a widened Android layout;
- DataGrid absence must not force the product into third-party-grid architecture; prefer list/detail and introduce a grid only for a proven use case;
- a performance/DPI/MSIX Architecture Spike is a hard gate before broad implementation.