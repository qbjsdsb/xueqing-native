# Xueqing Native Interaction State Contract v1 Candidate

Status: **Candidate — Prototype / protocol evidence required**

This document defines how native clients communicate editing, local durability, queued submission, authoritative online commands, unknown outcomes, conflicts, authorization changes and scope changes.

It is a UX contract, not a new domain protocol. Architecture and security contracts remain authoritative. In particular:

- `../architecture/COMMANDS.md`
- `../architecture/DRAFT_ENGINE.md`
- `../architecture/LOCAL_STATE_MODEL.md`
- `../architecture/AUTHORIZATION.md`
- `../architecture/PROJECTION_CONTRACT.md`
- `../architecture/LOCAL_DATA_SECURITY.md`

If a product surface cannot determine which class an operation belongs to, it must fail closed rather than inventing optimistic semantics.

---

## 1. Why this contract exists

Several user actions can look like “Save” while having different guarantees:

- text may only be an in-progress editor value;
- a draft may be durably saved on this device;
- a low-risk intent may be durably queued but not yet accepted by the server;
- an authoritative online command may be waiting for a server result;
- a timeout may leave the final server outcome unknown;
- the read model may be stale while protected local work is still safe;
- authorization may change while a draft still exists locally.

The UI must not collapse these into one generic success/failure state.

Canonical principle:

> **Say only what the system has actually proved.**

A client may say `Saved locally` only after the required durable local transaction completed. It may say `Submitted` / show a committed state only after authoritative server acceptance is known.

---

## 2. Four operation classes

Every meaningful write interaction should map to one of these classes before visual design is finalized.

### A. Local protected draft

Examples:

- Quick Capture text while the user is still composing;
- an unfinished note protected by Draft Engine;
- staged attachment metadata before formal submission.

Guarantee target:

- device-local durable preservation according to Draft Engine / Local State policy;
- scope-isolated;
- recoverable across supported navigation/lifecycle/process interruption;
- not a statement that the server accepted a teaching fact.

### B. Queueable low-risk intent

Examples are limited to operations explicitly approved by architecture, such as:

- Quick Capture designed as queued submission;
- approved low-risk append-only facts.

Guarantee target:

- durable local intent + stable operation identity has been recorded;
- submission may happen later;
- the server still re-runs authorization, Teaching Fact Gate and entity-state checks;
- queued does not mean accepted.

### C. Authoritative online command

Initial examples include:

- formal Case confirm/transition/stabilize/close/reopen;
- teacher reassignment / handoff / scope revocation;
- member lifecycle / credential commands;
- Student lifecycle governance / merge;
- any operation that requires a fresh authorization/responsibility snapshot.

Guarantee target:

- do not present success without authoritative committed result/receipt;
- expected-version/current-relation semantics remain server-authoritative;
- timeout with unknown result must reuse/query the same operation identity rather than create a new intent.

### D. Projection refresh / rebuild

Examples:

- refresh Today/Students/Case read models;
- rebuild disposable projection cache after schema, authorization or generation change.

Guarantee target:

- read-model state may be replaced;
- protected Durable Intent must not be destroyed merely because Projection Cache is stale or rebuilt.

---

## 3. Shared user-visible state vocabulary

Use Chinese-first wording in the product. Exact copy may be refined in prototype, but semantic distinctions must survive.

### Editing

Meaning: the user is actively editing; local durability may not yet be proven for the latest keystroke/composition state.

Preferred presentation:

- normally no banner;
- preserve visible content;
- autosave indicator only if latency is meaningfully noticeable.

Do not say:

- `已保存` before durable persistence is complete.

### Saving locally

Meaning: the client is flushing protected work into Durable Intent.

Candidate copy when visible:

- `正在保存草稿…`

Usually this state should be subtle and brief.

### Local draft safe

Meaning: the protected draft is durably stored in the correct local scope, but is not necessarily server-committed.

Candidate copy when distinction matters:

- `草稿已保存在本机`
- `本机草稿`

Do not replace this with generic `保存成功` when the user could reasonably interpret that as remote/formal completion.

### Waiting to sync

Meaning: a queueable intent is durably recorded locally and awaiting server submission.

Candidate copy:

- `待同步`
- `离线 · 3 条记录待同步`

Preferred behavior:

- do not turn the whole workspace into a warning state;
- show the state near the affected record or in one restrained global exception surface.

### Syncing

Meaning: a queued intent is actively being submitted.

Candidate copy if needed:

- `正在同步…`

Avoid permanent spinners or blocking unrelated work.

