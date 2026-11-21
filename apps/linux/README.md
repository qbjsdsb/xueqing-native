# Xueqing Native Linux Desktop Client (`Xueqing.Linux`)

Native Linux desktop workbench built with **C# / .NET 10 LTS + GTK4 + libadwaita (`GirCore`)**, sharing the platform-neutral desktop domain, PostgREST adapters, Durable Recovery stores, and `MainWindowViewModel` with Windows while using native Linux desktop & security primitives:

- **UI**: GTK4 + libadwaita (`Adw.ApplicationWindow`, `Adw.NavigationSplitView`, virtualized `Gtk.ListView`, native Wayland `text-input-v3` Chinese IME composition, system dark/high-contrast themes, `Gtk.FileDialog`).
- **Local Data Security (Fail-Closed)**: `SQLite3MC 2.4.0` encrypted SQLite databases + 256-bit random master keys stored exclusively in Linux Secret Service (`libsecret-1.so.0` / `org.freedesktop.secrets`, e.g., GNOME Keyring or KWallet) + `/proc/sys/kernel/random/boot_id` monotonic clock sampling. Never falls back to plaintext SQLite or unencrypted key files.
- **Protocol & AI MCP Interop**: Single-instance `xueqing://` URI handler (`xueqing://today`, `xueqing://student/<uuid>`, `xueqing://learning/<uuid>`) sharing the exact encrypted observation draft store with `apps/mcp/Xueqing.Mcp`.

## Prerequisites (Ubuntu 24.04 / 26.04 or GNOME 46+ Desktop)

- `.NET 10.0.401` SDK (matching `global.json`)
- Runtime libraries (preinstalled on standard Ubuntu/GNOME desktops): `libgtk-4.so.1`, `libadwaita-1.so.0`, `libsecret-1.so.0`

## Build & Test

```bash
dotnet build apps/linux/src/Xueqing.Linux/Xueqing.Linux.csproj -c Release
dotnet build apps/linux/tests/Xueqing.Linux.Tests/Xueqing.Linux.Tests.csproj -c Release
apps/linux/tests/Xueqing.Linux.Tests/bin/Release/net10.0/Xueqing.Linux.Tests --no-banner
```

## Run Locally

```bash
# 1. Verify full local encryption chain (libsecret + SQLite3MC 2.4.0 + boot_id)
apps/linux/src/Xueqing.Linux/bin/Release/net10.0/Xueqing.Linux --security-check

# 2. Automated GTK4/libadwaita + xueqing:// smoke check
apps/linux/src/Xueqing.Linux/bin/Release/net10.0/Xueqing.Linux --smoke-gui

# 3. Launch prototype workspace (1,000 synthetic students + real encrypted local draft shared with Xueqing.Mcp)
apps/linux/src/Xueqing.Linux/bin/Release/net10.0/Xueqing.Linux --prototype

# 4. Launch production workspace (requires deployment_profile.json next to executable)
apps/linux/src/Xueqing.Linux/bin/Release/net10.0/Xueqing.Linux
```

## Desktop Entry & `xueqing://` Protocol Registration

```bash
bash apps/linux/packaging/install-desktop-entry.sh --prototype
xdg-open xueqing://today
```

## Flatpak Manifest

A GNOME Platform Flatpak manifest is provided in `apps/linux/packaging/flatpak/com.xueqing.app.yml` for sandboxed Linux distribution after `dotnet publish -c Release -r linux-x64 --self-contained true`.
