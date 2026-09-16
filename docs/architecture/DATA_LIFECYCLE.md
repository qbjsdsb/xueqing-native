# Data Lifecycle and Production Privacy Gate

Xueqing may process sensitive educational data, including information about minors. This document is an engineering production gate, not legal advice.

## Principles

- collect only fields needed for teaching/organization workflows;
- classify data by purpose and sensitivity;
- define retention for server records, attachments, local cache, drafts, exports, logs and backups;
- distinguish business archive, append-only teaching history and a privacy deletion/erasure workflow;
- privacy deletion must consider identifiers, attachments, local caches and backup retention rather than only changing a status column;
- exports and diagnostics must follow minimization/redaction rules;
- provider/region choice must consider data residency, network quality, backup location and applicable cross-border requirements before real-data launch.

## Production gate

Before real student data, document the applicable notice/consent/guardian process, retention/deletion workflow, data-location decision, incident/contact process and any required professional compliance review. Development fixtures remain fictional regardless of future production policy.
