# ADR-0007 — Sensitive cached access requires a finite Offline Access Lease

**Status: Accepted (duration pending Spike)**

## Decision

Local-first does not grant indefinite offline access to cached student information. Sensitive cached access must be tied to a finite lease issued/renewed after successful online authorization validation.

The final lease duration and exact cryptographic/metadata representation remain a security-Spike decision.

## Consequences

After lease expiry the client may preserve encrypted unsynced input according to policy, but must not continue exposing the full cached student workspace indefinitely. Scope reduction detected online triggers purge/lockout of no-longer-authorized data.