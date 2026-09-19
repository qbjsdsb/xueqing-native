# ReopenLearningCase v1

Status: **Phase 1 command candidate — executable backend evidence required**

## Purpose

Reopen a previously closed Learning Case as a new explicit lifecycle event while creating the new pending primary Action required by an open Case.

`reopen` is a command/event, never a seventh Case state.

## RPC

```text
reopen_learning_case(
  p_operation_id,
  p_organization_id,
  p_student_id,
  p_subject_profile_id,
  p_owner_assignment_id,
  p_case_id,
  p_expected_case_version,
  p_new_primary_action_text,
  p_new_primary_action_due_on
)
```

## Preconditions

The server resolves and validates the live actor, membership, Student, active subject profile and active owner assignment.

The Case must:

- belong to the supplied teaching scope and responsible teacher;
- currently be `closed`;
- have the expected Case version;
- have no pending primary Action.

This V1 does not silently transfer responsibility. If the old owner assignment is no longer active, a future explicit handoff/reassignment command must resolve responsibility before reopen.

## Atomic mutation

The command:

1. locks the closed Case;
2. verifies no pending primary Action exists;
3. creates one new pending primary Action assigned to the legal owner;
4. changes Case state from `closed` to `intervening`;
5. increments Case version by 1;
6. appends one `case_reopened` event;
7. commits one idempotent operation receipt.

The new Action starts at version 1.

Reopen does not erase the prior close event, cancelled Action, completed Actions, Verifications, or any historical provenance.

## Result

The receipt includes:

- command = `reopen_learning_case_v1`;
- case_id;
- previous_case_state = `closed`;
- case_state = `intervening`;
- case_version;
- new_primary_action_id;
- new_action_version = 1;
- new_action_text;
- new_action_due_on;
- authoritative owner/responsible-teacher identity;
- server_committed_at.

## Idempotency

Retry the same intent with the same operation id. Receipt replay must not create a duplicate Action or event.

Different payload under the same operation id is rejected.
