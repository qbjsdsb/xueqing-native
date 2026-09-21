# Production Provider Decision Matrix

Status: **operator topology selected — production evidence pending**

Checked: 2026-09-21.

This document turns current provider facts into Xueqing-specific production constraints and records the operator's selected V1 topology. The selection is not production acceptance by itself; PR #63 still requires provisioned-project/runtime/backup evidence and the fail-closed topology validator.

## Current hosted environment observation

A read-only inspection of the connected Supabase account found:

- an existing `xueqing-dev` project in `ap-southeast-1` (Singapore);
- its migration history belongs to the legacy Flutter-era Xueqing backend;
- its deployed Edge Function set does not contain the current native Invitation Delivery function;
- therefore it is **development/legacy evidence only** and MUST NOT be treated as the Xueqing Native production project or as production-region acceptance.

No current Xueqing Native production Supabase project is provisioned.

The connected Supabase organization is currently on the Free plan and read-only account inspection found **two active Free projects**. Current Supabase billing documentation grants two active Free projects and states that paused projects do not count toward that quota. Therefore a distinct zero-paid Xueqing Native production project currently has no free project slot.

This is an operations blocker, not permission to reuse the legacy project or pause another project automatically. Before production provisioning, one of the following must be explicitly completed:

- pause an existing project only after it is confirmed safe to pause; or
- use another Free organization with available capacity; or
- explicitly revisit the zero-paid constraint / paid plan.

Project refs, credentials and account secrets are intentionally not recorded here.

## Selected V1 topology

The operator has explicitly decided that V1 student/teacher data does **not** require mainland-China residency.

The selected first authoritative topology is:

- provider: **Supabase Hosted**;
- exact primary region: **Singapore `ap-southeast-1`**;
- PostgreSQL/Auth/Storage origin: Singapore project region;
- private Attachment delivery: global CDN/edge transit **accepted for V1**, while the bucket remains private and Xueqing authorization remains authoritative;
- Invitation Delivery execution: **`ap-southeast-1` required**, enforced by client regional invocation plus server-side `SB_REGION` fail-closed verification;
- global API/gateway transit: **accepted for V1**;
- strict single-region provider-log residency: **not required**, but intentional production PII/secret logging remains forbidden;
- backup: independent encrypted database + private object backup outside the live project remains mandatory.

The existing `xueqing-dev` project is not promoted to production. A distinct Xueqing Native production project must be provisioned and verified before this Gate can be accepted.

After this Gate, execution order is **Backup/Restore → CloudBase second-provider conformance → Signing/Recovery → V1 RC**. A second provider is portability/recovery evidence, never active-active authority.

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

Operator decision:

- Xueqing **accepts provider global CDN/edge delivery for V1** because strict single-jurisdiction byte residency is not a selected requirement;
- the Attachment bucket remains private and per-user authorization remains mandatory;
- CDN/cache location never becomes business authority and never weakens Teaching Fact / IdentityLink checks;
- public buckets are not introduced to improve cacheability;
- a future provider switch must document its own cache/delivery behavior rather than inheriting this acceptance automatically.

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

## Secondary provider target: Tencent CloudBase

CloudBase is retained as the planned **second-provider portability target**, not an accepted provider and not a prerequisite to the first production topology.

Current Tencent Cloud documentation now exposes a Singapore CloudBase region in which environments use PostgreSQL and support Cloud Functions, Cloud Storage, HTTP Gateway and Identity Authentication. This makes **CloudBase Singapore** a useful later conformance target because a Supabase Singapore → CloudBase Singapore rehearsal can test provider portability without simultaneously changing the intended geographic deployment region.

Implication for Xueqing:

- first production authority remains Supabase Hosted / `ap-southeast-1`;
- Backup/Restore executes **before** #65 so the second-provider spike can reuse a proved provider-portable database/object archive contract;
- **no compatibility is claimed yet**;
- #65 must still prove PostgreSQL transaction semantics, application-owned IdentityLink mapping, authorization/RLS-equivalent enforcement, command idempotency/locking, provider-neutral projections, private Attachment authorization, trusted Invitation Delivery, and export/restore behavior;
- existing Supabase conformance evidence is the semantic baseline, not proof that CloudBase passes;
- no active-active production adapters, live dual-write, client provider fallback, or timestamp merge is allowed.

A later cross-provider restore/cutover rehearsal may use the same Singapore target, but the authoritative provider changes only through an explicit migration/recovery operation.

References:

- https://cloud.tencent.com/document/product/876/51107
- https://cloud.tencent.com/document/product/876/127357
- https://cloud.tencent.com/document/product/876/121347
- https://cloud.tencent.com/document/product/876/46898
- https://cloud.tencent.com/document/product/876/127357
- https://cloudbase.cloud.tencent.com/blog/2026/07/27/singapore-region

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
