# Production Provider Decision Matrix

Status: **decision support only — not production acceptance**

Checked: 2026-09-21.

This document turns current provider facts into Xueqing-specific production constraints. It does not select a jurisdiction on behalf of the operator.

## Current hosted environment observation

A read-only inspection of the connected Supabase account found:

- an existing `xueqing-dev` project in `ap-southeast-1` (Singapore);
- its migration history belongs to the legacy Flutter-era Xueqing backend;
- its deployed Edge Function set does not contain the current native Invitation Delivery function;
- therefore it is **development/legacy evidence only** and MUST NOT be treated as the Xueqing Native production project or as production-region acceptance.

No current Xueqing Native production Supabase project is provisioned.

Project refs, credentials and account secrets are intentionally not recorded here.

## Exact hosted project regions

Current Supabase documentation exposes the following exact primary project regions:

| Region | Location | Edge Function regional invocation currently available |
|---|---|---|
| `us-west-1` | N. California | yes |
| `us-west-2` | Oregon | yes |
| `us-east-1` | N. Virginia | yes |
| `us-east-2` | Ohio | **no matching regional-invocation entry** |
| `ca-central-1` | Canada Central | yes |
| `eu-west-1` | Ireland | yes |
| `eu-west-2` | London | yes |
| `eu-west-3` | Paris | yes |
| `eu-central-1` | Frankfurt | yes |
| `eu-central-2` | Zurich | yes |
| `eu-north-1` | Stockholm | **no matching regional-invocation entry** |
| `ap-south-1` | Mumbai | yes |
| `ap-southeast-1` | Singapore | yes |
| `ap-northeast-1` | Tokyo | yes |
| `ap-northeast-2` | Seoul | yes |
| `ap-southeast-2` | Sydney | yes |
| `sa-east-1` | São Paulo | yes |

Xueqing V1 requires Invitation Delivery to execute in the selected primary region whenever the hosted provider exposes that region for functions.

For `us-east-2` and `eu-north-1`, current provider documentation does not expose a same-region Edge Function invocation target. Those regions therefore remain unsuitable for Xueqing's same-region Invitation Delivery policy unless the provider capability changes or the Delivery adapter moves to a different trusted compute surface.

Current official exact-region list contains **no mainland-China region**. If the selected policy requires primary hosted data to remain in mainland China, Supabase Hosted is blocked as the production provider candidate under the current region list. This is a provider-location fact, not a legal-compliance conclusion.

## Edge Function control

Supabase Edge Functions are globally deployed and normally run near the requester.

Current provider support allows an invocation to request a region with `x-region` (or `forceFunctionRegion`).

Xueqing adds both sides of the control:

1. Windows Infrastructure may send `x-region`;
2. the trusted Invitation Delivery function may be configured with `XUEQING_REQUIRED_EDGE_REGION`;
3. before bearer parsing, body parsing, database access or invitation lookup, the function compares `SB_REGION` to the required region;
4. mismatch fails closed with `XQ_INVITATION_DELIVERY_REGION_UNAVAILABLE`.

This makes function execution-region drift executable rather than documentary.

It does **not** prove that the provider's global API gateway itself is located only in the selected jurisdiction. Gateway transit remains a production-residency decision item.

## Private Attachment Storage

Current Supabase documentation states that Storage origin runs in the project's region, while Storage requests are served through a global CDN.

Private buckets retain per-user authorization, but "private" does not mean "origin-region only."

Smart CDN on eligible paid plans can cache private/signed responses at the edge. Token expiry does not necessarily purge an already-cached response immediately.

Supabase also exposes an S3-compatible endpoint and a direct storage hostname. Current public documentation is not sufficient evidence that using those interfaces gives Xueqing a hard no-cross-region-cache guarantee for end-user Attachment reads.

Therefore:

