# Contributing to Xueqing Native

Xueqing Native is currently in Phase 0. Contributions should strengthen the product and architecture foundation rather than add speculative features.

## Before changing code

1. Read `README.md` and `AGENTS.md`.
2. Check relevant ADRs under `docs/adr/`.
3. For domain behavior, update the contract/document first.
4. Keep Android, Windows, and backend semantics aligned even when their UI differs.

## Pull request scope

Prefer one auditable outcome per PR. Avoid mixing architecture decisions, UI redesigns, backend migrations, and unrelated cleanup.

## Data safety

Only fictional or irreversibly anonymized development data may be committed. Never commit student/teacher/guardian personal data, tokens, passwords, service-role keys, database passwords, production exports, private attachments, or signing credentials.

## External code

Open-source repositories are references, not copy sources. Before copying non-trivial code, verify its license is compatible with Apache-2.0 and document the source. Prefer re-implementing the idea to importing unnecessary complexity.

## Definition of done

A change is not done because the UI appears to work. Relevant domain tests, database authorization tests, sync/retry behavior, failure states, and platform-specific accessibility/performance gates must pass for the feature being changed.
