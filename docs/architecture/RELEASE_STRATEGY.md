# Release Strategy

## Development

Public GitHub repository, fictional data only. CI may use standard hosted runners and produce temporary development artifacts.

## Windows

Primary technology: WinUI 3 packaged as MSIX.

Development/testing may use self-signed packages. Public production distribution must not assume that self-signed MSIX is acceptable for ordinary teachers.

Before production freeze, compare:

1. Microsoft Store distribution (Store signing + update channel);
2. direct trusted-signature MSIX/App Installer distribution.

Do not build a custom updater until platform-provided update paths are proven insufficient.

## Android

Development/release candidate may use signed APKs under a controlled test key strategy. Production signing keys must never be stored in Git. Play distribution can be evaluated later; it is not required for early pilots.

## Version compatibility

Client, local schema and backend contract versions must be explicit. Backend migrations must consider a compatibility window for clients that have not updated yet.

## Production gate

No production release with real data until provider/region, old-token behavior, local-data encryption, backup/restore, Storage recovery, signing, network reliability, privacy handling and real-device acceptance are all documented and passed.