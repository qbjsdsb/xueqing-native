# Xueqing Native Core Screen Architecture v1 Candidate

Status: **Candidate — Prototype Required**

This document defines the information architecture and native interaction responsibilities of the core Xueqing screens. It intentionally avoids freezing final pixel values or breakpoint numbers.

Upstream product/domain meaning is authoritative. This document focuses on **what each screen must help the user accomplish, what belongs on it, what does not belong on it, and how Windows/Android should differ without changing semantics**.

---

## 1. Navigation model

### Personal teaching workspace

Primary destinations:

- `Today`
- `Students`
- `Learning`

This remains the default context for a user who teaches, including an organization owner/admin who also teaches.

### Organization workspace

Secondary workspace for users with organization authority:

- `Organization Learning`
- `Organization Management`

Do not merge personal and organization queues into one Today surface.

### Platform expression

#### Windows

Use a stable desktop `NavigationView`/shell with a small top-level module set.

Personal and organization spaces should be visibly distinct, but organization authority should not visually dominate a teacher’s daily personal work.

#### Android

Keep bottom/top-level navigation small. Organization administration is secondary and should be entered from a secondary location such as account/more/organization context rather than becoming a fourth bottom-navigation destination.

---

## 2. Today

### User question

> What explicit teaching action should I do now or schedule next?

### Hard semantic boundary

Today contains **explicit pending Actions**.

It does not automatically include:

- every open Case;
- every `pending_verification` Case;
- every Evidence/Intervention/Assessment fact;
- every Quick Capture record that has no explicit Action.

One Action belongs to one semantic bucket only.

### Candidate information order

1. overdue;
2. due today;
3. undated / needs scheduling;
4. future, lower visual priority and usually collapsed by default.

Same-student items may cluster to reduce repetition, but independent Actions remain independently actionable.

### Windows

Default shape:

- work queue first;
- compact row actions;
- mouse hover and keyboard focus distinct;
- completion/edit actions should normally be possible without losing queue context;
- returning from Student/Case restores selection, focus and scroll position.

A narrow/large-text window may move date/status/actions below the title instead of forcing one-line rows.

### Android

Default shape:

- single-column work queue;
- primary tap targets remain reachable one-handed;
- future actions stay lower in the page hierarchy;
- common action completion must not require organization navigation.

### Avoid

- KPI cards;
- dashboard charts;
- implicit Case-to-task duplication;
- giant colored status pills;
- a permanent large search bar if Today itself does not require one.

### Required states

- empty;
- loading without false empty;
- partially loaded with existing content retained where safe;
- save in progress;
- save failed with retry;
- offline/pending sync;
- read-only/permission changed;
- long Chinese titles;
- many Actions;
- no due date;
- future Actions;
- closed Case no longer contributing a pending primary Action.

---

## 3. Students collection

### User question

> Which student do I need to understand or work on?

### Information priority per row

1. name;
2. minimum subject/grade context needed to disambiguate;
3. at most one small piece of current attention context when useful.

Avoid decorative avatars, chip walls and multiple metrics.

### Windows

Students is a high-leverage `list/detail` workspace.

The collection side should support:

- search;
- fast keyboard selection;
- virtualized large lists;
- mouse wheel;
- stable selected state;
- optional lightweight filtering when justified.

The detail side should update without resetting the list position.

The exact side-by-side threshold is **prototype-derived**, not frozen here. If either side becomes unusable, the UI must reflow to stacked navigation before compressing meaningful content into ellipses.

### Android

Use a single-column student list on compact windows and platform-adaptive list/detail on larger windows where appropriate.

Do not squeeze a desktop table into a phone layout.

---

## 4. Student Detail

### User question

> What matters most for this student now, and what happened recently?

### Default information order

1. minimal identity + subject context;
2. `Current` — at most three important open issues;
3. explicit next action where applicable;
4. recent records/facts;
5. full Case list on demand;
6. older history on demand.

If there are more than three open issues, the default view remains selective and offers `View all` rather than turning the detail page into an archive browser.

### Windows

The detail pane is a **work surface**, not a profile dashboard.

Object title sizing may be tighter than a full standalone page title.

At narrow widths or high text scale:

- identity metadata may stack;
- Case rows may stack state/date below the title;
- the detail pane may become a full-page/stacked destination.

### Android

Single vertical reading order:

- student identity;
- current issues;
- recent records;
- progressive links to all issues/history.

No desktop-style property grid.

### Avoid

- student health/risk/growth score;
- large KPI strip;
- one Card per issue;
- showing every historical Case by default;
- forcing grade/subject/name into one non-wrapping line.

---

## 5. Learning Case

### User question

> What is this problem, what has been tried, what evidence do we have, and what is the next teaching action?

### Core visual model

A Case is a **traceable professional narrative**.

Top area:

- Case title;
- current state;
- responsible teacher where relevant;
- explicit next action.

Main body:

- chronological facts/events;
- Evidence, Intervention, Assessment/Verification and lifecycle events remain semantically distinct;
- provenance and historical actor meaning remain visible where needed;
- history is not silently rewritten.

### Example reading rhythm

```text
Case title
State
Responsible teacher
Next action

Today
Verification
…

Sep 14
Intervention
…

Sep 12
Classroom observation
…
```

Do not convert each semantic fact type into a separate colored card family.

### Windows

Use a comfortable reading column with a prototype-tested maximum width. It must be `max-width`, not a fixed width.

Extra width may remain whitespace or host genuinely useful supporting context. Do not stretch the narrative across the full width merely to fill the window.

