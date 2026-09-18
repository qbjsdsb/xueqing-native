# Xueqing Native UX Foundation v2 — Native Reference RFC

Status: **RFC — executable native reference prototypes required**

This document records the current redesign direction for Xueqing Native. It is intentionally a design RFC, not an accepted replacement for domain, security, sync, authorization, or interaction-state contracts.

The purpose is to prevent future UI work from drifting back toward generic education SaaS, CRM/dashboard patterns, cross-platform visual imitation, or decorative “AI product” styling.

If this RFC conflicts with product/domain/security contracts, those contracts win.

Authoritative upstream semantics remain in:

- `docs/product/PRODUCT.md`
- `docs/architecture/DOMAIN_MODEL.md`
- `docs/architecture/COMMANDS.md`
- `docs/architecture/DRAFT_ENGINE.md`
- `docs/architecture/LOCAL_STATE_MODEL.md`
- `docs/architecture/AUTHORIZATION.md`
- `docs/ux/INTERACTION_STATE_CONTRACT.md`

---

## 1. Design thesis

Xueqing has **one domain system and two native applications**.

> **Share semantics, not pixels.**

Windows and Android must agree on:

- Student;
- Subject Profile;
- Learning Case;
- Observation / Evidence;
- Intervention;
- Assessment / Verification;
- Next Action;
- responsibility;
- lifecycle state;
- local draft / queued / committed distinctions;
- authorization and scope boundaries.

They do **not** need to share:

- navigation geometry;
- toolbar placement;
- pane structure;
- radius;
- surface treatment;
- accent behavior;
- interaction affordances;
- system integration;
- motion.

Platform conventions come before product conventions when the business meaning is unchanged.

---

## 2. Product mental model

Xueqing is not primarily a profile viewer, dashboard, CRM, ticket system, generic task manager, gradebook, or AI insight feed.

The teacher should be able to answer:

1. **What matters now?**
2. **What should happen next?**
3. **What evidence explains the judgment?**

The common reading model is therefore:

```text
Focus
→ Action
→ Evidence / Chronicle
```

This is a product reading order, not a database table order.

Avoid organizing primary teaching surfaces around tabs such as:

- Overview;
- Properties;
- Metrics;
- Archive;
- AI insights.

Those may exist only when a real workflow proves they are needed.

---

## 3. Shared spatial roles

The two clients may implement different layouts, but the same responsibilities recur.

### Collection / Roster

Purpose:

- choose the student or work item;
- preserve selection and source context;
- expose only enough information to make the choice.

Do not turn the collection into a mini profile dashboard.

### Primary Work / Chronicle

Purpose:

- show the current teaching focus;
- show the explicit next action;
- show the recent evidence and professional chronology.

This is the main work surface.

### Supporting Context / Inspector

Purpose:

- expose lower-frequency identity, responsibility, relationship, metadata, or linked-resource information.

It is **optional** by default and should not permanently consume width merely because a wide monitor exists.

### Capture

Purpose:

- preserve a classroom observation before the moment is lost.

Capture starts simple and reveals secondary fields progressively.

---

## 4. Xueqing visual signature: Evidence Thread

The one product-specific visual idea intentionally carried across platforms is the **Evidence Thread**.

It visualizes the actual teaching loop rather than a decorative brand motif.

Example:

```text
Sep 17    ● Observation
          │
Sep 14    ● Intervention
          │
Sep 10    ● Verification
          │
          ○ Next Action
```

Rules:

- historical fact nodes remain visually quieter than the current focus;
- future action is not visually equivalent to an already-recorded fact;
- Observation / Intervention / Verification should usually be distinguished by wording and/or platform-native glyphs, not by a permanent rainbow of colors;
- semantic color is reserved for real state meaning such as overdue, rejection, conflict, or current selection;
- the thread never invents chronology or causal meaning the domain model does not contain.

---

# 5. Windows — Teaching Desk

Windows is **Organize + Think**.

The experience should feel like a native Windows productivity application, not a web administration panel rendered in WinUI.

## 5.1 Shell

Candidate composition:

```text
TitleBar
├─ App identity
├─ Workspace switch
├─ search when justified
└─ account

Compact NavigationView
├─ Today
├─ Students
└─ Learning

Main workspace
├─ Roster / collection when relevant
├─ Chronicle / primary work
└─ optional Inspector
```

### Account and workspace are different concepts

Account answers:

> Who am I?

Workspace answers:

> In which responsibility scope am I working?

Destination answers:

> What kind of work am I doing?

Do not collapse these into one overloaded profile/menu control.

