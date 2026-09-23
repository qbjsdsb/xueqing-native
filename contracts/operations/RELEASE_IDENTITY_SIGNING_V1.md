# Release Identity + Signing v1

Status: **active Phase 1 contract**

Tracker: **#81**

## Purpose

Freeze the V1 release identity and the boundaries around signing, recovery and durable distribution without placing private signing material in Git.

## Single version source

`release/version.properties` is the checked-in release identity source for Android and Windows.

The release version is a product label. Platform-native monotonic versions are separate:

- Android `versionCode` is a monotonically increasing release sequence; it is not parsed from SemVer.
- Android `versionName` is the human-facing release label.
- Windows package version is the four-part numeric MSIX version and must strictly increase across installable release artifacts.
- Windows display/release label may remain `1.0.0-rc.1` while the MSIX package version is `1.0.0.1`.

Never infer one platform version from the other at runtime.

## Frozen application/package names

V1 freezes:

- Android application id: `com.xueqing.app`;
- Windows production package name: `Xueqing.Native`;
- Windows architecture: `x64`.

Changing either application/package name after public release is a migration, not a routine version bump.

## Windows Publisher

The production Windows Publisher must exactly match the trusted signing certificate subject.

Until a production signing identity is provisioned, the Publisher is intentionally **not hard-coded** into the development manifest.

`release/windows/Package.production.appxmanifest.template` uses `__XUEQING_WINDOWS_PUBLISHER__` and must be rendered only by protected release tooling.

This preserves the accepted development package identity while preventing a guessed production Publisher from becoming permanent.

## Android signing

The V1 Android release key:

- is created outside this repository;
- is never committed, printed or uploaded as an ordinary CI artifact;
- is stored for CI only in the protected `production-release` environment;
- has an independent offline recovery copy;
- records SHA-256 certificate fingerprint in release evidence;
- must sign both the clean-install candidate and every supported in-place upgrade.

The first final production APK must not be signed with the ordinary debug key.

## Windows signing

Production direct distribution requires a trusted signing identity.

Preferred decision order:

1. SignPath Foundation if Xueqing satisfies current open-source eligibility and the signing project is approved;
2. another publicly trusted production signing provider/certificate;
3. self-signed certificate only for test/RC distribution, never represented as the final production trust identity.

The signed certificate Subject must match the rendered MSIX Publisher exactly.

The thin `XueqingSetup.exe` is also Authenticode-signed for final public distribution. It installs the same signed MSIX and does not replace Package Identity.

## Windows Setup invariant

V1 Setup is a single self-contained EXE containing the exact already-signed production MSIX as an embedded resource.

Before installation it:

- verifies the embedded MSIX SHA-256 that was bound when Setup was built;
- verifies Windows Authenticode trust with `WinVerifyTrust`;
- installs through the current-user Windows package deployment API;
- confirms the resulting `Xueqing.Native` Package Name and Publisher;
- launches the existing `xueqing://today` protocol after success.

Setup contains no provider credential, no business/domain storage, no second updater authority and no administrator-elevation requirement. The Setup executable and embedded MSIX use the same trusted production signing certificate.

## Secret boundary

Forbidden in Git, PR logs and ordinary artifacts:

- Android keystore bytes/passwords/key password;
- Windows PFX/private key/password;
- SignPath API token;
- recovery archive password;
- provider service-role/admin credentials.

Release jobs fail closed when required signing material is unavailable.

## Recovery

Before V1 final acceptance record, outside Git:

- primary signing credential owner;
- offline recovery-copy location/owner;
- recovery verification date;
- certificate/key fingerprints;
- procedure to replace CI secret without changing application identity.

No release is accepted if losing a CI secret would permanently strand existing installations.

## Provenance

Every release artifact records:

- exact source commit;
- release label;
- platform-native version;
- package/application identity;
- signing fingerprint/identity;
- SHA-256;
- workflow/run identity.

GitHub Release is the durable distribution record. Actions artifacts are temporary evidence only.

The protected signing workflow records the signed APK, signed MSIX and signed Setup as a **draft prerelease** tied to the exact source SHA. Final #67 acceptance promotes those exact bytes; it must not rebuild equivalent-looking binaries from another commit.

## Manual acceptance account

A persistent fictional manual-acceptance account may exist in the production acceptance environment, but:

- its password is never stored in Git;
- it contains fictional data only;
- its Auth identity resolves through normal IdentityLink;
- it receives no service/admin privilege;
- it can be deleted/rebuilt independently of release signing;
- account bootstrap is an operator action protected by the existing production acceptance boundary.

Direct SQL mutation of Supabase `auth.users` is explicitly forbidden.
