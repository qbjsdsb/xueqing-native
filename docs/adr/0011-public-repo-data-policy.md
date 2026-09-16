# ADR-0011 — Public repository, fictional data only

**Status: Accepted**

## Decision

The repository remains suitable for public open-source development. Git, CI logs and shared fixtures contain only fictional/irreversibly anonymized data and no secrets.

Forbidden: real student/teacher/guardian information, production exports/attachments, credentials/tokens/service keys/database passwords, signing private keys/certificates or diagnostic bundles containing sensitive teaching content.

CI and fixtures should make the safe path easy rather than relying only on contributor memory.