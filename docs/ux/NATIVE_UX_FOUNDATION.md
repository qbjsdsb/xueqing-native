# Xueqing Native UX Foundation v1 Candidate

Status: **Candidate — Prototype Required**

This document translates the accepted product and architecture boundaries into a shared native UX and visual contract for Windows and Android.

It is **not** a pixel specification and does not freeze unproven breakpoint values, pane widths, exact reading widths, animation timings, or platform token overrides. Those values require executable WinUI 3 / Jetpack Compose prototype evidence before acceptance.

Authoritative upstream semantics remain in:

- `docs/product/PRODUCT.md`
- `docs/architecture/DOMAIN_MODEL.md`
- `docs/architecture/COMMANDS.md`
- `docs/architecture/DRAFT_ENGINE.md`
- `docs/architecture/LOCAL_STATE_MODEL.md`
- `docs/ux/WINDOWS_UX.md`
- `docs/ux/ANDROID_UX.md`

If this document conflicts with a domain or security invariant, the invariant wins.

---

## 1. Product interaction thesis

Xueqing is a **teaching judgment and action workspace**, not a dashboard, CRM, spreadsheet replacement, generic task manager, issue tracker, autonomous AI diagnosis surface, or document database.

The UI should help a teacher answer three questions quickly:

1. What matters most for this student now?
2. What should I do next?
3. What evidence shows whether the previous action worked?

The core loop remains:

```text
Student
→ Subject Profile
→ discover / record problem
→ Learning Case
→ Evidence
→ Intervention
→ Assessment / Verification
→ Next Action
→ Stable
→ Closed
```

`reopen` remains a controlled command/event after a genuine recurrence. It must never be presented as a generic Undo for `closed`.

---

## 2. One product language, two native work modes

The business meaning of a Student, Case, Action, Evidence, Intervention, Assessment, state, permission, draft and conflict must remain consistent across platforms.

The interaction model does **not** need to look identical.

### Windows = Organize + Think

Primary qualities:

- list/detail workspaces;
- sustained reading and comparison;
- keyboard and mouse efficiency;
- deep Case history;
- search and filtering;
- organization supervision;
- dense but controlled administration;
- explicit hover, selected and keyboard-focus states.

Windows must not be a stretched phone UI.

### Android = Capture

Primary qualities:

- Today;
- recent students;
- one-handed entry;
- Quick Capture;
- evidence/photo entry;
- current Case focus;
- next action;
- resilient drafting across interruption, process death and network failure.

Android must not reproduce Windows tables or multi-column management density.

---

## 3. Personal and organization workspaces stay separate

A user may hold organization authority and still be a classroom teacher. Permission and current task context are different concepts.

### Personal teaching workspace

Default context for any user who teaches:

- Today
- Students
- Learning

It answers:

> What do **I** need to understand or do next in my teaching responsibility?

### Organization workspace

Secondary context for users with organization authority:

- Organization Learning
- Organization Management

It answers:

> Where does the organization need attention, supervision or administration?

Organization supervision must not silently imply responsible-teacher ownership.

On Android, organization administration remains secondary and must not become a fourth bottom-navigation destination.

---

## 4. Information before containers

Visual hierarchy should be created in this order:

1. typography;
2. alignment;
3. whitespace;
4. grouping rhythm;
5. light dividers or system surfaces where needed;
6. color for action/state;
7. containers only when an independent boundary is actually useful.

Canonical principle:

> **Use typography to establish relationships, whitespace to establish hierarchy, color to express action and state, and containers only when a real boundary is required.**

Default list rows, Today actions, Student Case rows and Case timeline events should remain flat information surfaces rather than card-per-row UI.

---

## 5. Content order beats decorative hierarchy

The default reading order for teaching surfaces is:

```text
current focus
→ explicit next action
→ recent facts supporting the judgment
→ necessary history
```

The UI should not insert decorative KPI, profile-score or generic summary panels between those layers.

### Student Detail

Default state:

1. minimal student identity and subject context;
2. `Current` — at most three important open issues;
3. explicit next action where applicable;
4. recent facts / records;
5. older history on demand;
6. full Case list on demand.

### Learning Case

Default state:

1. Case title;
2. current state;
3. responsible teacher where relevant;
4. explicit next action;
5. chronological professional narrative.

The Case should read like a traceable professional record, not a ticket property sheet.

---

## 6. Today is a work queue, not a dashboard

Today contains **explicit pending Actions**, not every unresolved Case and not every `pending_verification` state.

A Case state must not automatically create a second task representation.

Candidate visual priority:

1. overdue;
2. due today;
3. undated / needs scheduling;
4. future actions, lower visual priority and usually collapsed by default.

The same Action appears in exactly one semantic bucket.

Same-student Actions should be clustered when that reduces repetition without hiding independent actions.

Do not add KPI cards such as total students, active Cases, completion rate or risk score to Personal Today.

---

## 7. Organization Learning is exception-first, not KPI-first

Organization Learning should answer:

> Where does someone with supervision authority need to look?

