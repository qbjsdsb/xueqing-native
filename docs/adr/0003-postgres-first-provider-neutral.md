# ADR-0003 — PostgreSQL-first, provider-neutral backend

**Status: Accepted**

## Decision

Domain/schema/authorization semantics are PostgreSQL-first and must not encode a cloud vendor as a business concept. Supabase is the default development/reference provider, not an unconditional permanent production lock-in.

## Consequences

- provider Auth subject is not the application user primary key;
- provider SDKs stay in infrastructure adapters;
- Git migrations and PostgreSQL tests express durable backend truth;
- production provider/region freezes only after real compatibility/network/session/Storage/restore gates.