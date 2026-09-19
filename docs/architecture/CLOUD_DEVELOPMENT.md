# Cloud-first Development

## Truth

Xueqing must be developable, buildable, testable and releasable without a powerful local computer. GitHub is the source/build/CI/release evidence hub.

## Required workflow

- ordinary work uses short branches + Draft/normal PRs;
- standard public GitHub-hosted runners are preferred; no paid larger runner is a baseline requirement;
- ordinary PRs receive no production secrets;
- heavy/manual workflows expose `workflow_dispatch` entry points;
- workflow concurrency should cancel obsolete runs for the same branch/PR;
- temporary PR artifacts use short retention; durable binaries belong in Releases;
- a green result is valid only for its exact commit SHA.

## Affected-gate routing

Use the lowest-cost CI gate that can actually prove the changed boundary, without weakening the exact-head acceptance rule.

- repository/docs-only changes should not start platform packaging, provider or device integration merely because durable project state changed;
- presentation-only changes should run the relevant platform build/unit/lint and UI/device smoke, but should not start database/provider E2E solely because a View, Composable or ViewModel moved;
- provider, projection/command adapter, backend or shared-contract changes must run the applicable provider/database conformance evidence;
- Durable Intent, local encryption, migration or process-recovery changes must run their durability/device gates;
- packaging, signing, LocalState upgrade or installer changes must run the platform packaging/upgrade gates;
- release candidates may deliberately run a broader matrix through explicit/manual workflows even when path routing would skip unrelated PR gates.

A skipped unrelated gate is not inherited evidence for a changed boundary. A green result still applies only to the exact SHA and the gates relevant to that SHA.

## Trust boundaries

Development, staging and production use separate provider projects/issuers/secrets. Production deployment uses a protected GitHub Environment and explicit approval. Do not use `pull_request_target` to execute untrusted PR code with privileged credentials.

## Cloud lab matrix

- Ubuntu: contracts, Kotlin/JVM tests, C# non-UI tests where practical, local Supabase/Docker/pgTAP, provider-contract tests.
- Windows: WinUI compile, .NET tests, MSIX package, install/uninstall smoke; GUI automation only after a reliability Spike.
- Android/Linux emulator: lint/unit/build/instrumented critical journeys; release-candidate physical-device cloud testing may be added later.

## Forbidden

Do not claim a platform or release is verified because it worked only on one local machine. Do not require production credentials for ordinary pull-request testing.
