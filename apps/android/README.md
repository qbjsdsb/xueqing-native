# Android Architecture Spike

Target: Kotlin + Jetpack Compose + Material 3 + ViewModel + Coroutines/Flow + Room + WorkManager.

Phase 1/2 spike must validate:

- cold start and baseline-profile/macrobenchmark setup;
- Today / Student / Quick Capture interaction prototypes;
- Room as local UI source of truth;
- queued Outbox writes and WorkManager retry;
- offline → process death → restart → resume sync;
- photo/draft handling with fictional images only;
- account switch and local-scope cleanup;
- encrypted local storage strategy;
- one-handed usability, text scaling, dark mode and accessibility.

Android is optimized for Capture, not for reproducing the Windows information architecture.