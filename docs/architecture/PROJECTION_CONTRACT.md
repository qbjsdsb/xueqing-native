# Projection Contract

## Truth

The PostgreSQL schema is not the client UI API. Android and Windows consume stable versioned read projections and submit explicit domain commands.

## V1 projection families

- `PersonalBootstrap`: capabilities, authoritative business date/timezone, Today items and student summaries.
- `StudentDetail`: student/profile/current Case and next-action projection.
- `CaseTimelinePage`: paged historical facts/events.
- `OrganizationSupervision`: organization-readable supervision summaries and responsibility facts.
- `ManagementSnapshot`: members/students/subjects/assignment summaries required for management workflows.

The exact physical PostgREST view/function layout is a Backend Spike decision.

## Envelope

Each projection should expose enough metadata to validate interpretation, including contract version, generation/freshness token as appropriate, authoritative business date/timezone and the caller's relevant scope/capability facts.

## Rules

- projections may duplicate/reshape facts for reading but never become a second write model;
- managers seeing a Profile does not make them the Responsible Teacher;
- large history is paged rather than placed in bootstrap snapshots;
- clients do not derive authorization from missing/visible rows;
- additive evolution is preferred; incompatible evolution is versioned.

## Acceptance

Both clients deserialize the same fictional versioned fixtures and produce equivalent domain-facing DTO semantics. Unknown/new fields must not break older compatible clients.