Useful rows are concrete, attributable facts, for example:

- a pending Action is overdue by N days;
- a Case has remained in a product-defined attention condition;
- a product-defined follow-up interval has elapsed;
- a teaching responsibility or workflow requires explicit supervision.

The UI must not invent `risk`, `health`, `growth score`, `AI insight` or similar evaluative concepts without a real product/domain rule.

Counts may exist as compact filters or summaries, but not as the primary dashboard metaphor.

---

## 8. Quick Capture is progressive, not a compressed Case form

The common Android capture flow should aim for roughly 10–20 seconds when the student/subject context is already known.

Default interaction:

```text
known student / subject context
→ “What did you notice?”
→ optional note / photo
→ Save
```

Do not require root cause, taxonomy, owner, severity, final Evidence type, Action date or formal lifecycle decisions during common classroom capture unless the product invariant truly requires them at that moment.

If Capture starts inside a Student/Case context, do not force the teacher to select the same student or subject again.

Formal organization, Case linkage and deeper judgment can happen later, especially on Windows.

---

## 9. Draft reliability changes interaction design

Teacher input is more important than navigation chrome.

The Draft Engine contract requires preservation across:

- navigation;
- backgrounding;
- process death;
- network failure;
- attachment operations;
- Chinese IME composition behavior.

Therefore:

- ordinary Back should not repeatedly trigger “discard changes?” dialogs when the draft can be durably preserved;
- an explicit `Discard draft` command is the destructive act;
- lifecycle/background transitions should not lose input;
- returning to a saved local draft should make the recovery state understandable;
- Quick Capture success must not be shown before required durable local persistence actually completed.

---

## 10. Normal success is quiet; exceptions speak

Default successful operations should avoid noisy feedback.

Prefer:

- the row updates;
- the new timeline fact appears;
- the button briefly changes state;
- the user returns to the expected context.

Reserve persistent or prominent feedback for:

- save failure;
- offline/pending-sync states;
- conflict/version changes;
- permission changes;
- destructive or formal lifecycle actions;
- locally saved but not yet formally committed work.

Examples:

- `Save failed · Retry`
- `Offline · 3 records waiting to sync`
- `This Case changed elsewhere. Your local draft is still safe.`

Do not fill normal screens with green `Synced` or repeated `Saved successfully` banners.

---

## 11. Undo and formal domain commands are different

Use Undo for lightweight, genuinely reversible UI actions where appropriate.

Do not present formal domain transitions as cosmetic Undo operations.

Examples requiring the real command lifecycle include:

- confirm/transition a formal Case;
- stabilize;
- close;
- reopen;
- teacher reassignment/handoff;
- membership lifecycle changes;
- student governance/merge.

`Reopen` is not `Undo close`.

Confirmation dialogs should be reserved for consequential or destructive actions and must explain the consequence rather than ask a generic `Are you sure?` question.

---

## 12. Shared semantic typography, platform-native mapping

Freeze the **roles**, not one global pixel scale.

Shared roles:

- Page Title
- Object Title
- Section Title
- Item Title
- Body
- Metadata
- State
- Primary Action
- Secondary Action
- Danger / Failure

### Windows starting mapping — prototype required

Candidate defaults only:

- Page Title: native Windows title scale, approximately 28/36 where the full page needs it;
- Object Title inside a detail pane: may use a tighter title/subtitle role;
- Body: native Windows body scale, approximately 14/20;
- Metadata: approximately 12/16 where the information is truly secondary.

Important business states must not be demoted to tiny/light metadata simply because they appear beside a date.

At constrained width or high text scale, the semantic hierarchy remains but exact title sizing may compact.

### Android starting mapping — prototype required

Candidate defaults only:

- top/object title: Material title role, around 22/28 where appropriate;
- primary reading body: around 16/24;
- compact secondary body/metadata: Material body/label roles as appropriate.

Font scale must be tested at the platform’s large-text extremes rather than assuming an `sp` mapping is automatically safe.

---

## 13. Platform-native geometry

### Windows

Use WinUI/Windows geometry as the default rather than importing Flutter/Material SaaS geometry.

Candidate native direction:

- page-level controls: around 4 DIP corner radius where the platform control does so;
- transient flyouts/dialogs: around 8 DIP where the platform control does so;
- structural rows, timeline entries and adjacent pane edges: often 0 custom radius;
- avoid inventing a 12/16/24 DIP card-radius system for ordinary information.

Prefer native `NavigationView`, list selection visuals, focus visuals, dialogs, menus and theme resources over hand-built imitations.

### Android

Material 3 shape scale is allowed, but ordinary information should not become a large-rounded-card system.

Working UI should mostly use restrained 4/8/12 dp geometry; larger radii are mainly for real modal/transient surfaces such as sheet/dialog/FAB where platform conventions support them.

---

## 14. Color and theming

### Shared rule

State is content first; color is redundant reinforcement.

Always write the state:

- `Overdue`
- `Pending verification`
- `Stable`
- `Closed`
- `Save failed`
- `Local draft`

