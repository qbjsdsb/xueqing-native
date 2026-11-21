# ADR-0014 — Linux desktop client uses .NET 10, GTK4 and libadwaita (GirCore)

**Status: Accepted (Formal Linux Client & Platform Contracts Implemented)**

## Decision

The Linux desktop client uses C#/.NET 10 LTS, GTK4, and libadwaita via `GirCore.Adw-1` / `GirCore.Gtk-4.0` (stable channel `0.8.1+`), sharing the platform-neutral `net10.0` desktop Core, Infrastructure, and Presentation layers with Windows. Local Durable Intent encryption reuses `SQLite3MC.PCLRaw.bundle` (2.4.0) with a 256-bit random master key protected by Linux Secret Service (`libsecret-1.so.0` / `org.freedesktop.secrets`), and Offline Access Lease monotonicity uses `/proc/sys/kernel/random/boot_id` + `Environment.TickCount64`.

## Why

- Preserves the "Share semantics, not UI" and true-native platform principle established by ADR-0001 (WinUI 3) and ADR-0002 (Jetpack Compose).
- Reuses ~18.7k lines of already-verified `net10.0` desktop domain logic, PostgREST adapters, Durable Outbox/Recovery stores, and `MainWindowViewModel`, avoiding a third independent client rewrite.
- GTK4 + libadwaita provides native Wayland `text-input-v3` Chinese IME composition, fractional DPI scaling, system dark/high-contrast themes, AT-SPI2 accessibility, and `Adw.Application.RunWithSynchronizationContext` for seamless `async/await` marshaling.

## Spike Evidence (`apps/linux/spikes/Xueqing.Linux.NativeSpike`)

Verified on Ubuntu 26.04.1 LTS (Wayland, x64, .NET 10.0.401):

- `GirCore.Adw-1` 0.8.1 and `GirCore.Gtk-4.0` 0.8.1 ship native `net10.0` assemblies and build with zero warnings (`TreatWarningsAsErrors=true`).
- `libsecret` + `SQLite3MC 2.4.0` WAL roundtrip passes with zero plaintext marker leakage, rejects wrong keys, and fails closed when the keyring entry or `DBUS_SESSION_BUS_ADDRESS` is missing.
- Unmodified `MainWindowViewModel` (1,000 synthetic students, search filter, `WindowLayoutPolicy`) runs cleanly inside `Adw.ApplicationWindow` + `Adw.NavigationSplitView` + `Gtk.ListView` with main-thread `SynchronizationContext` preservation.

## Constraints

- Fail closed whenever Secret Service is unavailable or locked; never fall back to plaintext SQLite or unencrypted key files.
- Prefer Flatpak (GNOME runtime) as the primary sandboxed distribution format alongside distro-compatible packaging.
- Linux platform enablement in production requires adding `'linux'` to backend compatibility policies and JSON schemas in a dedicated migration PR.
