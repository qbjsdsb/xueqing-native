# Provider Adapter Conformance

## Goal

Provider SDK and wire defaults must not leak into product semantics. C# and Kotlin adapters expose Xueqing behavior through application contracts even when the underlying provider/library choices differ.

## Conformance is incremental, not a single bootstrap claim

The full provider boundary covers several capabilities that enter the product at different times. Do not block the first useful command slice on future Auth/Projection/Storage work, and do not claim those future capabilities are proven early.

### A. Command Adapter Conformance

Required when a domain command first crosses a provider boundary.

For `CreateObservation v1`, prove:

- View, ViewModel and domain/application contracts do not depend on Supabase/PostgREST SDK or HTTP response types;
- the adapter sends the frozen RPC name and parameters and never lets the client nominate `actor_app_user_id`;
- the caller supplies one stable `operation_id`; the adapter never generates a replacement id;
- retry/result-resolution preserves that original `operation_id`;
- frozen `P0001 + XQ_*` codes map to provider-neutral typed domain rejections;
- missing/invalid live authentication is distinguishable from a teaching-assignment rejection;
- timeout, connection failure and server-side unknown outcome remain `unknown`/retryable with the same operation id;
- a malformed or mismatched 2xx receipt is not accepted as success because the server may already have committed;
- unknown provider/protocol errors fail closed instead of being assigned invented business meaning.

### B. Session / Auth Conformance

Required when production-style login/session management enters a client:

- sign out current device/session only;
- explicit sign out/revoke all devices where supported;
- refresh/session-change handling;
- disabled/revoked old-token negative access;
- account/environment switch cleanup.

### C. Projection Adapter Conformance

Required when versioned read projections enter a client:

- custom API schema / projection RPC reads;
- contract-version and scope interpretation;
- provider errors normalized without deriving authorization from missing rows;
- account/environment switch cannot expose stale cross-scope projections.

### D. Storage Conformance

Required only when private attachment Storage enters product scope:

- private upload/download authorization;
- signed URL handling and short-lived exposure policy;
- provider-specific object/storage errors normalized behind application contracts.

## Rule

View, ViewModel and domain/application code never depend on provider SDK types. Adapter behavior is version-gated in CI before a provider/library upgrade is accepted.

Passing one capability layer does **not** close the broader provider-conformance gate. Each layer closes only when the corresponding product capability exists and has executable evidence.
