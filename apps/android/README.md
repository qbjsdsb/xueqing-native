# Android Architecture Spike

Android is optimized for **Capture**, not for reproducing the Windows information architecture.

Target architecture direction:

- Kotlin + Jetpack Compose + Material 3;
- ViewModel + Coroutines / Flow when real presentation state requires them;
- Room as the local UI source-of-truth candidate for the next durability slice;
- WorkManager as a later queued-background-retry candidate.

These are Spike targets. Room, WorkManager, local encryption and production sync are **not** considered accepted merely because they appear in this list.

## Current bootstrap slice

PR #13 deliberately proves only the smallest cloud-reproducible native shell:

- stable API 36 Android build toolchain;
- Kotlin + Compose compiler plugin on an officially compatible AGP line;
- edge-to-edge `ComponentActivity`;
- restrained Material 3 Light/Dark theme;
- Personal top-level destinations fixed to `今日 / 学生 / 学情`;
- deterministic fictional display data only;
- a platform-entry → presentation package boundary;
- unit tests that prevent `记录` from becoming a fourth top-level destination;
- cloud AAR-metadata + unit-test + lint + debug-APK gates.

The bootstrap UI is **not** a visual freeze. It intentionally exposes no production Quick Capture save action because the Draft Engine / durable local persistence layer does not exist yet. In particular, it must not claim `保存成功`, remote submission, or any equivalent persistence guarantee.

## Stable bootstrap matrix

The accepted bootstrap matrix is intentionally conservative rather than latest-at-all-costs:

- Android Gradle Plugin: `8.13.2`;
- Gradle: `8.13` on CI;
- JDK: `17`;
- Kotlin Gradle plugin: `2.3.21`;
- Compose compiler plugin: `2.3.21` (kept equal to Kotlin);
- Compose BOM: `2026.06.00` (stable Compose UI/Foundation `1.11.4` line);
- Activity Compose: `1.13.0`;
- compile / target SDK: stable Android 16 / API `36`;
- SDK Build Tools: `35.0.0`;
- minimum SDK: `26` for the Spike only.

Why this matrix:

- Compose `1.12+` requires `compileSdk >= 37`, so it is outside this stable API 36 baseline.
- AGP `8.13.2` explicitly supports Kotlin 2.3 and API 36/36.1, while Kotlin `2.3.21` lists AGP 8.13 inside its fully supported range.
- AGP 9 built-in Kotlin was evaluated rather than assumed. It is not required for this bootstrap, and the previous `AGP 9.4.0 + Kotlin 2.4.20` candidate sat outside Kotlin's fully supported AGP range. Moving back to AGP 8.13.2 lets the project use the normal explicit `org.jetbrains.kotlin.android` plugin with a documented compatibility matrix instead of relying on an edge combination.
- API 37 is not installed or used by the CI baseline.

A future AGP 9 migration should be a separate dependency/toolchain change with its own exact-head CI evidence; it must not be smuggled into a feature PR.

`minSdk = 26` is a build-floor candidate for this architecture Spike, **not** a frozen production support policy. Product support range must be accepted separately with real compatibility evidence.

## Package boundary

The bootstrap keeps only boundaries that already protect something real:

```text
Android platform entry
  com.xueqing.app.MainActivity
          ↓
Compose presentation shell
  com.xueqing.app.presentation.*
          ↓
future application / shared domain contracts
          ↓
future Android infrastructure
```

Rules for follow-up work:

- `MainActivity` owns Android lifecycle/bootstrap only; it must not become a business state machine.
- Composables must not call Room, Supabase, backend APIs or other provider SDKs directly.
- Presentation code consumes application/domain contracts once those contracts exist.
- Android platform types must not leak into shared domain semantics.
- Do not create empty `application`, `domain` or `infrastructure` packages just to make the tree look layered; introduce them with the first real responsibility they protect.

## Required next slice

The next Android PR should be **Room + Draft Engine + process-death recovery**.

Its first proof is local durability, not cloud sync: while a teacher is recording text, Activity recreation or OS process death must not destroy the scoped local draft. Only after durable local draft recovery is proven should the Android line add queued/cloud submission behavior.

Later Phase 1/2 work still needs to validate:

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

The broader executable Android acceptance gate remains tracked in GitHub issue #12; PR #13 is only the bootstrap foundation described above.
