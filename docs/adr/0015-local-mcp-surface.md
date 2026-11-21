# ADR-0015 — Local Xueqing MCP surface (`Xueqing.Mcp`) over stdio

**Status: Proposed**

## Decision

Provide an official local Model Context Protocol (MCP) server (`apps/mcp/Xueqing.Mcp`) built on `.NET 10` and the official `ModelContextProtocol` C# SDK over `stdio` transport, sharing the exact `net10.0` Core, Infrastructure, encrypted local draft storage, and OS keyring adapters (`WindowsDpapi*` / `LinuxSecretService*`) used by the desktop clients.

## Why

- Complements `WINDOWS_AGENT_AUTOMATION_V1.md` (UIA + keyboard + `xueqing://` deep links) with a structured, cross-platform (Windows + Linux), low-token interface for AI assistants.
- Keeps AI workflows outside the desktop UI chrome (honoring `PRODUCT.md`: Xueqing is not an AI chatbot) while letting teachers use their preferred AI client to review teaching context and draft classroom observations.

## Security & Authority Constraints

- **No elevated credentials**: Never hold `service_role`, direct SQL credentials, or bypass RLS/Teaching Fact Gate. Production mode reuses the local user's encrypted Refresh Token session via `ProviderAuthCoordinator` and `Postgrest*` adapters.
- **V1 boundary is Read + Encrypted Local Draft + Deep Link only**: AI may read authorized projections and write/update local encrypted observation drafts (`SqliteObservationDraftStore` with `expected_epoch` optimistic concurrency), then hand off a validated `xueqing://` deep link so the responsible teacher reviews and submits in the native app.
- **No silent consequential writes**: Submitting observations, creating/transitioning/closing/reopening Learning Cases, rescheduling/verifying actions, or modifying organization membership remain explicit human-confirmed actions.
- **Stderr-only logging & zero secret leakage**: `stdout` carries MCP JSON-RPC exclusively; tool outputs never include tokens, credentials, or raw key material.
