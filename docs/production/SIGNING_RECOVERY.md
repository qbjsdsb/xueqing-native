# Signing + Recovery Operations

Status: **Phase 1 / #81**

This document records the repository-visible portion of the V1 signing and recovery procedure. Private key material and passwords are intentionally not recorded here.

## Protected GitHub environment

Production signing jobs use GitHub Environment `production-release`.

Recommended protection:

- only protected `main` / release tags may deploy;
- require an explicit reviewer before secrets are released;
- do not allow ordinary pull-request jobs to reference production signing material;
- use GitHub-hosted runners for the signing workflow unless a separately audited signing provider requires otherwise.

## Android secret names

- `XUEQING_ANDROID_KEYSTORE_B64`
- `XUEQING_ANDROID_KEYSTORE_PASSWORD`
- `XUEQING_ANDROID_KEY_ALIAS`
- `XUEQING_ANDROID_KEY_PASSWORD`

The keystore must have an independent encrypted offline recovery copy. Record the certificate SHA-256 fingerprint after the first protected signing run.

## Windows signing modes

Environment variable `XUEQING_WINDOWS_SIGNING_MODE` must be one of:

- `signpath` — preferred when the SignPath open-source project is approved;
- `pfx` — trusted CA-issued production code-signing certificate stored as protected CI material.

Common variable:

- `XUEQING_WINDOWS_PUBLISHER` — exact certificate Subject; it is rendered into the MSIX manifest and must match the final signature.

SignPath configuration:

- secret `SIGNPATH_API_TOKEN`;
- variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG`, `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`;
- variable `SIGNPATH_SETUP_ARTIFACT_CONFIGURATION_SLUG` for the user-facing Setup EXE.

PFX configuration:

- secret `XUEQING_WINDOWS_PFX_B64`;
- secret `XUEQING_WINDOWS_PFX_PASSWORD`;
- variable `XUEQING_WINDOWS_TIMESTAMP_URL`.

Self-signed development certificates are explicitly excluded from final production identity.

## Recovery record kept outside Git

The release owner must keep a separate encrypted record containing:

1. Android keystore recovery location and decryption procedure;
2. Android certificate fingerprint and alias;
3. Windows signing provider/account recovery owner or PFX recovery location;
4. Windows certificate Subject/thumbprint;
5. date the offline recovery copy was last verified;
6. GitHub Environment secret replacement procedure.

V1 final acceptance must not proceed until a second copy is independently recoverable and verified.

## Exact-source signing

`release-signing.yml` accepts only a 40-character source SHA and requires it to be present on `main` history. It rebuilds and signs from that exact source. The resulting candidate artifacts retain SHA-256 and signer evidence as workflow artifacts.

Final #67 RC promotion must consume those already-signed candidate bytes; it must not rebuild from a different commit.


## Setup and durable candidate record

The signed Windows MSIX is embedded byte-for-byte into `XueqingSetup-<version>-x64.exe`. Setup and MSIX must be signed by the same production certificate. CI verifies both signatures and runs a current-user install smoke before accepting the Windows candidate.

After Android and Windows signing succeed, the workflow downloads those exact signed artifacts and creates a **draft GitHub prerelease** for the checked-in release label. The draft contains the signed APK, signed MSIX, signed Setup and checksum/signing evidence. #67 promotes this durable candidate record only after final acceptance; it does not rebuild the binaries.
