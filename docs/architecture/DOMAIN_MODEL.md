# Domain Model

This document carries forward the mature semantics from legacy Xueqing while allowing the physical schema to be redesigned cleanly.

## Identity and organization

`AppUser` is an application-owned stable identity. External provider identities are represented by `IdentityLink(provider_key, issuer, external_subject)` and may change without rewriting teaching history.

An `OrganizationMembership` controls current organization participation (`onboarding`, `active`, `disabled`). Roles initially remain `org_owner`, `org_admin`, `teacher`, but role alone is not a teaching assignment.

## Student aggregate

An organization has one canonical `Student` per real student. Grade, class/campus and teacher changes are historical relations, not reasons to create a new Student.

`StudentSubjectProfile` represents the student's service state within a subject. Its active/inactive/archived state gates new teaching facts.

`StudentTeacherAssignment` expresses actual teaching responsibility for a student+subject, with lead/collaborator semantics and active intervals.

## Learning Case

Current state:

```text
new → confirmed → intervening → pending_verification → stable → closed
```

Rules:

- reopen is a command/event, not a status;
- assessment result is a verification fact and does not automatically change Case status;
- a formal open Case on an active Profile has a legal owner and exactly one pending primary Action;
- closed Case has no pending primary Action;
- service suspension may preserve unresolved history while suspending ordinary tracking.

## Historical facts

Case events, finalized Evidence, Intervention and Assessment preserve provenance. Corrections/supersession must be explicit; ordinary edits must not silently rewrite historical meaning.

## Time

System event time is stored in UTC. Business dates, Today and due/overdue interpretation use the organization's IANA timezone, not the device timezone.

## Concurrency

Mutable aggregates use explicit versions for server-authoritative optimistic concurrency. `Student.version` protects Student-root lifecycle/canonical identity only; child mutations do not mechanically increment it unless the root snapshot actually changes.

## Responsibility

Actor, Supervisor and Responsible Teacher are independent concepts. Organization supervision permits oversight; it does not create a missing StudentTeacherAssignment or silently transfer owner/assignee responsibility.