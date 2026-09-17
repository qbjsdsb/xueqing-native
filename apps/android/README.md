# Android Native Client

Android is optimized for **Capture**, not for reproducing the Windows information architecture.

Accepted platform baseline:

- stable Android 16 / API 36;
- Kotlin + Jetpack Compose + Material 3;
- ViewModel + Coroutines / Flow;
- Room 2.8.5 for Durable Intent / later projection storage;
- WorkManager reserved for later queued background scheduling.

## Accepted bootstrap and Draft Durability gates

The native shell and local text-draft durability are already accepted. Exact-head CI has proved:

- Kotlin/Compose cloud builds on the frozen API 36 toolchain;
- Quick Capture drafts scoped by environment / app-user / organization / Student / subject / context;
- transaction epoch barriers so stale autosave cannot resurrect a deliberately discarded draft;
- Activity recreation and background/foreground recovery;
- real adb `force-stop` → relaunch recovery from Room;
- committed Room schema drift checks.

These facts mean **local draft durability is accepted**, not that the full Android Issue #12 is finished and not that any remote save/sync claim exists.

## Current gate: encrypted Durable Intent

The current security gate moves the accepted Draft database from plaintext Room storage to a pre-production encrypted baseline without changing product behavior.

Design:

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
- the plaintext SQLCipher password is never persisted to disk; SQLCipher's Room 2 helper retains it only in process memory for the helper lifetime;
- if an existing database loses either its wrapped envelope or Keystore wrapping key, opening **fails closed** instead of silently regenerating keys or deleting data;
- this is a pre-production baseline: an old plaintext Spike database is deliberately rejected rather than receiving a production migration that no production user yet needs;
- the database is excluded from Android backup/device-transfer rules until the separate backup/restore gate is designed and proven;
- the wrapped password envelope lives in `noBackupFilesDir`;
- explicit purge closes the Room singleton, removes database/WAL/SHM/journal files, deletes the wrapped envelope and deletes the Keystore alias.

The executable gate must prove correct-key reopen, missing/wrong key fail-closed behavior, absence of a plaintext sentinel in database/WAL/SHM, Keystore non-exportability, explicit purge, backup exclusion and force-stop recovery on the encrypted database.

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
- SQLCipher for Android Community: `4.19.0`;
- AndroidX SQLite support API: `2.7.0`;
- compile / target SDK: `36`;
- minimum SDK: `26` for the current native baseline.

Do not opportunistically upgrade Compose, Room, Lifecycle, AGP, Kotlin or API level inside security/product feature PRs.

## Package boundary

```text
Android platform entry
  com.xueqing.app.MainActivity
          ↓
Compose presentation
  com.xueqing.app.presentation.*
          ↓
Durable Intent infrastructure
  com.xueqing.app.durability.*
          ↓
future application/domain + provider adapters
```

Rules:

- `MainActivity` owns lifecycle/bootstrap only;
- Composables and ViewModels must not call Supabase/provider SDKs;
- provider-specific SDKs stay behind future Infrastructure Adapters;
- Android platform types must not leak into shared domain semantics;
- do not create empty layers merely to make the tree look architecturally complete.

## What remains outside this gate

The broader Android Issue #12 remains open. Subsequent real product slices still need to prove, where relevant:

- Chinese IME composition and predictive Back;
- TalkBack, one-handed use and large text;
- Outbox + WorkManager retry and offline/process-death reconciliation;
- account / organization switch and scoped purge policy;
- photo/attachment staging and retention;
- phone / foldable / tablet / desktop-windowing adaptation;
- backup/restore behavior rather than the current safe exclusion;
- cold start and performance evidence.

UX semantics remain governed by `docs/ux/*`, especially `INTERACTION_STATE_CONTRACT.md`. A locally durable encrypted draft is still **not** a queued submission and is still **not** server acceptance.
