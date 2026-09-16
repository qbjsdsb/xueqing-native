# Windows Architecture Spike

Target: C# + .NET 10 LTS + WinUI 3 + stable Windows App SDK + CommunityToolkit.Mvvm.

Phase 1 must validate the platform before formal business implementation. The spike uses fictional data only and must cover:

- cold/warm startup;
- `NavigationView` shell and list/detail workspace;
- 1,000 students and at least 10,000 timeline items with virtualization;
- search/filtering and keyboard navigation;
- continuous window resize and a single centralized layout breakpoint source;
- 100%, 150%, 200% DPI; dark/light/high-contrast modes;
- SQLite local store, Outbox and sync cursor persistence;
- local-database encryption options and key-storage feasibility;
- MSIX build/install/upgrade;
- diagnostic bundle generation with sensitive-data redaction.

No formal student business UI should be built here until the spike gate passes.