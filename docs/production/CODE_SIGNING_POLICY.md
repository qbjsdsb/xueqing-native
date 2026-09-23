# Code signing policy

Xueqing Native is an Apache-2.0 open-source project maintained in the public repository `qbjsdsb/xueqing-native`.

For Windows public distribution, the preferred trusted signing route is SignPath Foundation when the project is accepted.

**Free code signing provided by SignPath.io, certificate by SignPath Foundation.**

## Signing scope

The signing policy applies only to release artifacts built from this repository:

- `Xueqing-<version>-x64.msix`;
- `XueqingSetup-<version>-x64.exe`.

Android uses a separate project-owned Android release signing identity and is not signed by SignPath.

No third-party executable is re-signed as if it were authored by Xueqing Native.

## Trusted build origin

A signing request must originate from the checked-in GitHub Actions release workflow.

The release workflow:

1. accepts an exact 40-character source commit;
2. requires that commit to be on protected `main` history;
3. builds on GitHub-hosted runners;
4. uploads the unsigned artifact to GitHub Actions before SignPath submission;
5. submits the GitHub artifact through the official SignPath GitHub integration;
6. waits for explicit signing approval when required;
7. verifies the returned signature, Publisher and SHA-256;
8. records exact source commit, workflow identity and artifact hashes in release evidence;
9. creates a draft GitHub prerelease from those exact signed bytes.

Signed artifacts must not be rebuilt from a different commit during final RC promotion.

## Project roles

Until additional maintainers are appointed:

- **Committer / maintainer:** GitHub user `qbjsdsb`;
- **Reviewer:** GitHub user `qbjsdsb`;
- **Signing approver:** GitHub user `qbjsdsb`.

If the maintainer set changes, this policy must be updated before the new person can approve a production signing request.

Repository and signing-provider accounts used by maintainers/approvers must use multi-factor authentication.

## Review and approval

Normal source changes enter `main` through pull requests and repository rules.

Production signing is a separate protected operator action:

- pull-request CI never receives production signing secrets;
- production signing uses the protected GitHub Environment `production-release`;
- signing is performed only after the release infrastructure and relevant exact-head gates are green;
- a signing request must be explicitly approved by an authorized signing approver;
- failures in source provenance, signature verification, Publisher matching or release evidence fail closed.

## Key custody and recovery

Windows private signing keys are never committed to Git.

When SignPath Foundation signing is used, the certificate private key is held by the signing service rather than stored in this repository or ordinary CI artifacts.

Android signing material and any non-SignPath Windows fallback signing credential are restricted to the protected release environment and must have an independently verified encrypted offline recovery copy.

Recovery ownership and procedures are documented in `docs/production/SIGNING_RECOVERY.md`; secret values are deliberately excluded from Git.

## Privacy and network behavior

Xueqing is a teacher workflow application and may transmit user-entered teaching data to the Xueqing backend selected by the deployment profile when the user signs in, syncs, uploads an attachment, sends an invitation or performs another explicit network-backed product action.

The accepted first production topology uses the Xueqing-controlled Supabase deployment in Singapore. The application does not send teaching content to SignPath; SignPath is used only for Windows release-artifact signing.

Privacy-safe diagnostic archives are created only by an explicit local user action. Their schema is allowlist-first and excludes student names, teaching free text, attachment bytes, passwords, access/refresh tokens, service/admin credentials and unrestricted logs.

Xueqing does not add a second telemetry channel as part of code signing.

## Installation and system changes

The Windows user-facing installer is a thin, signed `XueqingSetup.exe`.

It:

- embeds the exact already-signed production MSIX;
- verifies the embedded package hash and Windows trust before installation;
- installs the current-user `Xueqing.Native` package through Windows package deployment;
- does not create a second business-data store or updater authority;
- does not require administrator elevation as part of the accepted V1 path.

Users can uninstall Xueqing through the normal Windows installed-app management surface.

## Incident response

If a signing credential, signing account or published artifact is suspected to be compromised:

1. stop new signing and release promotion;
2. preserve the affected source SHA, workflow evidence, signatures and hashes;
3. revoke/rotate the affected signing credential or provider access as appropriate;
4. update protected GitHub secrets/variables without changing the established application identity unless an explicit migration is approved;
5. publish a security/recovery release only after exact-source verification and the normal release gates pass.

No maintainer should work around SignPath or repository signing controls to force a release.
