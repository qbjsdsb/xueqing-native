# CloseLearningCase v1

Status: **Phase 1 command candidate — executable backend evidence required**

## Purpose

Close a stable Learning Case while atomically removing its pending-primary-Action obligation.

Closing is explicit. Verification outcome never closes a Case automatically.

## RPC

```text
close_learning_case(
  p_operation_id,
  p_organization_id,
  p_student_id,
  p_subject_profile_id,
  p_owner_assignment_id,
  p_case_id,
  p_primary_action_id,
  p_expected_case_version,
  p_expected_action_version
)
```

## Preconditions

The same live actor/membership/student/profile/owner-assignment checks as other authoritative Case commands apply.

Additionally:

- Case state must be `stable`;
- Case version must equal `p_expected_case_version`;
- the supplied Action must be the Case's current pending primary Action;
- Action version must equal `p_expected_action_version`.

## Atomic mutation

The command:

1. locks the Case and current primary Action;
2. changes the Action from `pending` to `cancelled` and increments Action version;
3. changes Case state from `stable` to `closed` and increments Case version;
4. appends one `case_closed` event;
5. commits one idempotent receipt.

A closed Case has **zero** pending primary Actions.

Cancelling the monitoring/follow-up Action on close does not rewrite earlier completed Actions or Verification history.

## Result

The receipt includes:

- command = `close_learning_case_v1`;
- case_id;
- previous_case_state = `stable`;
- case_state = `closed`;
- case_version;
- cancelled_primary_action_id;
- cancelled_action_version;
- authoritative owner/responsible-teacher identity;
- server_committed_at.

## Concurrency and idempotency

Stale Case/Action versions fail closed. Unknown result retries reuse the same `operation_id`.

Same operation + same payload replays the committed receipt. Different payload under the same operation id is rejected.