Candidate workspace examples:

- Personal Teaching;
- Organization A;
- Organization B.

Personal teaching remains the default for a user who teaches, even if the same person also has organization authority.

### Organization navigation

Personal teaching and organization supervision must not appear as one long permanent navigation menu.

Switching workspace may replace the active destination set.

Candidate personal destinations:

- Today;
- Students;
- Learning.

Candidate organization destinations:

- Learning Supervision;
- Students & Assignments;
- Members & Permissions.

Exact wording remains prototype-reviewable.

---

## 5.2 Navigation rail

Prefer a compact native `NavigationView` / rail-like desktop shell when it preserves enough discoverability.

Goals:

- navigation consumes little horizontal space;
- labels remain discoverable through native expanded state / tooltips;
- window-width adaptation uses platform behavior rather than hand-built duplicate navigation systems;
- the shell must still work at the project’s validated narrow and high-text-scale sizes.

Do not freeze rail width or breakpoint values from mockups.

---

## 5.3 Roster

Windows Students is a high-frequency collection.

Each row should primarily answer:

- who is this;
- why might I select them now;
- when was the relevant recent activity, if useful.

Candidate row:

```text
林晨
作文迁移 · 待验证                  9/17
```

Avoid by default:

- decorative student avatars;
- multiple chip/badge rows;
- repeated metadata that does not help selection;
- risk/health/growth scores;
- card-per-student UI.

Hover may reveal accelerators such as:

- Record;
- More.

Critical actions must still have discoverable non-hover paths.

---

## 5.4 Chronicle

The selected Student or Case should read as one continuous professional work surface rather than a stack of dashboard cards.

Candidate reading rhythm:

```text
林晨
九年级 · 语文

正在关注

作文论据选择仍不稳定               待验证
最近验证 · 9 月17日

文言实词辨析容易混淆               跟进中
最近记录 · 9 月14日

下一步

○ 用陌生材料重新验证论据选择
  9 月20日                    完成   改期

教学脉络

9 月17日   ● 课堂观察
           │ …
9 月14日   ● 干预
           │ …
9 月10日   ● 验证
           │ …
           ○ 下一步
```

The surface should avoid:

- profile hero areas;
- KPI strips;
- property walls;
- three identical large rounded cards for Current / Next / Recent;
- permanent decorative color blocks.

Use typography, alignment, separators, native selection, and the Evidence Thread for structure.

---

## 5.5 Inspector

Supporting context is **on demand**.

Examples:

- student canonical metadata;
- teaching responsibility;
- Case ownership/state metadata;
- linked material information.

The default 1280-class experience should not be forced into a permanent third/fourth pane if that makes the primary work weaker.

The Inspector may appear as:

- an optional side pane;
- a details pane;
- a Flyout for short contextual information.

Its exact form is prototype-derived.

---

## 5.6 Windows-native interaction

Native value should come from real desktop behavior.

First-class candidates:

- keyboard selection in Roster/Today;
- visible keyboard focus;
- mouse hover accelerators;
- context menus as accelerators, never the only critical path;
- compact sizing only where density improves work;
- drag/drop from Explorer for future approved evidence/material flows;
- Mica/base-layer use for app chrome where appropriate;
- Acrylic/system transient surfaces for Flyouts/menus;
- High Contrast and Windows theme resources.

Small keyboard set candidate:

- `Ctrl+F` — search current collection;
- `Ctrl+N` — Quick Capture / new observation where context permits;
- `F5` — refresh;
- arrows — collection movement;
- `Enter` — open/activate current object;
- `Esc` — close transient/Inspector or return to source context.

Do not bind dangerous domain deletion to casual desktop shortcuts.

Advanced accelerators such as Space Peek or a command palette are later options, not Reference Prototype requirements.

---

## 5.7 Windows system integration — later layer

Promising later capabilities:

- taskbar Jump List for non-sensitive global tasks such as Today / Quick Capture;
- app notifications that deep-link back to the correct safe context;
- protocol activation;
- Explorer drag/drop for approved attachment/evidence flows;
- optional multi-window after state/authorization ownership is proven.

Privacy rule:

> OS-level surfaces must not expose student identity/content by default.

These integrations are not required to accept the first Reference Prototype.

---

# 6. Android — Teaching Pocket

Android is **Capture + immediate follow-up**.

The experience should feel like a native contemporary Android application rather than a compressed desktop client or a generic education dashboard.

Primary modes:

- **Agenda** — what should I do now;
- **Roster** — which student should I open;
- **Chronicle** — what matters for this student;
- **Capture** — what did I just observe.

