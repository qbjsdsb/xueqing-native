# AI / Agent Bootstrap and Handoff

## Purpose

A fresh ChatGPT, Work, Codex or human contributor must be able to recover Xueqing without relying on one long conversation or private model memory.

## Bootstrap order

Before changing code or architecture:

1. read `/AGENTS.md`;
2. read `docs/project/PROJECT_STATE.yaml`;
3. query GitHub for current `main` HEAD;
4. inspect active Draft PRs relevant to the task;
5. verify the PR base SHA, current head SHA and exact-head CI evidence;
6. read affected ADRs/contracts/docs;
7. read implementation/tests for the affected boundary;
8. consult legacy `qbjsdsb/xueqing` only when the task needs historical behavior or migration evidence.

State the recovered baseline before substantial work: repository, phase, task scope, active branch/PR, exact head, verified gates and frozen boundaries.

## Conflict rule

When information disagrees, prefer:

`GitHub current state > accepted ADR/contracts > durable project state > implementation comments > chat/project memory > old summaries`.

Never use memory to override a newer commit, ADR or CI result.

## Dynamic handoff

Use a Draft PR as the live handoff unit for non-trivial work. Its body should record:

- goal and base SHA;
- scope and frozen boundaries;
- implemented work;
- current head SHA;
- exact CI evidence;
- blockers/known issues;
- next safest action.

Do not copy rapidly changing PR numbers, HEADs or CI run IDs into `PROJECT_STATE.yaml`.

## Scratch-space rule

No valuable work may exist only in an agent scratch filesystem, ephemeral cloud machine or chat. Push durable work to a branch early. A crashed/expired session must not destroy useful work.

## Exact-SHA evidence

A test result applies only to the exact SHA tested. After any new commit, do not inherit the old green state without fresh affected-gate evidence.

## Privacy

AI/project memory may contain product principles, architecture decisions and fictional fixtures. Do not place real student/guardian educational content, credentials, tokens, production exports, raw production logs or attachments into memory for convenience. Production incidents must be redacted first.
