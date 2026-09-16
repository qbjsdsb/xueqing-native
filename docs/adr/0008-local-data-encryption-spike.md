# ADR-0008 — Local sensitive-data encryption is a pre-production Spike gate

**Status: Accepted**

## Decision

Do not freeze a database-encryption library by intuition. Before real data, compare maintained Android and Windows approaches for whole-DB/field encryption and OS-backed key storage.

Windows Spike compares practical SQLCipher/SQLite3MC-style options and/or minimal SQLite + Windows cryptography. Android evaluates maintained Room-compatible encryption and Keystore-backed keys.

## Gate

No real student data is authorized until cache scope, encryption, key lifecycle, migration/recovery, attachment TTL and purge behavior are documented and tested.