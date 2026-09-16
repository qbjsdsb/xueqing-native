# Commands and Invariants

## Principle

RLS answers **who may access**. Domain commands answer **whether this multi-step change is complete, legal and safe to retry**.

## High-risk command shape

A high-risk lifecycle/governance command must have:

- stable `operation_id` representing one user intent;
- expected aggregate version(s) and relevant current-relationship snapshot;
- server-side actor/authority resolution;
- deterministic lock/re-read of affected current rows;
- final invariant validation after locks;
- operation-bound event/audit keys;
- one atomic commit;
- a committed operation result/receipt that is returned on retry.

Timeout with unknown result: query/retry the same `operation_id`; never create a replacement intent and never repair with guessed client CRUD.

## Initial online-only commands

- formal Case confirm/transition/stabilize/close/reopen;
- member credential/lifecycle commands;
- teacher reassignment/handoff and scope revocation;
- Student lifecycle governance and merge;
- operations that require a fresh authorization/responsibility snapshot.

The list can later shrink only after a specific offline protocol is proven safe.

## Offline-queue candidates

- protected drafts;
- Quick Capture designed as queued submission;
- explicitly approved low-risk append-only facts.

Queued submission does not mean trusted submission: the server must re-run the Teaching Fact Gate and entity-state checks.

## Core invariants

- formal open active-profile Case → legal owner + exactly one pending primary Action;
- `closed` → no pending primary Action;
- `assessment passed` does not imply stable/closed;
- finalized historical facts retain original actor/provenance;
- management supervision must not rewrite responsible teacher by implication;
- duplicate `operation_id` must not duplicate state, event, action or audit side effects.