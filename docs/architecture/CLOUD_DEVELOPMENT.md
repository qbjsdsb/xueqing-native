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

## Trust boundaries

Development, staging and production use separate provider projects/issuers/secrets. Production deployment uses a protected GitHub Environment and explicit approval. Do not use `pull_request_target` to execute untrusted PR code with privileged credentials.

## Cloud lab matrix

- Ubuntu: contracts, Kotlin/JVM tests, C# non-UI tests where practical, local Supabase/Docker/pgTAP, provider-contract tests.
- Windows: WinUI compile, .NET tests, MSIX package, install/uninstall smoke; GUI automation only after a reliability Spike.
- Android/Linux emulator: lint/unit/build/instrumented critical journeys; release-candidate physical-device cloud testing may be added later.

## Forbidden

Do not claim a platform or release is verified because it worked only on one local machine. Do not require production credentials for ordinary pull-request testing.
