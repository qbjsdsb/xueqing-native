# Android Native Client

Android is optimized for **Capture**, not for reproducing the Windows information architecture.

Accepted platform baseline:

- stable Android 16 / API 36;
- Kotlin + Jetpack Compose + Material 3;
- ViewModel + Coroutines / Flow;
- Room 2.8.5 for encrypted Durable Intent;
- WorkManager 2.11.2 for the accepted queued Observation submission path.

## Accepted native product baseline

The Android line has moved beyond a bootstrap-only spike. Exact-head CI has proved:

- Kotlin/Compose cloud builds on the frozen API 36 toolchain;
- PersonalBootstrap-backed personal teaching roster;
- Student → Student Detail → explicit subject-scoped Quick Capture;
- draft scopes bound by environment / app-user / organization / Student / subject / context;
- transaction epoch barriers so stale autosave cannot resurrect a deliberately discarded draft;
- Activity recreation, background/foreground and real adb `force-stop` → relaunch recovery;
- SQLCipher + Android Keystore encrypted Durable Intent;
- Quick Capture → encrypted Durable Outbox → WorkManager → reference provider → authoritative CreateObservation;
- server-side authority is re-checked when the queued Observation reaches the backend;
- scope switching prevents stale input from being accepted under the previous Student/subject while a prior local save is still completing;
- committed Room schema drift checks.

The broader Android Issue #12 remains open. Acceptance of these product paths does not imply that every IME, accessibility, adaptive-layout, attachment or performance requirement is finished.

## Durable Intent encryption

Accepted design:

```text
Android Keystore non-exportable AES-256 wrapping key
        ↓ AES-GCM
random 32-byte SQLCipher database password
        ↓
wrapped envelope in app noBackupFilesDir
        ↓
SQLCipher for Android Community + Room 2 SupportOpenHelperFactory
        ↓
encrypted xueqing-durable-intent.db
```

Rules:

- use platform Android Keystore directly; do not introduce deprecated `androidx.security.crypto` / `MasterKey` / `EncryptedSharedPreferences` APIs;
- the plaintext SQLCipher password is never persisted to disk;
- an existing database with missing/corrupt key material fails closed instead of silently regenerating keys or deleting data;
- the pre-production plaintext Spike database is deliberately rejected rather than silently migrated;
- Durable Intent remains excluded from Android backup/device-transfer until the separate backup/restore gate;
- explicit purge closes Room and removes DB/WAL/SHM/journal, wrapped envelope and Keystore alias.

## Current gate: Offline Access Lease

Encryption does not prove that cached student data is still authorized while a device is offline.

The active Phase 1 gate is the bounded Offline Access Lease contract:

- candidate maximum duration: 72 hours;
- exact environment / AppUser / organization scope binding;
- monotonic-time expiry during one validated boot session;
- wall-clock rollback detection;
- boot-session change fails closed and requires online revalidation;
- loss of cached projection authority must not silently destroy encrypted drafts or queued teacher work.

The executable reference lives behind `contracts/security/OFFLINE_ACCESS_LEASE_V1.md` and the Android application-security tests. This gate protects **cached projection reads**; it is not a replacement credential for server writes.

## Frozen dependency matrix

The accepted matrix remains intentionally conservative:

- Android Gradle Plugin: `8.13.2`;
- Gradle: `8.13` on CI;
- JDK: `17`;
- Kotlin / Compose compiler plugin: `2.3.21`;
- Compose BOM: `2026.06.00`;
- Activity Compose: `1.13.0`;
- Room: `2.8.5`;
- Lifecycle: `2.10.0`;
- WorkManager: `2.11.2`;
- SQLCipher for Android Community: `4.17.0`;
- AndroidX SQLite support API: `2.7.0`;
- compile / target SDK: `36`;
- minimum SDK: `26`.

Do not opportunistically upgrade Compose, Room, Lifecycle, AGP, Kotlin or API level inside security/product feature PRs. API 37 / newer AGP and later SQLCipher upgrades belong in a separate toolchain gate.

## Package boundary

```text
Android platform entry
  com.xueqing.app.MainActivity
          ↓
Compose presentation
  com.xueqing.app.presentation.*
          ↓
application contracts / product state
  com.xueqing.app.application.*
          ↓
Durable Intent + provider infrastructure
  com.xueqing.app.durability.*
  com.xueqing.app.infrastructure.*
```

Rules:

- `MainActivity` owns lifecycle/bootstrap only;
- Composables and ViewModels must not call Supabase/provider SDKs directly;
- provider-specific behavior stays behind Infrastructure Adapters;
- Android platform types must not leak into provider-neutral application/domain semantics;
- do not create empty layers merely to make the tree look architecturally complete.

## What remains outside the accepted slices

The broader Android Issue #12 still needs evidence, where relevant, for:

- Chinese IME composition and predictive Back;
- TalkBack, one-handed use and large text;
- account / organization switch and scoped purge policy;
- photo/attachment staging and retention;
- phone / foldable / tablet / desktop-windowing adaptation;
- backup/restore behavior rather than the current safe exclusion;
- cold start and performance evidence.

The next product line after the Offline Access Lease gate is the Learning Case + Primary Action vertical slice. UX semantics remain governed by `docs/ux/*`, especially `INTERACTION_STATE_CONTRACT.md`.
