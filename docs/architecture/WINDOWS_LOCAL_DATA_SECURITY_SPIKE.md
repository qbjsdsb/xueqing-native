# Windows Local-Data Security Spike

Status: Phase 1 security spike. No production data. No final provider freeze yet.

## Threat model

This gate protects locally cached teaching data against offline file copying, casual disk inspection, accidental backup exposure and loss of a device while the application is not already unlocked by an authorized Windows user.

It does **not** claim to protect data from malware or an administrator already executing as the authorized Windows user, and it does not replace the Offline Access Lease. Authorization freshness, local encryption and remote RLS solve different problems.

## 2026-09 candidate research

### SQLCipher

SQLCipher Community Edition source remains available under a BSD-style license, but official SQLCipher .NET binary packages are Commercial/Enterprise products. The historical free `SQLitePCLRaw.bundle_e_sqlcipher` path is deprecated and was never an official Zetetic binary distribution. Xueqing will not adopt that deprecated bundle.

Self-building SQLCipher Community Edition remains technically possible, but it would make Xueqing responsible for a native Windows/OpenSSL build and update pipeline. That is not the first zero-cost candidate while a maintained packaged alternative exists.

### SQLite3 Multiple Ciphers

SQLite3MC is MIT-licensed and actively maintained. The modern independent packages are `SQLite3MC.PCLRaw.bundle`, `SQLite3MC.PCLRaw.provider`, and `SQLite3MC.PCLRaw.lib`; the older `SQLitePCLRaw.*e_sqlite3mc` packages are deprecated.

For `Microsoft.Data.Sqlite`, SQLite3MC explicitly requires the **Core** package so the process has exactly one SQLitePCLRaw bundle. This spike therefore tests `Microsoft.Data.Sqlite.Core 10.0.12` + `SQLite3MC.PCLRaw.bundle 2.4.0` in an isolated project without changing the already-green Outbox provider.

Version 2.4.0 is based on SQLite 3.53.4, supports database rekey for most WAL scenarios, and is published through the project's GitHub Actions build/release pipeline. Its native package is materially larger than the normal SQLite provider, so install-size impact remains a packaging acceptance item.

### Windows DPAPI + AES-GCM

`System.Security.Cryptography.ProtectedData 10.0.12` wraps Windows DPAPI. `DataProtectionScope.CurrentUser` is preferred over `LocalMachine`: LocalMachine would allow every user on the machine to decrypt the protected secret, while CurrentUser binds protection to the Windows user credential context. Windows roaming profiles can make CurrentUser-protected data decryptable from another machine in the same profile environment, so DPAPI must not be described as a hardware-bound device key.

The intended key hierarchy candidate is:

```text
random 256-bit local master secret
  -> wrapped by DPAPI CurrentUser
  -> used directly only for key-encryption / derived-purpose keys
  -> AES-GCM envelopes for files/values outside whole-DB encryption
```

AES-GCM envelopes use a fresh 96-bit nonce, a 128-bit authentication tag and associated data bound to an explicit purpose + record identifier. Swapping ciphertext between records or changing its purpose must fail authentication.

## Why test both layers

Whole-database encryption and DPAPI/AES-GCM are complementary, not mutually exclusive:

- encrypted SQLite can protect the relational cache, indexes, Outbox payloads and WAL files as a unit;
- DPAPI can keep the database passphrase/master secret out of source code and plaintext config;
- AES-GCM is still needed for external attachment files or narrowly scoped sensitive blobs that live outside SQLite.

## Spike acceptance tests

The Windows 2025 CI run must prove:

1. AES-GCM decrypts only with the correct key, purpose and record id;
2. encrypting the same plaintext twice yields different envelopes;
3. ciphertext/tag tampering fails authentication;
4. DPAPI CurrentUser round-trips a random 256-bit key;
5. DPAPI purpose separation rejects the wrong purpose;
6. SQLite3MC reports the expected 2.4.x runtime;
7. an encrypted SQLite database can be reopened with the correct password;
8. missing/wrong passwords cannot read the encrypted schema/data;
9. a high-entropy fictional plaintext marker does not appear in the DB/WAL files;
10. WAL-mode encrypted database rekey invalidates the old password and accepts the new password;
11. the pre-existing Outbox/Core/WinUI workflows remain green.

## Not yet authorized by this spike

Even if all tests pass, this PR does **not** yet authorize real student data. Before provider freeze and production use we still need:

- migrate the real Infrastructure project in a separate PR and re-run all Outbox tests on the encrypted provider;
- measure startup/open/write/read/rekey performance with representative fixture volume;
- verify MSIX publish/install/upgrade includes the correct native DLLs on x64 and chosen future architectures;
- define the exact key-slot file format and atomic write/recovery rules;
- define logout, account-switch, app-user disable and Offline Access Lease expiry behavior;
- define which unsynced drafts, if any, survive logout and how their key is retained;
- define attachment encryption/key rotation/deletion behavior;
- test plaintext-to-encrypted migration and rollback/recovery;
- produce user-accessible MIT/open-source notices;
- document secure diagnostic behavior so keys, plaintext and raw databases never enter support bundles.

No signing key, database key, password, token, production endpoint or real student information belongs in Git.
