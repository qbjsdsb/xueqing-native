# Sync Contracts

Xueqing uses Local-first Command Sync, not a general CRDT.

The sync contract will define Outbox envelopes, operation-result lookup, server change cursor, authorization/scope version, conflict/error envelopes, purge instructions and retry classes.

Never use wall-clock `updated_at` alone as a causal ordering mechanism.