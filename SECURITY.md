# Security Policy

Xueqing handles potentially sensitive educational records. Security boundaries are product requirements, not optional hardening.

## Current status

The repository is public and Phase 0 / development only. Do not use it with real student, guardian, teacher, institution, credential, or production attachment data until the explicit production security gates are passed.

## Never commit

- access / refresh tokens;
- passwords or temporary credentials;
- Supabase service-role or other privileged keys;
- database passwords or backup credentials;
- signing certificates/private keys;
- production exports or database dumps;
- real student, guardian, teacher or institution personal data;
- private educational attachments.

## Security model

The target model includes live-session validation, active organization membership, explicit capabilities, subject scopes, legal student-teacher assignments, profile/entity state, RLS and command-specific authorization. UI visibility is never authorization.

High-risk state transitions must run as server-authoritative transactional commands using idempotent `operation_id`, expected versions, locking/re-validation and auditable results.

## Local data

Local-first support creates a second security boundary. Offline access lease duration, local encryption, cached-field minimization, attachment retention and purge behavior are hard Phase 0/Spike decisions. An account disabled on the server must lose future server access immediately; cached data must be removed or rendered unavailable when the client next validates authorization, and offline access may not be indefinite.

## Reporting

Do not publish exploitable vulnerabilities in a public issue before a remediation path exists. Use GitHub private vulnerability reporting if enabled; otherwise contact the repository owner privately.