- Xueqing keeps the CDN/data-residency item blocked;
- cache-busting or a short browser TTL is not accepted as a residency control;
- a future accepted topology must either explicitly accept provider edge caching, prove an origin-only access path, or move private Attachment object delivery to a provider/topology with the required residency guarantee.

## Zero-paid production constraint

The project has a zero-paid-dependency goal.

Current Supabase Free plan documentation states:

- automatic backups are not included;
- Point-in-Time Recovery is not included;
- Log Drains are not included;
- API/database log retention is 1 day;
- low-activity Free projects may be paused.

Therefore **Supabase Hosted Free cannot be Xueqing's sole durability/recovery mechanism for production data**.

A zero-paid Xueqing production topology may still use a Free hosted runtime only if later gates independently prove:

- scheduled logical database backup outside the live project;
- independent private Storage object backup;
- encrypted backup handling;
- restore to a fresh environment;
- backup retention and integrity checks;
- an availability/health strategy compatible with Free-project pausing;
- no production PII or backup artifact is stored in public GitHub/CI.

Otherwise the production provider must change or the zero-paid constraint must be revisited explicitly.

## Secondary candidate: Tencent CloudBase

CloudBase is retained as a **candidate**, not an accepted provider.

Current Tencent Cloud documentation states:

- CloudBase primary supported region is Shanghai (`ap-shanghai`);
- a CloudBase environment can be created with PostgreSQL as its database type;
- Shanghai PostgreSQL environments can also use Cloud Functions, Cloud Storage, HTTP Gateway and Identity Authentication;
- the Free Experience tier currently includes PostgreSQL but does not include database rollback;
- Free Experience Cloud Functions have a fixed 3-second timeout;
- CloudBase Storage integrates CDN by default, so "Shanghai bucket" is not by itself proof that every Attachment response remains only in Shanghai.

Implication for Xueqing:

- if production policy requires primary data to remain in mainland China, CloudBase Shanghai is a materially more plausible hosted candidate than current Supabase Hosted region availability;
- **no compatibility is claimed yet**;
- before selection, a dedicated provider-conformance spike must prove PostgreSQL semantics required by Xueqing, application-owned IdentityLink mapping, authorization/RLS-equivalent enforcement, command idempotency/locking, private Attachment authorization, provider-neutral projections, and backup/restore behavior;
- existing Supabase conformance evidence remains useful as a semantic contract but cannot be copied as proof for CloudBase;
- the zero-paid constraint still requires an independent backup/restore strategy because the current free experience does not include data rollback.

CloudBase conformance is now a planned second-provider portability milestone after this residency Gate, regardless of which provider is selected for the first production topology.

That sequencing has two different meanings:

- if the selected production policy requires mainland-China residency, CloudBase conformance becomes a prerequisite to accepting CloudBase as the production topology and therefore materially blocks that production path;
- if Supabase satisfies the selected first production topology, PR #63 may close on Supabase evidence first, and the queued CloudBase conformance spike still runs next as a portability milestone before Backup/Restore.

Do not start the implementation in parallel with this Gate, and do not maintain active-active production adapters or dual-write live traffic.

References:

- https://cloud.tencent.com/document/product/876/51107
- https://cloud.tencent.com/document/product/876/127357
- https://cloud.tencent.com/document/product/876/121347
- https://cloud.tencent.com/document/product/876/46898

## References

- https://supabase.com/docs/guides/platform/regions
- https://supabase.com/docs/guides/functions/regional-invocation
- https://supabase.com/docs/guides/functions
- https://supabase.com/docs/guides/storage/cdn/fundamentals
- https://supabase.com/docs/guides/storage/cdn/smart-cdn
- https://supabase.com/docs/guides/storage/s3/authentication
- https://supabase.com/docs/guides/platform/backups
- https://supabase.com/docs/guides/observability/log-drains
- https://supabase.com/docs/guides/platform/free-project-pausing
- https://supabase.com/docs/guides/deployment/going-into-prod
