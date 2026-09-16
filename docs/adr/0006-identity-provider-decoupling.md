# ADR-0006 — Business identity is decoupled from Auth provider identity

**Status: Accepted**

## Decision

Use application-owned stable `AppUser` IDs. External authentication identity is linked through provider key + issuer/tenant + opaque external subject.

Business teaching facts never use provider auth PK/email as their durable identity foreign key.

## Why

This preserves teaching/audit history through provider changes and avoids forcing a provider-specific ID type through the whole schema.