### Android

Single-column reading surface with short native actions. Keep the current state and next action easy to reach without turning the top into a property wall.

### Lifecycle actions

Formal commands such as stabilize/close/reopen remain explicit domain actions.

`Reopen` must appear as a new controlled event/state change, not an Undo that erases closure history.

### Avoid

- Jira/CRM property sheet as the primary visual metaphor;
- giant editing form;
- color-coded card stack;
- auto-stable after a passed assessment;
- hidden provenance;
- generic `Undo close` semantics.

---

## 6. Quick Capture

### User question

> What did I just notice, and can I record it before the teaching moment is gone?

### Default common flow

If Student/subject context is already known:

```text
Student · Subject

What did you notice?
[ text ]

Optional note / photo

Save
```

If started globally, add a lightweight inline Student/subject selector rather than forcing a separate full-screen selection flow when that is unnecessary.

### Android — primary implementation target

Use a **full-screen short task** on compact phones rather than treating the core writing experience as a transient bottom sheet.

Reasons:

- Chinese IME;
- long-ish classroom notes;
- photo picker/camera;
- background/foreground transitions;
- process-death recovery;
- accessibility text scaling;
- predictive back.

The content must be scrollable under constrained height/large text/IME. The save action should remain reachable but must not be an absolute fixed bar that covers focused content.

Back should preserve a durably saved local draft where the Draft Engine allows it. Re-entry should make the draft recoverable.

### Windows

Quick Capture can be a compact keyboard-efficient task layer or context action, but must preserve the same business semantics.

### Avoid

- forcing taxonomy/root cause/severity/owner/date/attachment/final Case classification on the common first capture step;
- repeated student selection when context is already known;
- modal discard prompts on every Back when safe draft persistence exists;
- blocking local save on photo upload/sync.

---

## 7. Organization Learning

### User question

> Where does organization supervision need attention?

### Default shape

An **exception/attention queue**, not a KPI dashboard.

Candidate row composition:

```text
Student · Subject
concrete attention fact
responsible teacher
```

Filters may include teacher, subject, state or time where they genuinely support supervision.

### Windows

Use dense, scan-friendly rows with list/detail where useful. Open the relevant Student/Case context without losing the supervision list position.

### Android

Secondary/lightweight management only. Do not reproduce the full desktop supervision surface unless a future mobile use case proves it necessary.

### Hard boundary

UI does not invent `risk` or `needs intervention` labels from absence of data or from arbitrary presentation logic.

Attention conditions must come from an explicit product/domain rule.

---

## 8. Organization Management

### User question

> How do I safely manage members, responsibilities, invitations and organization teaching operations?

### Windows

This is intentionally denser than teaching-reading screens.

Prefer row/table/list structures with:

- keyboard navigation;
- multi-select only where operations are actually bulk-safe;
- discoverable primary actions;
- right-click/context menus only as accelerators, never the only path;
- focused dialogs/flyouts for short tasks;
- explicit destructive/formal commands.

### Column priority model — candidate

Wide:

```text
Name | Role | Status | Recent activity | Actions
```

Medium:

```text
Name | Role | Status | Actions
```

Narrow:

```text
Name
Role · Status                         Actions
```

Narrower still:

- stacked row/detail navigation;
- do not compress every column into ellipsis.

Exact collapse points require native prototype evidence.

### Android

Convert dense table semantics into simple vertical rows/detail/sheets where mobile administration is supported.

Do not squeeze the Windows table into a phone width.

---

## 9. Navigation and context preservation

### Windows

Core journeys should preserve:

- selected Student;
- selected Case where appropriate;
- list scroll position;
- keyboard focus target;
- source context when returning from a detail or dialog.

Closing a dialog should return focus to the invoking object whenever practical.

Entering and leaving Case detail should not unexpectedly reset the Student list.

### Android

Back/predictive-back should return to the expected source context.

Quick Capture launched from Student context should return to that Student context after save/back rather than dropping the user at an unrelated root screen.

Draft recovery must not accidentally cross Personal/Organization scope boundaries.

---

## 10. Search

Search should exist where object-finding is a real task.

Strong candidates:

- Students;
- Organization Management;
- larger supervision collections.

Do not place a permanent giant Search Bar on every page merely for visual consistency.

Windows keyboard search should not steal Chinese IME composition input.

---

## 11. Failure and conflict surfaces

Do not collapse all sync/concurrency problems into `Sync conflict`.

Distinguish at least:

### Read projection is stale

Safe response may be to refresh/rebuild the disposable read model.

### Local user intent exists

Draft/Outbox user work must remain preserved according to the Durable Intent contract.

### Formal command version changed

The server-authoritative expected-version path decides legality. UI should explain that the object changed and preserve the user’s safe local work rather than asking the teacher to choose between arbitrary `local` and `cloud` database versions.

### Permission reduced

Do not disguise authorization changes as ordinary empty state.

If local draft work exists, explain that submission may no longer be allowed while preserving it according to policy.

---

## 12. Representative native prototype pages

The v1 UX Foundation should not be considered accepted until executable prototypes validate at least these five representative surfaces:

1. Today;
2. Student collection + Student Detail;
3. Learning Case;
4. Android Quick Capture;
5. Organization Management.

Organization Learning semantics must also be covered in documentation and may reuse validated collection/detail patterns when its product rules are ready.

The concrete acceptance matrix lives in `docs/ux/PROTOTYPE_ACCEPTANCE_MATRIX.md`.