---

## 6.1 Android shell

Compact-phone top-level destinations remain small:

- Today;
- Students;
- Learning.

Record is an **action**, not a fourth destination.

Use platform-native:

- edge-to-edge;
- Material 3 app bars;
- NavigationBar;
- NavigationRail on larger windows where adaptive guidance chooses it;
- FAB for the single dominant capture action where justified;
- predictive back;
- system dark mode;
- touch-safe target sizes.

Do not reproduce the Windows compact table/list density on a phone.

---

## 6.2 Agenda

Today is an agenda, not a dashboard.

Reading order:

1. overdue;
2. today;
3. needs scheduling;
4. future lower priority.

Candidate shape:

```text
今日
9 月18日 · 周五

已逾期

林晨
作文迁移验证
昨天                              >

王宇
文言实词复查
9 月16日                          >

今天

李欣
病句辨析验证
今天                              >

                           ✎ 记录
```

Avoid:

- welcome copy;
- motivational slogans;
- KPI cards;
- large decorative illustrations;
- permanent filter-chip walls.

---

## 6.3 Android Roster

Phone collection remains simple and touch-first.

Candidate row:

```text
林晨
九年级 · 语文
作文迁移 · 待验证                 >
```

Do not require student photos for visual completeness.

Use search when object-finding is real. Secondary filters should normally move into platform-native sheets/dialogs rather than permanently occupy the top of the phone screen.

---

## 6.4 Android Chronicle

Phone Student detail enters the work directly.

Avoid profile-dashboard tabs such as Overview / Records / Data unless a real workflow proves them necessary.

Candidate order:

```text
← 林晨
  九年级 · 语文

正在关注
…

下一步
…

教学脉络
● Observation
│
● Intervention
│
● Verification

                         ✎ 记录
```

The content logic aligns with Windows, but the interaction remains touch-first and vertically composed.

---

## 6.5 Quick Capture

Quick Capture is a short full-screen task on compact phones.

Initial surface should be almost empty:

```text
← 快速记录

林晨 · 语文

你刚才观察到了什么？



本机草稿 · 刚刚

照片              更多

提交记录
```

Rules:

- the input is the visual and interaction priority;
- known Student/subject context is inherited rather than reselected;
- tags/taxonomy/root cause/severity/date are not shown by default;
- secondary options appear progressively;
- Photo Picker / camera integration follows platform privacy expectations;
- Chinese IME must never be covered by a fixed action bar;
- ordinary back should preserve a proven durable draft instead of repeatedly asking whether to discard;
- wording must preserve the Interaction State Contract distinction between local draft, queued intent and remote commit.

---

## 6.6 Android-native lifecycle

Reference UX should explicitly exploit, not merely survive:

- predictive back;
- edge-to-edge;
- IME insets;
- Activity recreation;
- process-death recovery;
- phone/tablet/window resizing.

A draft-safe predictive-back flow is a signature native experience:

```text
editing
→ durable local draft
→ predictive back to source context
→ later re-entry restores the protected draft
```

The UI must never imply remote submission merely because local durability succeeded.

---

## 6.7 Android adaptive layout

Think in window roles rather than device labels.

Compact:

```text
Roster
  → Chronicle
```

Expanded:

```text
NavigationRail | Roster | Chronicle
```

Larger layouts may add a genuinely useful supporting pane only when the primary content remains readable.

Prefer stable Material adaptive libraries and proven platform behavior over custom breakpoint forests.

---

## 6.8 Android system integration — later layer

Promising later capabilities:

- launcher shortcut: Quick Capture / Today;
- privacy-safe home widget;
- Photo Picker;
- share target for approved material flows;
- notifications that return to the correct context;
- restrained semantic haptics.

Privacy rule:

> launcher, widget, lock-screen and notification surfaces must not reveal student identity/content by default.

These are follow-up productivity layers, not Reference Prototype acceptance blockers.

---

# 7. Visual language

## 7.1 No custom cross-platform component language

Do not invent a parallel “Xueqing UI kit” for common primitives when the platform already supplies a good native one.

Prefer:

Windows:

- WinUI typography;
- system brushes;
- NavigationView;
- native selection/focus;
- Button/MenuFlyout/CommandBar patterns;
- system materials.

Android:

- Material 3 semantic color roles;
- platform app bars;
- NavigationBar/Rail;
- FAB;
- BottomSheet;
- platform text fields/editors;
- native motion and back behavior.

Product-specific UI should exist only where the teaching model genuinely requires it.

