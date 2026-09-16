# Open-source Reference Matrix

External projects are evidence libraries, not substitute product managers.

## Microsoft WinUI Gallery

Borrow: official control usage, Fluent patterns, theme/accessibility examples, responsive/window behavior and known WinUI workarounds.

Do not borrow: sample-app navigation/structure as if it were a production LOB architecture.

## Files (`files-community/Files`)

Borrow: mature WinUI desktop information density, Navigation/Settings patterns, CommunityToolkit.Mvvm usage, command organization and long-lived Windows app practices.

Do not borrow: file-manager domain complexity or dependencies unrelated to Xueqing.

## Microsoft PowerToys

Borrow: DI/service boundaries, Windows integration discipline, DPI/theme/high-contrast test mindset, diagnostics bundle/redaction approach and production release rigor.

Do not borrow: C++/multi-process/module complexity without a specific Xueqing need.

## Android Now in Android

Borrow: Compose/UDF/ViewModel/Repository patterns, local-first data flow, Room/WorkManager, testing, macrobenchmark and baseline-profile discipline.

Do not mirror its modularity mechanically; Xueqing should stay smaller until scale proves the need.

## Joplin

Borrow primarily as a synchronization caution: mature local-first sync remains complex, conflicts must be explicit, and wall-clock timestamps are not a reliable general causal model.

Do not copy: generalized note/document multi-master sync or conflict algorithms into Xueqing.

## Frappe Education / Gibbon

Borrow: students/teachers as long-lived entities, historical relationships, role-specific views and education-domain continuity.

Do not borrow: ERP expansion into admissions, billing, full scheduling, finance or portals. Also respect their licenses; domain ideas are references, not code-copy sources.

## Legacy Xueqing

This is the primary product/domain reference. Borrow the tested domain invariants, permission rules, command semantics, security tests and UX lessons. Do not copy Flutter UI implementation.