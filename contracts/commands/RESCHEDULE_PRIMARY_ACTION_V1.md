# ReschedulePrimaryAction v1

Status: **Phase 1 command candidate — executable backend evidence required**

## Purpose

Change the business due date of the one authoritative pending primary Action for an open Learning Case without completing the Action or changing Case lifecycle state.

This is an **authoritative online command**.

## Entry point

Reference-provider RPC:

```text
reschedule_primary_action(
  p_operation_id,
  p_organization_id,
  p_student_id,
  p_subject_profile_id,
  p_owner_assignment_id,
  p_case_id,
  p_primary_action_id,
  p_expected_case_version,
  p_expected_action_version,
  p_new_due_on?
)
```

## Concurrency / authority

The command must resolve the current AppUser through IdentityLink and re-check the live Teaching Fact Gate.

It then locks the exact Learning Case and pending primary Action and requires:

- Case is not closed;
- Case owner assignment is still the supplied active assignment;
- responsible teacher is the current actor;
- the Action is the Case's current pending primary Action;
- Action assignee is the current actor;
- Case version equals `p_expected_case_version`;
- Action version equals `p_expected_action_version`.

Stale versions fail with a stable conflict error. The command never performs last-write-wins.

## Atomic effects

A successful new intent atomically:

1. changes only the current primary Action `due_on`;
2. increments Action version by 1;
3. increments Case version by 1 and updates Case server timestamp;
4. appends one `primary_action_rescheduled` Case event with old/new due date and versions;
5. commits one operation receipt.

Case lifecycle state is unchanged.

## Idempotency

Same actor + command + semantically equal payload + same operation_id returns the committed receipt.

Reusing the operation_id for a different payload fails with `XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD`.

## Result

At minimum:

- command = `reschedule_primary_action_v1`;
- operation_id;
- case_id;
- case_state;
- case_version;
- primary_action_id;
- action_version;
- previous_due_on;
- due_on;
- case_event_id;
- server_committed_at.

## Non-goals

This command does not complete/cancel the Action, record Verification, create another Action, or change Case lifecycle state.