The Evidence Thread is the primary candidate.

---

## 7.2 Color

Normal teaching content is platform-neutral.

Use color for:

- selection;
- current action emphasis;
- overdue/error;
- unknown/rejected/conflict states;
- permission/session exceptions when appropriate.

Do not permanently assign a bright color family to every record type.

Do not require one fixed brand accent to dominate both platforms.

---

## 7.3 Typography

Student names and page headings are work-object titles, not marketing heroes.

Use restrained platform-native type ramps.

Avoid oversized headings that force useful teaching content below the fold.

---

## 7.4 Containers

Avoid Card Soup.

A container is justified when it represents a real independent interaction boundary such as:

- editor;
- modal/flyout task;
- selected/focused work unit;
- action object where touch/mouse affordance benefits from a surface.

Do not wrap every list row, Case fact, timeline event or metadata group in a separate rounded card.

---

# 8. Explicit anti-patterns

The Reference Prototype fails directionally if it depends on:

- dashboard KPI cards;
- radar charts / growth scores;
- invented “risk” labels;
- AI insight panels;
- motivational copy such as “看见每一个学生”;
- generic praise such as “思考有深度 / 表达更清晰” when no concrete teaching fact requires it;
- avatar walls;
- gradient-heavy brand surfaces;
- chip walls;
- status-pill walls;
- permanent right-side profile property panels;
- Windows that resembles a stretched phone app;
- Android that resembles a compressed Windows table;
- organization and personal teaching queues mixed together;
- visual success claims stronger than proven local/remote state.

---

# 9. Reference Prototype sequence

Do **not** redesign the entire product in one PR.

## Reference A — Windows Students / Chronicle

Validate:

- native TitleBar/app shell;
- account vs workspace distinction;
- compact NavigationView;
- Roster selection;
- continuous Chronicle;
- Evidence Thread;
- optional Inspector;
- hover/focus/keyboard states;
- return-context preservation;
- existing authoritative PersonalBootstrap + recent Observation state where available.

Required environment matrix remains governed by `PROTOTYPE_ACCEPTANCE_MATRIX.md`.

No backend/domain expansion belongs in this prototype.

## Reference B — Android Student / Quick Capture

Validate:

- edge-to-edge;
- native top app bar/navigation;
- Roster → Chronicle navigation;
- FAB/contextual capture entry;
- full-screen Quick Capture;
- durable draft wording;
- Chinese IME;
- predictive back;
- process-death restoration;
- source-context return;
- compact and expanded-window behavior.

Keep the existing Draft/Outbox/WorkManager/server contracts unchanged.

---

# 10. Migration principle

This RFC does not justify deleting known-good UX behavior merely because visual composition changes.

Preserve executable evidence for:

- Windows width matrix;
- Windows keyboard/focus;
- Windows Dark/High Contrast/text scale;
- Android Draft durability;
- Android encrypted Durable Intent;
- Android queued Observation flow;
- authoritative Windows Student/recent Observation projection;
- authorization and provider boundaries.

A new reference UI must re-pass affected UX gates before the previous layout can be removed.

---

# 11. Acceptance questions

The redesign is ready to expand only when the Reference Prototypes answer yes to these questions.

### Windows

Can a teacher inspect many students in sequence without losing list position, selection or keyboard flow?

Can common actions happen near the current object without permanent button clutter?

Does a wide screen improve work rather than merely create larger empty/card areas?

Does the UI still feel native in narrow windows, Dark, High Contrast and large text?

### Android

Can a teacher capture a classroom observation in roughly 10–20 seconds when context is known?

Can predictive back and process interruption occur without losing protected text?

Does the phone remain one-handed and touch-safe without turning every surface into an oversized card?

Does the same state adapt naturally to a larger window without duplicating business logic?

### Teaching semantics

Can the teacher identify within seconds:

- what matters now;
- what happens next;
- which recent facts support the judgment?

### Responsibility semantics

Can an owner/admin who also teaches clearly distinguish personal teaching responsibility from organization supervision?

---

# 12. Non-goals of this RFC

This RFC does not:

- change the domain model;
- change Teaching Fact Gate;
- change authorization;
- change sync semantics;
- define production provider/region;
- define Offline Access Lease;
- add AI product features;
- add new backend tables or commands;
- freeze final breakpoints/pane widths;
- require Jump Lists/widgets/share targets in the first prototype;
- declare existing v1 candidate documents obsolete before executable evidence exists.

The next durable step is to build the two Reference Prototypes and update this RFC from measured evidence rather than screenshots alone.