### Remotely committed / authoritative success

Meaning: authoritative acceptance is known.

Preferred presentation:

- the new state/fact appears in place;
- normal success is quiet;
- explicit `已提交` / `已完成` text is used only when the distinction from local state is important.

Do not show permanent green `已同步` decoration on ordinary healthy screens.

### Online command pending

Meaning: an authoritative command is in flight and cannot safely be represented as complete yet.

Candidate copy:

- `正在提交…`
- operation-specific wording such as `正在关闭问题…`

Behavior:

- prevent duplicate intent creation;
- keep useful surrounding context visible;
- allow cancellation only if the protocol proves cancellation semantics.

### Result unknown

Meaning: transport failed/timed out after the command may have reached the server; final outcome is unknown.

Candidate copy:

- `正在确认结果…`
- `暂时无法确认是否已完成`

Primary safe action:

- `重新确认`

Rules:

- reuse/query the same operation identity;
- do not offer `再提交一次` as a new intent;
- do not optimistically roll the UI back to the old authoritative state if that would imply known failure;
- do not show a raw timeout/HTTP code as the primary explanation.

### Authoritative rejection / validation failure

Meaning: the server definitively rejected the operation.

Candidate copy should explain the business reason when safe:

- `无法关闭：仍有待处理的下一步`
- `当前任课关系已变化，无法提交这条教学记录`

Behavior:

- keep recoverable user input;
- offer the next safe corrective action;
- do not retry blindly when the rejection is deterministic.

### Version / current-relation conflict

Meaning: the authoritative object changed since the client snapshot used by the command.

Candidate copy:

- `这条记录已发生变化`
- `王老师已更新下一步。你的本机草稿仍然保留。`

Primary action:

- `查看最新内容`

Rules:

- refresh authoritative projection;
- preserve protected draft/intent according to policy;
- do not ask teachers to choose `本地版本` vs `云端版本` as a database-level merge decision;
- do not show `409 Conflict` as primary UI.

### Permission reduced / revoked

Meaning: the user no longer has the same authorization for the target object or scope.

Candidate copy:

- `当前已无法继续提交这条记录`
- `你的权限已发生变化`

Rules:

- do not present unauthorized content as a generic empty state;
- do not leak stale unauthorized projection data;
- handle protected local draft according to explicit retention/recovery policy;
- management visibility must not be treated as responsible-teacher permission.

### Session invalid / membership disabled

Meaning: live authorization/session can no longer support business access.

Candidate copy:

- `登录状态已失效，请重新登录`
- `你已不再属于当前机构`

Rules:

- remove/disable unauthorized projections promptly;
- do not silently submit queued teaching intent under a no-longer-valid authority;
- protected local intent handling follows policy and must not be guessed by UI.

### Offline access unavailable / lease expired

The Offline Access Lease protocol is still an open architecture gate. Therefore copy and exact behavior remain **Prototype/Protocol Required**.

Required UX principle now:

- if locally cached educational data may no longer be legally/securely displayed, the UI must distinguish `offline but allowed` from `offline access no longer authorized`;
- never imply that old cached data is safe to keep showing merely because it exists on disk.

Do not freeze duration, countdown behavior or recovery actions until the architecture spike accepts them.

### Read model stale / refreshing

Meaning: Projection Cache may be old or being replaced.

Preferred behavior:

- preserve the last safe useful structure when authorization allows;
- use restrained refresh affordance;
- do not flash false empty states;
- never delete Durable Intent as a side effect of ordinary projection refresh.

### Scope switch in progress

Meaning: app user, organization or environment is changing.

Rules:

- current-scope drafts must not restore into the new scope;
- old-scope projections must not remain visible after authorization/scope transition requires removal;
- block ambiguous cross-scope submission;
- show the new scope identity clearly once transition completes.

Candidate copy only when needed:

- `正在切换机构…`

---

## 4. State precedence

When multiple technical states exist, show the state that best represents the user’s immediate safety/action need.

Candidate priority for an affected operation:

1. unauthorized / scope invalid;
2. destructive data-integrity or unrecoverable local failure;
3. authoritative result unknown;
4. authoritative conflict/rejection;
5. local save failed;
6. offline / waiting to sync;
7. syncing / submitting;
8. local draft safe;
9. normal committed state.

This is not a global banner priority table. Prefer local presentation near the affected object unless the problem affects the whole workspace/session.

Do not stack five banners for one underlying condition.

---

## 5. Back, close and navigation behavior

### Protected draft exists

