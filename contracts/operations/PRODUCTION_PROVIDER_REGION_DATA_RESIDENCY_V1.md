# Production Provider Region / Data Residency v1

## Purpose

This gate prevents Xueqing from treating a development provider configuration as a production data-residency decision.

No real student, teacher, parent, attachment or production credential may be introduced until this gate is accepted with evidence.

The contract is provider-neutral. Supabase Hosted is the current development/reference-provider candidate, not an automatically accepted production provider.

## Required distinction

A production topology must separately identify and evidence:

1. **authoritative at-rest data**
   - PostgreSQL business tables;
   - Auth identity/session state;
   - Storage metadata;
   - private Attachment object origin;
2. **transient processing**
   - API gateway;
   - Auth service;
   - Storage service;
   - Edge Functions / invitation delivery;
3. **derived or cached copies**
   - CDN / edge caches;
   - observability / logs;
   - diagnostics exports;
4. **recovery copies**
   - database backups / PITR;
   - independent private object backup required by the later Backup/Restore gate.

"Project region" by itself is not sufficient evidence for all four categories.

## Production region selection

The intended legal/business data jurisdiction must be selected explicitly before provider provisioning.

For a hosted provider that offers grouped/general regions and exact regions:

- Xueqing production MUST use an exact/specific region when jurisdiction matters;
- a provider's broad "Europe", "APAC", "Americas" or similar grouping is not accepted as jurisdiction evidence;
- the checked-in evidence records only the public region identifier and jurisdiction rationale, never project secrets;
- changing the region later is treated as a migration/recovery event, not an ordinary setting toggle.

The repository must not infer a jurisdiction from a developer's current location, a CI runner, or the reference-provider local environment.

## PostgreSQL + Auth

Xueqing business state remains PostgreSQL-first.

The production evidence must prove:

- exact authoritative database region;
- project/provider identifier stored only in non-secret deployment configuration;
- Auth identity state location;
- that application-owned AppUser + IdentityLink remains the business identity layer;
- no production service/admin credential reaches Android, Windows, public GitHub, CI artifacts or screenshots.

If the candidate provider stores Auth in the project's Postgres database, that relationship may be used as evidence only when backed by current provider documentation and the provisioned project's exact region.

## Private Attachment Storage

The Attachment bucket remains private and authorization remains application-owned / Teaching Fact based.

Production evidence must separately record:

- object origin region;
- metadata region;
- whether authenticated/private responses can be cached outside the origin jurisdiction;
- whether that caching is disabled, bypassed, contractually accepted, or makes the provider unsuitable for the selected residency requirement;
- whether signed URLs are used.

V1 Xueqing does not rely on public buckets.

A private bucket is an access-control property, not proof that bytes never leave the origin region.

## Edge / invitation delivery

Organization Invitation Delivery is a trusted-server side effect, not a domain source of truth.

Production evidence must state:

- where the Edge/server function executes by default;
- whether execution is region-pinned;
- whether region pinning is client-controlled or server-enforced;
- which personal data enters the function;
- which external email/Auth provider receives recipient data;
- whether transient processing outside the selected jurisdiction is accepted.

If the provider globally distributes functions or routes to the nearest edge by default, the topology MUST NOT claim same-region processing without an explicit control/evidence.

Client-supplied region hints are not by themselves a server-enforced residency guarantee.

## Logs / diagnostics

Production logs are a separate data surface.

Rules:

- application code MUST NOT intentionally log student free text, attachment bytes, invitee email, JWTs, refresh tokens, service/secret keys or database credentials;
- provider-managed logs and retention must be documented;
- every configured log drain is a cross-system data flow and must record destination + region;
- CI diagnostics remain fictional and secret-scrubbed;
- production diagnostics must not be uploaded to public GitHub Actions artifacts.

Unknown provider-log residency is a blocker when the selected requirement applies to logs.

## Backup dependency

This gate records backup residency and scope, but does not perform the restore drill.

The later Backup/Storage Restore gate must not assume a database backup contains private Storage object bytes.

The production topology must identify:

- database backup mechanism and retention class;
- known backup/restore region behavior;
- whether Storage objects are included;
- independent object-backup requirement.

## Evidence manifest

Repository evidence lives at:

`docs/production/PROVIDER_TOPOLOGY.json`

It contains no secret project ref if the ref is considered sensitive by deployment policy, no API keys, no database host credentials, no user identifiers and no production data.

The manifest has two states:

- `blocked`: topology research is valid but one or more production facts/decisions are unresolved;
- `accepted`: every blocker is cleared and evidence is sufficient for real-data readiness to proceed to Backup/Restore.

The command:

```text
python3 tools/production/verify_provider_topology.py \
  docs/production/PROVIDER_TOPOLOGY.json \
  --require-accepted
```

MUST succeed before this Gate is marked completed in `PROJECT_STATE.yaml`.

## Current Supabase Hosted candidate findings — 2026-09-21

Current official provider documentation states:

- each project has one primary region and the selected region determines where primary project data is stored;
- broad/general region selection is not jurisdiction proof;
- Supabase Auth stores its user information in the project's Postgres `auth` schema;
- the Storage origin runs in the project region, while Storage uses a global CDN;
- private Storage responses can participate in CDN caching;
- Edge Functions are globally distributed and normally execute near the requester, with per-invocation regional selection available;
- database backups do not include Storage object bytes.

These statements are provider-candidate evidence only. They do not close the production gate.

References:

- https://supabase.com/docs/guides/platform/regions
- https://supabase.com/docs/guides/auth
- https://supabase.com/docs/guides/auth/architecture
- https://supabase.com/docs/guides/storage/cdn/fundamentals
- https://supabase.com/docs/guides/storage/cdn/smart-cdn
- https://supabase.com/docs/guides/functions/regional-invocation
- https://supabase.com/docs/guides/platform/backups
- https://supabase.com/docs/guides/observability/log-drains

## Acceptance

This gate is accepted only when all of the following are true:

- intended jurisdiction is explicitly selected;
- exact production primary region is provisioned and independently recorded;
- PostgreSQL/Auth at-rest region is evidenced;
- private Attachment object-origin region is evidenced;
- global/private CDN behavior is explicitly accepted or eliminated for the selected residency policy;
- Edge/invitation transient processing policy is explicit and evidenced;
- logs/diagnostics residency and drain policy is explicit;
- database backup residency/scope evidence exists;
- independent Storage object backup requirement is carried into the next gate;
- production clients contain publishable/public configuration only;
- no production PII/secrets are present in repository or CI evidence;
- `verify_provider_topology.py --require-accepted` succeeds.

## Non-goals

- no production cutover;
- no real student data;
- no provider marketing-based compliance claim;
- no Backup/Restore drill here;
- no release signing/key recovery here;
- no generic multi-cloud abstraction.
