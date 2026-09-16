# Android Architecture Spike

Android is optimized for **Capture**, not for reproducing the Windows information architecture.

Target architecture direction:

- Kotlin + Jetpack Compose + Material 3;
- ViewModel + Coroutines / Flow;
- Room as the local UI source-of-truth candidate;
- WorkManager as the queued-background-retry candidate.

These are Spike targets. Room, WorkManager, local encryption and production sync are **not** considered accepted merely because they appear in this list.

## Current bootstrap slice

The first Android PR deliberately proves only the smallest cloud-reproducible native shell:

- stable Android build toolchain;
- AGP 9 built-in Kotlin (no legacy `kotlin-android` plugin);
- Compose compiler Gradle plugin;
- edge-to-edge `ComponentActivity`;
- restrained Material 3 Light/Dark theme;
- Personal top-level destinations fixed to `今日 / 学生 / 学情`;
- deterministic fictional data only;
- unit test that prevents `记录` from becoming a fourth top-level destination;
- cloud unit-test + lint + debug-build gate.

The bootstrap UI is **not** a visual freeze. It intentionally avoids production Quick Capture save semantics until the Draft Engine / local durability slice exists.

### Toolchain candidate pinned for the bootstrap

- Android Gradle Plugin: `9.4.0`;
- Gradle: `9.6.0` on CI;
- JDK: `17`;
- Kotlin / Compose compiler plugin: `2.4.20`;
- Compose BOM: `2026.08.00`;
- Activity Compose: `1.13.0`;
- compile / target SDK: `37` for the Spike;
- minimum SDK: `26` for the Spike only.

`minSdk = 26` is a build-floor candidate for this architecture Spike, **not** a frozen production support policy. Product support range must be accepted separately with real compatibility evidence.

The first bootstrap intentionally does **not** adopt preview AGP solely for screenshot testing. Screenshot/golden tooling is a separate evidence Spike because the official Compose screenshot-testing stack is still evolving.

## Required follow-up slices

Phase 1/2 Android work must still validate:

- Today / Student / Student Detail / full-screen Quick Capture interaction prototypes;
- Draft Engine semantics and process-death recovery;
- Room projection cache vs Durable Intent separation;
- queued Outbox writes and WorkManager retry;
- offline → process death → restart → resume/reconcile sync;
- photo/draft handling with fictional images only;
- account / organization switch and strict local-scope cleanup;
- encrypted local storage / Android Keystore strategy in its dedicated gate;
- predictive Back;
- one-handed usability;
- 200% text scaling;
- TalkBack;
- edge-to-edge / IME handling;
- phone / foldable / tablet / desktop-windowing adaptation;
- cold start and baseline-profile / Macrobenchmark evidence.

UX semantics are governed by:

- `../../docs/ux/NATIVE_UX_FOUNDATION.md`;
- `../../docs/ux/CORE_SCREEN_ARCHITECTURE.md`;
- `../../docs/ux/INTERACTION_STATE_CONTRACT.md`;
- `../../docs/ux/PROTOTYPE_ACCEPTANCE_MATRIX.md`.

The executable Android acceptance gate is tracked in GitHub issue #12.