Ordinary Back/navigation should normally be allowed after the draft has been durably preserved.

Do not repeatedly ask:

- `是否放弃修改？`

when the app can safely preserve the draft.

Explicit `丢弃草稿` is destructive and may require confirmation when the consequence is not obvious.

### Local persistence still in progress

If leaving immediately would violate the Draft Engine guarantee:

- request/await the required flush according to lifecycle contract;
- keep UI responsive where possible;
- do not silently abandon the newest committed composition text.

### Authoritative online command in flight

Back/navigation must not create a second command or make the user believe the operation was cancelled when it was not.

If the UI allows leaving:

- the operation identity/result tracking must continue safely;
- returning must show the real state.

If safe navigation cannot be supported, communicate the brief blocking condition specifically rather than using a generic modal spinner.

### Result unknown

The user may navigate away if tracking remains safe, but the object must retain a recoverable `confirming result` state until reconciled.

---

## 6. Success-feedback contract

Canonical principle:

> **Success is the default; exceptions are information.**

### Quiet success is preferred when

- the new row/timeline event appears immediately;
- the invoking view returns to the expected source context;
- there is no ambiguity between local and remote completion.

### Explicit success wording is justified when

- the user needs to distinguish `saved locally` from `submitted`;
- a consequential formal command completed;
- navigation hides the resulting object and confirmation is otherwise unclear.

### Avoid

- permanent green `已同步` banners;
- snackbar/toast after every ordinary successful save;
- success animation that delays continued classroom work.

---

## 7. Error-placement contract

### Object-local error

Use near the affected row/control when only one object failed.

Examples:

- one Action failed to reschedule;
- one Quick Capture intent is pending/rejected;
- one attachment failed.

### Surface-level error

Use when the current page cannot fulfill its purpose.

Examples:

- Student list projection cannot load;
- organization management projection unavailable.

### App/session-level error

Use sparingly for global conditions.

Examples:

- session invalid;
- current organization unavailable;
- local encrypted store cannot be opened safely.

Do not promote a row-level retry into a full-screen error.

---

## 8. Quick Capture reference flow

### Known Student/subject, online

```text
Editing
→ local draft flush
→ Local draft safe
→ enqueue approved capture intent
→ Waiting to sync / Syncing
→ server accepts
→ committed fact appears
```

The common fast path may visually collapse intermediate states when they complete quickly, but it must never claim a stronger guarantee than has occurred.

### Offline

```text
Editing
→ local draft flush
→ Local draft safe
→ approved queueable intent recorded
→ Waiting to sync
```

Candidate user feedback:

- return to source context;
- affected record can show `待同步`;
- one restrained global indicator may show pending count.

### Server later rejects

```text
Waiting to sync
→ server authoritative rejection
→ keep recoverable local content/context
→ explain why + safe next action
```

Do not silently delete the captured observation merely because it could not be promoted to an accepted teaching fact.

Exact recovery/export/reassignment behavior is policy-dependent and remains Protocol Required.

---

## 9. Formal Case command reference flow

Example: `Close Case`.

### Successful path

```text
user chooses Close
→ consequence confirmation if required
→ Online command pending
→ authoritative committed result
→ Case state updates to Closed
```

### Timeout / unknown outcome

```text
user chooses Close
→ Online command pending
→ transport timeout
→ Result unknown
→ query/retry same operation identity
→ authoritative result recovered
```

Never represent `Reopen` as an Undo for this flow.

### Expected-version conflict

```text
user chooses Close
→ server reports current relation/version changed
→ Conflict
→ preserve safe local work
→ fetch latest projection
→ user reviews latest state
→ a new deliberate command may be created only after re-evaluation
```

---

## 10. Account / organization switch contract

Switching account, organization or environment is a security and data-lifecycle boundary, not merely navigation.

Required ordering conceptually:

1. stop ambiguous new submissions in the old scope;
2. flush/protect eligible old-scope Durable Intent according to policy;
3. end old-scope projection visibility as required;
4. establish new authenticated/authorized scope;
5. load/rebuild new-scope projections;
6. restore only drafts/intents belonging to that exact scope when policy allows.

Fail conditions:

- Organization A draft appears inside Organization B;
- a manager’s organization projection becomes their Personal Today;
- stale Student data remains visible after scope revocation;
- queued intent is silently reassigned to a different user/org.

---

## 11. Attachment-state contract

Attachment protocol is not fully frozen, but UX should distinguish at minimum:

- selected/staged locally;
- local staging failed;
- waiting to upload;
- uploading;
- upload failed/retryable;
- remotely committed/attached;
- attachment rejected by policy.

