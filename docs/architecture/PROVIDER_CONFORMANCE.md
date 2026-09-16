# Provider Adapter Conformance

## Goal

Provider SDK defaults must not leak into product semantics. C# and Kotlin adapters expose the same Xueqing behavior even when underlying SDK defaults differ.

## Minimum contract tests

Both adapters must prove, against the same fictional backend fixture where possible:

- sign out current device/session only;
- explicit sign out/revoke all devices where supported;
- refresh/session-change handling;
- disabled/revoked old-token negative access;
- custom API schema projection reads and command RPCs;
- timeout/unknown-result preservation of `operation_id`;
- stable domain error mapping (provider exception strings are not product contracts);
- private Storage upload/download authorization;
- signed URL handling and short-lived exposure policy;
- account/environment switch cleanup.

## Rule

View, ViewModel and domain code never depend on provider SDK types. Adapter behavior is version-gated in CI before provider/library upgrades are accepted.
