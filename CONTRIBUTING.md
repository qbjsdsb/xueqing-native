# Contributing to Xueqing Native

The current phase, milestone, and next execution line are defined only in `docs/project/PROJECT_STATE.yaml`. Do not copy a phase number into this document; dynamic project state must have one durable source of truth.

## Before changing code

1. Read `README.md` and `AGENTS.md`.
2. Read `docs/project/PROJECT_STATE.yaml` for the current milestone and execution line.
3. Check relevant ADRs and `docs/architecture/EVOLVABILITY_BOUNDARIES.md`.
4. For domain behavior, update the contract/document first.
5. Keep Android, Windows, and backend semantics aligned even when their UI differs.

## Pull request scope

Prefer one auditable outcome per PR. Avoid mixing architecture decisions, UI redesigns, backend migrations, and unrelated cleanup.

## Data safety

Only fictional or irreversibly anonymized development data may be committed. Never commit student/teacher/guardian personal data, tokens, passwords, service-role keys, database passwords, production exports, private attachments, or signing credentials.

## External code

Open-source repositories are references, not copy sources. Before copying non-trivial code, verify its license is compatible with Apache-2.0 and document the source. Prefer re-implementing the idea to importing unnecessary complexity.

## Definition of done

A change is not done because the UI appears to work. Relevant domain tests, database authorization tests, sync/retry behavior, failure states, and platform-specific accessibility/performance gates must pass for the feature being changed.
