# Privacy-safe Diagnostics Manifest v1

Status: **active Phase 1 contract**

Tracker: **#82**

## Purpose

Define the only fields that Android and Windows may place in the first V1 support archive.

The design is **allowlist-first**:

```text
typed bounded technical state
        ↓
diagnostics manifest
        ↓
zip/share/save
```

It is not:

```text
collect unrestricted logs
        ↓
attempt to redact private data
```

## Archive

The V1 archive contains exactly one JSON payload named `diagnostics.json`.

The manifest identifies its own schema contract and contains no arbitrary log/message field.

## Allowed information

- platform and OS version;
- application version and exact source commit;
- architecture/package identity;
- deployment profile/environment/trust-domain/provider identifiers;
- client contract and local schema versions;
- coarse Session/Auth category;
- current compatibility state/reason/policy revision;
- bounded counts of pending intents/outbox/attachment staging;
- coarse last-sync category;
- bounded recent machine-readable Xueqing error codes;
- generated timestamp.

## Forbidden information

The archive must never contain:

- Student names or ids;
- Observation/Learning Case/Action free text;
- attachment bytes, thumbnails or user-derived filenames;
- email address;
- password;
- access/refresh token;
- Authorization header;
- database connection string/password;
- service/admin credentials;
- raw local database;
- encryption key material;
- unrestricted application logs.

## Error codes

`recent_error_codes` accepts only bounded `XQ_[A-Z0-9_]+` values.

No exception message, HTTP response body or provider payload is accepted into the manifest.

## Session categories

Allowed values:

- `signed_out`;
- `authenticated`;
- `refresh_required`;
- `revoked_or_invalid`;
- `configuration_unavailable`.

## Compatibility categories

Allowed values:

- `supported`;
- `update_recommended`;
- `update_required`;
- `security_blocked`;
- `unknown`.

`unknown` is diagnostic local state, not a server compatibility decision.

## Sync categories

Allowed values:

- `never`;
- `recent`;
- `stale`;
- `unknown`.

## Privacy acceptance

Tests must construct source fixtures containing obvious private sentinel strings and prove those values cannot appear in the serialized manifest/archive because there is no field through which they can enter.

The support archive is not telemetry. It is created only by an explicit local user/support action.
