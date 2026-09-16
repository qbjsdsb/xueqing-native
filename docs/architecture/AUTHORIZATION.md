# Authorization and Responsibility

## Authorization chain

```text
valid Auth identity / live session
→ active Organization Membership
→ role/capability
→ subject scope
→ legal Student/Staff Assignment
→ active entity/service state
→ owner/assignee/current relation
→ command-specific policy
```

A successful login does not grant student-data access. A hidden button is not authorization.

## Actor / Supervisor / Responsible Teacher

- **Actor**: member who actually executes the command.
- **Supervisor**: owner/admin with organization-level oversight capability.
- **Responsible Teacher**: member with the legal teaching assignment responsible for that student+subject/Case/Action.

One person may hold several of these at once, but the server must never infer responsibility merely from visibility or management role.

## Teaching Fact Gate

Teaching Evidence, Intervention, Assessment, teacher Lesson behavior and Quick Capture/new teaching Case require, as applicable:

```text
live session
+ membership active
+ teacher capability
+ matching active teaching subject scope
+ target Student Subject Profile active
+ legal active Student Teacher Assignment
+ operation-specific permission
```

Management-only authority cannot bypass this gate when the manager is acting as a teacher.

Organization-scope supervision may create/operate through dedicated policies, but a new Case must resolve responsibility to a legal active teaching assignment; if a required responsible teacher cannot be resolved, fail closed.

## Session revocation

Production security requires old-token negative tests. Signing out, credential reset or membership disable must prevent subsequent student-business access using a previously captured token, even before its nominal JWT expiry when the provider architecture allows/needs a live-session guard.

## Client projections

Clients should distinguish at minimum:

- personal teaching responsibility projection;
- organization-management/supervision projection.

“My Today/My Students” must never quietly become “all students I can supervise.”