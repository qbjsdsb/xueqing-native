# Security Policy

Xueqing handles potentially sensitive educational records. Security boundaries are product requirements.

## Current status

The public repository is development/Phase 0 only. Do not use it with real student, guardian, teacher, institution, credential or production attachment data until explicit production gates pass.

## Never commit

Tokens, passwords, temporary credentials, service-role/privileged keys, database/backup passwords, signing private keys/certificates, production exports/dumps, real personal/educational data, private attachments or credential-bearing logs.

## Authorization

UI visibility is never authorization. Server paths must validate live session, active membership, capabilities, teaching scopes/assignments, entity state and operation permission. High-risk commands are server-authoritative, atomic and idempotent.

## Environment isolation

Development, staging and production are separate trust domains with separate provider projects/issuers/secrets. Ordinary PRs must not receive production secrets. Production deployment uses protected environments/approval and never runs untrusted pull-request code with privileged credentials.

## Local data

Offline access is finite. Projection Cache is minimized/disposable; Durable Intent receives strong migration/privacy protection. Encryption, backup exclusion, lease trust/clock rollback, purge/account-switch and attachment retention are production gates.

## CI and supply chain

Use least-privilege workflow permissions. Pin third-party Actions to immutable commit SHAs where practical. Do not use privileged `pull_request_target` patterns to execute untrusted code. Dependency/security scanning may be enabled as implementation code appears.

## Reporting

Do not publish exploitable vulnerabilities in a public issue before a remediation path exists. Prefer GitHub private vulnerability reporting if enabled; otherwise contact the repository owner privately.