A photo/network failure must not destroy safely captured text.

Do not block local text capture on remote attachment upload unless the final protocol explicitly requires atomic remote submission.

---

## 12. Today and Action-state implications

Today remains explicit-Action-first.

Do not create duplicate Today rows merely because a Case is `pending_verification`.

For an Action being edited/submitted:

- local optimistic presentation may be used only where the command protocol allows it;
- authoritative online-only lifecycle changes must not disappear from Today as if committed before success is known;
- result-unknown state should keep enough object identity/context for reconciliation.

---

## 13. Organization-management implications

Management operations often have stronger authority/freshness requirements than teaching capture.

Examples such as disabling membership, role/scope changes and handoff should default to authoritative online command semantics unless architecture explicitly proves queueing safe.

UI rules:

- disabled/hidden buttons are discoverability decisions, not authorization enforcement;
- when a command is unavailable, explain the missing precondition where useful and safe;
- never imply that manager visibility makes the manager the responsible teacher;
- destructive/formal operations explain consequence and target identity.

---

## 14. Accessibility and interaction requirements

Every non-normal state must remain understandable without hue alone.

Windows:

- state text participates correctly in High Contrast;
- retry/confirm-result controls are keyboard reachable;
- focus returns to a meaningful object after transient UI closes;
- progress indicators do not steal focus unnecessarily.

Android:

- TalkBack announces meaningful state changes without reading noisy technical detail;
- pending/offline state is not encoded only by a small icon;
- snackbar is not the only place a persistent failure exists;
- predictive Back must preserve protected draft semantics.

---

## 15. Telemetry / diagnostics boundary

User-facing UX must not expose secrets, raw tokens, database paths, cryptographic material, internal SQL/HTTP errors or operation identifiers as ordinary copy.

Diagnostics may retain safe correlation identifiers according to the diagnostics-redaction policy, but the teacher-facing message should describe:

- what is known;
- what is not known;
- whether their work is safe;
- what they can do next.

---

## 16. Prototype fixtures required by this contract

Native prototypes must include deterministic examples of:

1. local draft safe but not submitted;
2. queueable intent waiting to sync;
3. queueable intent later rejected by server;
4. online formal command pending;
5. online formal command with unknown result;
6. expected-version/current-relation conflict;
7. local save failure while editor content remains visible;
8. projection refresh/rebuild while Durable Intent survives;
9. permission reduced while protected draft exists;
10. session invalid / membership disabled;
11. account or organization switch with scope-isolated drafts;
12. attachment staged locally while upload fails;
13. offline access allowed vs offline access no longer authorized once the Offline Lease protocol is defined.

These are semantic fixtures, not decorative mock states.

---

## 17. Acceptance questions for every write interaction

Before a native write interaction is considered designed, reviewers must be able to answer:

1. Which operation class is this: A draft, B queueable intent, C authoritative online command, or D projection refresh?
2. What guarantee exists when the UI says `saved`, `waiting`, `submitted` or `complete`?
3. Can the user navigate away safely at each intermediate state?
4. What survives process death?
5. What happens if network disappears before/after local durability?
6. What happens if server outcome is unknown?
7. What happens if current version/relation changed?
8. What happens if permission/session/scope changed?
9. Is any local work preserved without leaking it into the wrong user/org scope?
10. What is the next safe user action, and is that action expressed without technical storage jargon?

If these cannot be answered, the interaction is not ready for pixel polish.

---

## 18. Candidate boundaries still intentionally open

Do not freeze from this document alone:

- Offline Access Lease duration/countdown/recovery UX;
- exact retention/export behavior for drafts that become unauthorized;
- attachment upload atomicity and expiry UX;
- which additional teaching commands become safely queueable after protocol proof;
- exact snackbar/banner/icon copy and animation timings;
- background sync scheduling wording.

Those require architecture/protocol evidence and native prototypes.

---

## 19. Acceptance criterion

This contract can move from Candidate to accepted v1 only when Windows and Android prototypes prove that the same semantic operation states remain understandable across:

- online/offline transitions;
- process/lifecycle interruption;
- authorization/scope changes;
- conflicts/unknown results;
- Light/Dark/High Contrast or platform accessibility modes;
- keyboard/IME/predictive-back interaction;
- real local durability behavior.

The goal is not to expose the synchronization architecture to teachers. The goal is to ensure the interface never lies about whether their work is safe, queued, authoritative, rejected, conflicted or no longer authorized.