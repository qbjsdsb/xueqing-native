# Permission Contracts

Permission contracts describe capabilities and responsibility semantics without coupling them to a provider SDK.

Never collapse these concepts:

- authenticated actor;
- organization supervisor;
- responsible teacher;
- student/subject assignment;
- visibility projection.

Server policy is authoritative; client capability projections exist for UX only.

## Provider Session/Auth conformance v1

Provider authentication proves possession of a valid external session and supplies an external subject. It does **not** freeze Xueqing business authority for the lifetime of that provider token.

For every authenticated business projection or command, the server must resolve the provider subject to the application-owned `AppUser` and re-evaluate the live authority required by that operation. At minimum, the current Observation slice enforces:

- the external subject resolves to an existing application-owned `AppUser`;
- the `AppUser` is enabled;
- scoped teaching access uses a live active membership with teaching capability;
- teaching facts require the live active Student / Subject Profile / Teacher Assignment required by the command;
- UI state, a previous PersonalBootstrap snapshot, and provider token age are never authorization proof.

An already-issued provider access token may remain cryptographically valid after Xueqing authority changes. That token — and a provider-refreshed replacement for the same external identity — must still fail closed immediately when the application-owned actor is disabled. Membership or assignment revocation must likewise remove the affected teaching context and reject scoped reads/writes without waiting for JWT expiry.

Provider logout/revocation mechanics remain an infrastructure/session concern. Xueqing correctness must not depend on a provider promising instantaneous invalidation of every previously issued access token.

Executable reference-provider evidence lives in `backend/supabase/tests/provider_session_auth_conformance.sh`. It uses only fictional local data and verifies that rejected stale-authority commands create neither an Observation nor an operation receipt.