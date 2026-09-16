# ADR-0009 — High-risk governance/lifecycle commands are online-only initially

**Status: Accepted**

## Decision

Initial offline queues focus on capture/drafts and explicitly safe low-risk append facts. Commands whose correctness depends on fresh authority/current relationships require online server execution.

Initial online-only set includes formal Case lifecycle transitions, membership/credential lifecycle, responsibility handoff/reassignment, Student merge and similar governance operations.

The set may change only through a command-specific protocol/ADR proving safe offline semantics.