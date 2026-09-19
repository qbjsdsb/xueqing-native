# RecordVerificationAndNextAction v1

Status: **Phase 1 command candidate — executable backend evidence required**

## Purpose

Record what happened after the current primary Action and atomically establish the next concrete teaching Action while preserving the formal open-Case invariant.

This is an **authoritative online command**.

## Entry point

Reference-provider RPC:

```text
record_verification_and_next_action(
  p_operation_id,
  p_organization_id,
  p_student_id,
  p_subject_profile_id,
  p_owner_assignment_id,
  p_case_id,
  p_current_primary_action_id,
  p_expected_case_version,
  p_expected_action_version,
  p_verification_outcome,
  p_verification_summary,
  p_next_action_text,
  p_next_action_due_on?
)
```

## Verification outcome

V1 uses a deliberately small descriptive vocabulary:

- `met`;
- `partially_met`;
- `not_met`;
- `uncertain`.

These values describe the verification result. They do **not** automatically imply Case `stable` or `closed`.

## Concurrency / authority

The server resolves the current actor and re-runs the live Teaching Fact Gate.

It locks the exact open Case and current pending primary Action and validates:

- supplied assignment is still active and owned by the actor;
- Case responsible teacher is the actor;
- Case version equals the expected version;
- the supplied Action is the current pending primary Action;
- Action version equals the expected version.

Stale versions fail closed.

## Atomic effects

A successful new intent atomically:

1. appends one immutable Verification fact tied to the completed Action;
2. marks the old primary Action `completed`, sets completed timestamp and increments its version;
3. creates exactly one new `pending` primary Action assigned to the same legal current teacher;
4. increments Case version by 1 and updates Case timestamp;
5. leaves Case lifecycle state unchanged;
6. appends one `verification_recorded_next_action_created` Case event;
7. commits one operation receipt.

At commit the open Case still has **exactly one** pending primary Action.

There is no client repair path if any side effect fails; the entire transaction rolls back.

## Result

At minimum:

- command = `record_verification_and_next_action_v1`;
- operation_id;
- case_id;
- case_state;
- case_version;
- completed_primary_action_id;
- completed_action_version;
- verification_id;
- verification_outcome;
- next_primary_action_id;
- next_action_version = 1;
- case_event_id;
- server_committed_at.

## Non-goals

This command does not:

- automatically mark the Case stable or closed;
- reopen a closed Case;
- reassign responsibility;
- run offline;
- replace formal Case lifecycle commands.

A later lifecycle command may use accumulated Verification facts as evidence, but lifecycle state remains an explicit teacher/governance decision.
