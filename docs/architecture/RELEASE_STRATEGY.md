# Release Strategy

## Development

Public repository, fictional data only, cloud-reproducible CI. Zero-cost is a development preference, not a reason to reduce production security, recoverability or compliance.

## Windows

WinUI 3 packaged as MSIX. Development may use self-signed packages. Before production compare Microsoft Store distribution (trusted signing/update channel) with direct trusted-signature MSIX/App Installer. Do not build a custom updater until platform paths are proven insufficient.

## Android

Development/release candidates may use controlled test signing. Production signing keys never enter Git and require a documented encrypted backup/recovery procedure. Final production distribution is a production gate rather than a permanent GitHub-APK assumption.

## Compatibility

Client, Projection/Command contract, local Durable Intent schema and backend versions are explicit. Maintain a practical N-1 compatibility window. Additive evolution is preferred; incompatible contracts are versioned.

Before production define a minimal emergency client policy for recommended/minimum/security-blocked versions. It is not a generic feature-flag system.

## Recovery

Release validation includes upgrade and downgrade/recovery smoke. Projection Cache may be rebuilt; Durable Intent may not be sacrificed by rollback.

Database backup alone is insufficient when Storage contains attachments. Production DR requires database backup + Storage object backup/manifest + a tested restore rehearsal.

## Production gate

No real-data release until provider/region/data-residency decision, old-token behavior, local security, privacy/data lifecycle, backup+Storage restore, signing/recovery, network reliability, release compatibility and real-device/platform acceptance are documented and passed.
