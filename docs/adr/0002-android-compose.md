# ADR-0002 — Android client uses Kotlin and Jetpack Compose

**Status: Accepted**

## Decision

Android uses Kotlin, Jetpack Compose/Material 3, ViewModel, Coroutines/Flow, Room and WorkManager.

## Why

The Android product is a fast classroom capture tool. Compose and the Android Architecture Components provide the current native path for state-driven UI, local data and resilient background work.

## Constraints

- Android is optimized for Capture, not UI parity with Windows;
- Room/local store becomes the normal UI read source once Local-first implementation begins;
- critical flows receive benchmark/accessibility coverage early.