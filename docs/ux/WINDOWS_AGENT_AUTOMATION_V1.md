# Windows Agent Automation V1

Status: active Phase 1 compatibility contract for Issue #79.

## Authority boundary

Automation is an input/navigation surface, not an authority surface.

Agents, accessibility tooling and terminal launchers must use the same Session/Auth,
IdentityLink, organization membership, teaching assignment, command idempotency and
application-service boundaries as a human using the WinUI app.

Forbidden:

- service-role/admin credentials in the client or agent surface;
- direct SQL or Storage authority;
- deep-link parameters that fabricate organization, role, AppUser, assignment,
  command receipt, expected version or write authorization;
- coordinate-only core journeys;
- background autonomous consequential writes.

## Protocol V1

The packaged Windows app owns one protocol scheme:

- `xueqing://today`
- `xueqing://student/<student-uuid>`
- `xueqing://learning/<case-uuid>`

Only canonical, non-empty UUID entity ids are accepted. Query strings, fragments,
userinfo, extra path segments and unknown routes fail closed.

A protocol request is retained across the ordinary login boundary, then resolved only
against the authenticated application-owned workspace. An unresolved or ambiguous id
must never manufacture context.

## Stable automation ids

The following identifiers are compatibility surface in V1:

Shell/auth:

- `AppTitleBar`
- `ShellNavigation`
- `WorkspaceSwitcher`
- `PersonalWorkspaceMenuItem`
- `OrganizationWorkspaceMenuItem`
- `TodayNavigation`
- `StudentsNavigation`
- `LearningNavigation`
- `OrganizationManagementNavigation`
- `SignOutNavigation`
- `AuthEmail`
- `AuthPassword`
- `AuthSubmit`
- `AuthRetry`
- `AuthClearSession`
- `AuthStatus`

Student/Learning surface ids remain governed by their existing native UX smoke contract.
Renaming a frozen id requires coordinated automation-test and contract updates.

## Consequential-write rule

Navigation, searching, reading and editing a local draft may be automated.

Submitting an Observation, progressing/closing/reopening a Learning Case, changing
organization authority, sending an invitation, or another authoritative write remains
an explicit application action. An automation agent must stop before that action unless
the user has explicitly authorized it.

## Acceptance direction

The exact packaged build must prove:

1. ordinary launch still works;
2. protocol launch routes through the same authenticated app;
3. unknown/unauthorized ids fail closed;
4. keyboard-only and UIA journeys remain equivalent;
5. sign-out/revocation prevents the old account context from being reused;
6. no hard-coded screen coordinates are required for the critical journey.

A local `xueqingctl` is not part of V1 unless UIA + keyboard + protocol launch proves
insufficient.
