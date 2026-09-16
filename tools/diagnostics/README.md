# Diagnostics Tooling

Target: generate an explicit user-initiated diagnostic bundle for support.

Allowed examples: app/version/build metadata, OS/app architecture, local-schema version, sync queue counts/status, redacted error codes, hashed correlation/operation identifiers, DPI/theme/accessibility mode and non-sensitive network status summary.

Forbidden: student/teacher/guardian names, teaching content, Evidence text/photos, access/refresh tokens, passwords, provider secrets, signing material, raw database copies.

Diagnostics should be local-first and redacted before export. Automatic third-party upload is not assumed.