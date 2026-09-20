# TransitionLearningCaseState v1

Status: **Phase 1 command candidate — executable backend evidence required**

## Purpose

Advance one open Learning Case through the explicit forward-only lifecycle without changing or replacing its current pending primary Action.

This is not a generic state setter. The server accepts only the next legal transition:

```text
new -> confirmed
confirmed -> intervening
intervening -> pending_verification
pending_verification -> stable
```

`closed` and reopen use dedicated commands because they change the primary-Action invariant.

## RPC

```text
transition_learning_case_state(
  p_operation_id,
  p_organization_id,
  p_student_id,
  p_subject_profile_id,
  p_owner_assignment_id,
  p_case_id,
  p_expected_case_version,
  p_target_state
)
```

## Authorization and scope

The server resolves the actor from the live provider identity and application-owned IdentityLink.

The command requires:

- enabled AppUser;
- active organization membership with teaching capability;
- active Student;
- active StudentSubjectProfile;
- the supplied owner assignment to be active and owned by the resolved actor;
- the Learning Case to match organization/student/profile/owner/responsible teacher.

Management role alone never substitutes for teaching assignment.

## Concurrency

The caller supplies `p_expected_case_version`.

The command locks:

1. the Case;
2. its current pending primary Action.

The Case version must match the expected version. A stale version fails closed with `XQ_CASE_VERSION_CONFLICT`.

Locking the pending Action serializes lifecycle transitions against Action reschedule, Verification/Next Action, and close.

## Invariants

Before transition:

- the Case must be in the exact predecessor state for `p_target_state`;
- the open Case must have exactly one pending primary Action assigned to the same legal owner.

The command:

1. increments Case version by 1;
2. changes Case state to the requested legal next state;
3. preserves the current pending primary Action unchanged;
4. appends one `case_state_transitioned` event;
5. commits one idempotent operation receipt.

Verification facts do not automatically run this command and do not automatically stabilize or close the Case.

## Result

The receipt includes at minimum:

- command = `transition_learning_case_state_v1`;
- operation_id;
- organization/student/profile scope;
- owner_assignment_id;
- responsible_teacher_app_user_id;
- case_id;
- previous_case_state;
- case_state;
- case_version;
- primary_action_id;
- server_committed_at.

## Idempotency

Retry the same user intent with the same `operation_id`.

Same operation + same normalized payload returns the committed result. Reusing the operation id with different payload fails with `XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD`.
