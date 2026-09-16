# Threat Model — Phase 0 Baseline

Xueqing handles sensitive educational records. This document defines attack paths to test; it is not a claim of completed production security.

## Threats and required defenses

- lost/stolen device → minimal cache, encryption Spike, finite lease, OS backup exclusion, lock/purge policy;
- authenticated but unauthorized teacher → server RLS/assignment/scope checks on every projection/command path;
- disabled member/revoked old session → negative tests for projection, command and Storage paths;
- forged/stale command → server authority, expected versions/current relations, stable operation identity and atomic transaction;
- sync authorization bypass → sync API never trusts client-supplied organization/student identity as authority;
- malicious attachment path/name → server-controlled/random object identity; no student names or raw client path traversal;
- leaked signed URL → private buckets, short TTL and no logging/caching as durable authorization;
- export formula injection → sanitize/escape untrusted cell content before CSV/XLSX interpretation;
- logs/diagnostics → redact names/content/tokens/signed URLs/paths; structured technical metadata only;
- system/device backup → sensitive cache/intent excluded from automatic cloud/device migration unless explicitly designed and encrypted;
- obsolete vulnerable client → minimum-supported/security-block client policy before production;
- CI/supply chain → least-privilege token, no production secrets on untrusted PR, pinned Actions, dependency review/scanning where available;
- production-secret exposure → separate protected environments and rotation/revocation procedure.

## Acceptance style

Each threat must map to a server/client/storage/CI control and an automated or documented manual test before production real-data approval.