Do not encode the only meaning in hue.

### Windows

Prefer WinUI theme resources for canvas, surfaces, text, selection, focus and High Contrast behavior.

A restrained Xueqing deep-teal family may provide product identity and limited action emphasis, but it must not override platform accessibility behavior.

High Contrast system semantics take priority over brand colors.

### Android

Use Material `ColorScheme` and system light/dark behavior.

Dynamic wallpaper color must not redefine business semantics such as overdue, failure, pending verification or local draft.

Dynamic color can be evaluated later as an optional preference rather than a requirement for v1.

---

## 15. Dark mode

Dark mode should remain visually continuous and quiet.

Avoid assigning a different dark surface to every:

- AppBar;
- body;
- list;
- detail pane;
- card;
- bottom navigation.

Use surface changes only when they communicate a real layer such as input, selection, modal/sheet or an independent work surface.

Do not use dark-mode color intensity as decoration.

---

## 16. High Contrast / accessibility behavior

Accessibility is part of the native design, not a later skin.

### Windows

Validate:

- system High Contrast themes;
- theme-resource lookup rather than hard-coded foreground/background overrides;
- keyboard-only core journeys;
- visible focus;
- 100/150/200% DPI;
- text scaling up through the practical platform maximum, including a destructive 225% pass;
- Narrator-friendly names for meaningful controls.

High Contrast may legitimately add stronger structural borders that Light/Dark do not need.

### Android

Validate:

- up to 200% font scale;
- TalkBack semantics;
- 48 dp minimum touch target for interactive regions;
- system dark mode;
- edge-to-edge/insets;
- predictive back;
- IME visibility and focus;
- phone/foldable/tablet/desktop-windowing size changes.

A 24 dp icon may live inside a 48 dp touch target; the icon itself does not need to become 48 dp.

---

## 17. Layout adaptation rules

Do not freeze one device-name breakpoint table.

The preferred rule is:

> **Reflow the layout before degrading the content.**

Examples:

- list/detail becomes stacked before the list and detail are squeezed into unusable widths;
- metadata/action can move below a title before the title is truncated;
- management columns disappear or merge by priority before every column becomes ellipsis;
- long Chinese text wraps naturally;
- key rows have `min-height`, not rigid heights;
- Case reading width has a prototype-tested `max-width`, not a fixed width.

Windows pages must use one authoritative layout source/service rather than inventing unrelated local breakpoint sources.

---

## 18. Interaction-state model

Distinct states must remain distinguishable:

- default;
- hover (Windows);
- selected;
- keyboard focus;
- pressed;
- disabled;
- loading;
- saving;
- save failed;
- offline/pending sync;
- conflict/version changed;
- permission reduced;
- locally preserved draft.

Do not make hover, selected and focus all look like the same accent fill.

Loading should preserve useful existing structure when possible rather than flashing the entire workspace blank.

Failure must state the next safe action.

---

## 19. Motion and haptics

Motion exists to preserve spatial/context continuity or explain state change.

Use motion for:

- entering/leaving a detail context;
- pane transitions;
- predictive back;
- subtle state changes.

Do not add decorative logo animation, cascading card entrances, glowing gradients, springy dashboards or AI-like sparkle effects.

Android haptics should be reserved for useful platform moments such as long-press/gesture thresholds/rejection, not every successful save.

---

## 20. Native iconography

Do not force one cross-platform SVG icon family.

- Windows: Fluent/WinUI-native icon resources where suitable.
- Android: Material-native icon resources where suitable.

Product consistency comes from meaning and placement, not identical vector outlines.

---

## 21. Explicit anti-patterns

Reject by default:

- card per row;
- card inside card;
- giant rounded surfaces for ordinary information;
- broad blue-purple gradients;
- glassmorphism/content blur;
- neon/glow;
- AI sparkles/wands/robots;
- decorative avatars for every student/member;
- chip walls for ordinary metadata;
- repeated explanatory subtitles that restate the page title;
- giant KPI dashboards;
- risk/growth/health scores without a real domain rule;
- command palettes added before command volume justifies them;
- context-menu-only critical actions;
- fixed-height rows that clip large text;
- fixed pane widths treated as universal truth;
- color as the only state encoding;
- `local vs cloud — choose one` conflict dialogs that ask teachers to become database administrators.

---

## 22. Candidate items that still require native prototype evidence

Do **not** freeze the following from this document alone:

- exact Windows compact/regular/wide thresholds;
- exact Student list preferred/minimum width;
- exact Case reading `max-width`;
- exact pane transition timing;
- exact Android phone/tablet pane thresholds beyond platform window-size/adaptive primitives;
- exact organization-management column-collapse points;
- exact Android Quick Capture bottom-action composition under every IME/window case;
- final accent hex values;
- final semantic state color pairs;
- final font-weight overrides beyond platform-native defaults.

These are accepted only after the executable prototype gates in `PROTOTYPE_ACCEPTANCE_MATRIX.md` pass.
