# ADR-0004 — Local-first Command Sync

**Status: Superseded by ADR-0013**

## Original decision

Both native clients use local databases for responsive reads, protected drafts, queued commands and cached projections. Formal business state remains server-authoritative. Queued writes use stable operation identity and conflicts are not resolved by Last Write Wins.

The original text required a server-issued pull cursor/equivalent for V1. Further research found that requirement premature for the initial product scale and authorization model. ADR-0013 retains Local-first Command Sync while replacing mandatory V1 cursor sync with scoped Projection Snapshots.
