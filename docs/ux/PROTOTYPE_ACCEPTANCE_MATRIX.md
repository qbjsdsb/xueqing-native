# Xueqing Native Prototype Acceptance Matrix v1 Candidate

Status: **Candidate — executable evidence required**

This document defines the minimum evidence required before `NATIVE_UX_FOUNDATION.md`, `CORE_SCREEN_ARCHITECTURE.md` and `INTERACTION_STATE_CONTRACT.md` can move from Candidate to an accepted v1 UX/Visual Foundation.

The goal is not screenshot polish. The goal is to prove that the core design survives real native layout, input, accessibility and failure conditions without losing teacher work or violating product semantics.

All fixtures must be fictional and deterministic.

---

## 1. Scope and sequencing

### Phase A — documentation candidate

Allowed:

- UX documentation;
- deterministic fictional fixture definitions;
- acceptance-test planning.

Not allowed in the documentation PR:

- production business UI changes;
- backend/provider changes;
- real data;
- weakening existing architecture/security gates.

### Phase B — isolated Windows UX prototype

Start only from a current stable `main` after in-flight infrastructure work that touches the real app shell/package composition is reconciled.

Prototype surfaces:

1. Today;
2. Students + Student Detail;
3. Learning Case;
4. Organization Management.

Use fictional in-memory/local fixtures only. Do not require production backend behavior to validate layout/navigation/accessibility.

### Phase C — isolated Android architecture + UX prototype

Prototype surfaces:

1. Today;
2. Students;
3. Quick Capture;
4. minimal Case reading context as needed to validate navigation.

Validate Compose architecture, state restoration, IME and process-death behavior early.

### Phase D — Foundation acceptance

Only after executable evidence passes may candidate values such as final breakpoints, pane minimums, Case reading width and management column-collapse thresholds be frozen.

---

## 2. Global pass/fail rules

A prototype is **failed** if any representative core journey requires one of the following workarounds:

- shrinking critical text below platform-native accessible roles;
- clipping unique/important text;
- hiding the only state meaning in color;
- forcing horizontal scrolling for ordinary teaching understanding;
- squeezing list/detail panes into unreadable widths instead of reflowing;
- losing typed teacher input during ordinary OS/app lifecycle behavior;
- resetting list selection/scroll/focus without product reason;
- requiring mouse-only access to a critical Windows action;
- requiring a context menu as the only path to a critical action;
- covering the focused Android input with IME/system bars;
- presenting stale read-model refresh as permission to destroy Durable Intent;
- asking the teacher to resolve low-level `local vs cloud` storage conflicts;
- turning normal information rows into card soup to rescue an unclear hierarchy;
- showing a stronger write guarantee than the architecture has actually established;
- converting an authoritative command timeout into a brand-new duplicate intent;
- restoring one account/organization draft into another scope;
- representing a permission/session loss as a harmless generic empty state.

A prototype may still pass with changed visual composition between widths/scales if the semantic priority and actionability remain intact.

---

## 3. Windows viewport matrix

Validate the representative Windows surfaces at these **candidate test widths**:

- 800 DIP;
- 960 DIP;
- 1024 DIP;
- 1280 DIP;
- 1600 DIP.

Also include at least one deliberately short-window pass so vertical assumptions are tested.

These are test points, **not frozen product breakpoints**.

### What the points are intended to expose

#### 800

- list/detail must not be forced side-by-side merely because desktop is available;
- actions/metadata must reflow;
- no critical title/action clipping.

#### 960

- transition pressure near compact/regular layouts;
- pane minimum-width evidence.

#### 1024

- first realistic point where some list/detail candidates may become viable;
- must still prove both sides have enough usable width.

#### 1280

- representative desktop workspace;
- compare with the current architecture-spike historical breakpoint without treating it as automatically final.

#### 1600

- prove content does not stretch into unreadable lines;
- prove supporting whitespace/context is handled intentionally.

---

## 4. Windows DPI / text / theme matrix

### DPI

Minimum:

- 100%;
- 150%;
- 200%.

### Text scaling

Minimum practical passes:

- normal;
- 150%;
- 200%;
- destructive pass up to 225% where supported.

225% need not preserve the exact compact composition of 100%, but must preserve core task completion and critical information.

### Theme

- Light;
- Dark;
- at least one Windows High Contrast theme.

### Fail conditions

- hard-coded colors make selected/focus/error unreadable in High Contrast;
- fixed-height rows clip text;
- buttons overlap text;
- unique Case/Action titles disappear behind ellipsis when reflow was possible;
- page/object title consumes so much space that the primary task becomes inaccessible;
- multiple custom dark surfaces create unnecessary visual layering.

---

## 5. Windows keyboard/focus matrix

Core journeys must be operable without a mouse.

Validate at least:

- `Tab` / `Shift+Tab` traversal;
- arrow-key collection movement where natural;
- `Enter` / `Space` activation according to control semantics;
- `Esc` closes transient UI only when safe;
- `F6` / pane traversal if used by the implementation;
- `Ctrl+F` search where supported;
- `Ctrl+N` new/capture where adopted;
- `F5` refresh where adopted;
- `Ctrl+S` only where a real explicit-save interaction exists and does not conflict with autosave semantics;
- shortcuts do not steal Chinese IME composition.

### Focus restoration

Explicitly test:

- open Student detail → return → focus/selection remains on the source Student when practical;
- open Case from Student → return → source context remains stable;
- open management dialog → close → focus returns to invoking row/control;
- error/retry does not throw focus to unrelated navigation chrome.

### Fail conditions

- keyboard focus is visually indistinguishable from selected state;
- closing a dialog sends focus to the navigation root instead of the invoking object;
- common keyboard navigation causes the detail pane to flash/reset unnecessarily;
- IME Enter/Esc is intercepted as an app-level action during active composition.

---

## 6. Today prototype fixture

Use a deliberately hostile fictional fixture, not a four-row beauty sample.

Minimum fixture characteristics:

- 20–30 pending Actions;
- at least two overdue Actions;
- multiple due-today Actions;
- at least two undated Actions;
- future Actions including a far-future date;
- one student with five Actions;
- one very long Chinese Action title;
- one saving Action;
- one save-failed Action with retry;
- one read-only/permission-changed row;
- Cases in `pending_verification` that do **not** have an extra explicit Action, proving they do not automatically appear as duplicate Today tasks.

### Pass requirements

- explicit Action is the queue unit;
- each Action is in one semantic bucket only;
- same-student clustering does not hide independent actions;
- future remains lower priority without losing discoverability;
- long titles wrap before critical action controls are destroyed;
- failure stays local to the affected action when possible;
- returning from Student/Case preserves source context.

---

## 7. Students + Student Detail fixture

Use at least 1,000 deterministic fictional Students in the Windows prototype.

Include:

- short Chinese names;
- long Chinese names;
- Chinese/Latin mixed names;
- multiple grades;
- multiple subjects;
- no active Case;
- one active Case;
- 20+ Cases;
- pending verification;
- stable;
- closed-only history;
- recently updated and long-idle examples.

### Collection pass requirements

- virtualization/large-list behavior remains smooth enough for the prototype gate;
- search remains usable;
- arrow-key selection is stable;
- selection/focus/hover remain distinct;
- rapid selection does not blank/reset the detail surface unnecessarily;
- list scroll position remains stable when inspecting detail.

### Detail pass requirements

- default view shows at most three current important issues;
- `View all` is progressive rather than default archive expansion;
- long names/subject metadata wrap safely;
- empty/open/closed-only states are distinguishable;
- the page does not become a student KPI/profile dashboard.

### List/detail adaptation pass

At every Windows test width/text scale:

- calculate whether both panes meet their minimum usable widths;
- if not, reflow/stack before content becomes unreadable;
- record the measured evidence that justifies the eventual product breakpoint.

Do not accept a breakpoint because a framework sample used that number.

---

## 8. Learning Case fixture

Use at least one deliberately long fictional Case:

- 100+ timeline facts/events;
- at least six months of history;
- Evidence;
- Intervention;
- Assessment/Verification;
- explicit Next Actions;
- stable transition;
- later verification;
- close event;
- genuine reopen event;
- long Chinese paragraph;
- attachment placeholder;
- one failed save/retry state;
- one historical actor who is no longer an active member.

### Pass requirements

- the Case reads as one professional chronological narrative;
- semantic fact types remain understandable without separate colored-card families;
- responsible teacher/current state/next action remain understandable under large text;
- historical provenance is not visually rewritten as current ownership;
- close and reopen remain distinct historical events;
- long content remains readable at narrow and wide windows;
- wide windows use a prototype-tested comfortable reading `max-width` rather than full-window line length;
- exact reading max-width remains Candidate until measured.

---

## 9. Organization Management fixture

Include enough fictional rows to validate dense administration and scrolling.

Represent:

- owner;
- admin;
- teacher;
- active;
- onboarding/pending state;
- disabled/revoked state;
- long Chinese display names;
- long organization/role metadata where applicable;
- rows with available actions and rows with permission-limited actions.

### Candidate column-priority behavior

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

Narrower:

- stacked list/detail.

### Pass requirements

- no strategy of compressing every column until all values are ellipses;
- keyboard-only core administration journey works;
- multi-select exists only for commands proven bulk-safe;
- context menu is an accelerator, not the only discovery path;
- destructive/formal lifecycle actions use explicit command UI;
- 200%+ text triggers earlier reflow rather than smaller text.

---

## 10. Android device/window matrix

Validate at least:

### Compact phone

- typical portrait phone;
- one intentionally small/short phone window;
- landscape pressure pass.

### Larger/adaptive windows

Candidate validation points inspired by common Android adaptive testing, without freezing device-class breakpoints:

- foldable-sized expanded window around 840 × 700 dp;
- small tablet around 1024 × 640 dp;
- larger tablet around 1280 × 800 dp;
- desktop/Chromebook-style windowing pass where CI/device lab capability exists.

The exact product layout mode should come from platform adaptive/window-size evidence rather than device-name detection.

---

## 11. Android font / input / accessibility matrix

Validate:

- normal font scale;
- large font scale;
- 200% font scale;
- Light;
- Dark;
- TalkBack names/roles for critical controls;
- minimum 48 dp interactive touch targets;
- edge-to-edge/system insets;
- Chinese IME;
- predictive back.

Do not use `sp` for layout padding/geometry simply because text uses `sp`.

---

## 12. Android Quick Capture lifecycle torture test

Quick Capture is a hard product gate.

Use a known Student/subject context and start typing a fictional classroom observation.

Test combinations of:

- active Chinese Pinyin composition;
- keyboard shown/hidden;
- 200% font scale;
- smallest supported height;
- rotate/resized window;
- fold/unfold where available;
- open photo picker/camera and return;
- background/foreground;
- temporary network loss;
- process death/restart;
- predictive-back gesture;
- explicit draft discard;
- locally saved pending-sync state.

### Pass requirements

- typed text is not lost during ordinary supported lifecycle behavior;
- known Student/subject context remains correct;
- staged attachment state remains explainable;
- content can scroll when IME + large text consume vertical space;
- focused input can be brought into view;
- save remains reachable without covering content;
- ordinary Back can preserve a durable draft rather than requiring repeated discard confirmation;
- explicit discard is clearly destructive;
- successful local save is not reported before required durable persistence completed;
- photo upload/sync does not block safe local text capture when the architecture allows queued submission.

Any ordinary lifecycle path that destroys typed teacher work is an automatic failure.

---

## 13. Android Today / Student pass requirements

### Today

- one-handed common interactions;
- long Action titles wrap;
- bottom navigation remains stable;
- `Record` is an action, not a fourth destination;
- future work remains lower priority;
- offline/pending-sync state does not turn normal Today into a warning dashboard.

### Student

- compact phone uses vertical navigation;
- larger windows may adopt platform-adaptive list/detail;
- Student context remains understandable after rotation/resize;
- returning from Quick Capture returns to expected source context;
- touch targets remain safe without visually inflating every icon/label.

---

## 14. Dark-mode visual gate

Fail the prototype if Dark mode uses surface changes merely to decorate hierarchy.

Check specifically for:

- separate dark shade for every AppBar/list/detail/card/nav surface;
- over-bright semantic colors;
- selected + hover + focus collapsing into one accent block;
- Card-per-row introduced only because low-contrast spacing feels weak.

Preferred direction:

- continuous base surface;
- real surface changes for input/selection/modal tasks;
- typography and spacing carry most hierarchy.

---

## 15. Windows High Contrast gate

Check:

- navigation/content structure remains understandable;
- selected item remains identifiable;
- keyboard focus remains obvious;
- dialog/flyout boundaries remain visible;
- state text remains meaningful with brand hue removed;
- system theme resources are not defeated by hard-coded foreground/background values.

Stronger borders in Contrast mode are acceptable and often desirable even when Light/Dark remain border-light.

---

## 16. Error / offline / permission / conflict fixture

Every representative prototype must include at least one meaningful exception state.

Validate:

### Loading

Loading is not rendered as false empty state.

### Save failed

- input remains;
- retry is local and obvious;
- the UI does not pretend completion happened.

### Offline / pending sync

- local-safe work is distinguished from formally committed remote state where necessary;
- normal synced state does not need permanent success decoration.

### Read model stale

- refresh/rebuild may update disposable projection state;
- Durable Intent remains safe.

### Expected-version/formal command conflict

- UI explains that the object changed;
- server-authoritative command semantics remain authoritative;
- safe local work is preserved;
- no raw technical error such as `409` is shown as the primary explanation.

### Permission reduced

- do not masquerade as generic empty state;
- do not leak unauthorized data;
- existing protected local draft behavior follows policy rather than silently deleting teacher work.

---

## 17. Interaction-state semantic gate

The prototype must prove the semantics defined in `INTERACTION_STATE_CONTRACT.md`. A layout prototype that cannot express these states is not ready for v1 freeze.

Required deterministic state fixtures:

1. editing with latest text not yet durably flushed;
2. protected local draft durably safe but not submitted;
3. approved low-risk intent durably queued and waiting to sync;
4. queued intent actively syncing;
5. remotely committed success;
6. queueable intent later authoritatively rejected;
7. authoritative online command pending;
8. authoritative command transport timeout with **unknown result**;
9. same-operation result reconciliation without duplicate intent;
10. expected-version/current-relation conflict;
11. local persistence failure while visible editor content remains;
12. projection refresh/rebuild while Durable Intent survives;
13. permission reduced/revoked while a protected draft exists;
14. session invalid / membership disabled;
15. account or organization switch with strict scope isolation;
16. attachment staged locally while remote upload fails;
17. offline-access-allowed vs offline-access-no-longer-authorized once the Offline Access Lease protocol is accepted.

### Pass requirements

- generic `保存成功` is not used where only local durability or queueing is known;
- `本机草稿` / `待同步` / authoritative committed state remain semantically distinct when the distinction matters;
- result-unknown state never becomes a new duplicate submission action;
- the user is never asked to resolve storage internals as `本地版本 vs 云端版本`;
- safe local work survives projection refresh and ordinary lifecycle behavior;
- protected work never crosses user/organization/environment scope;
- permission/session loss does not remain hidden behind stale authorized UI;
- formal online-only commands do not pretend to succeed while offline;
- primary error copy says what is known, whether work is safe, and the next safe action without exposing raw HTTP/SQL/crypto details;
- one underlying incident does not create stacked redundant banners/toasts.

### Navigation/back requirements

- ordinary Back may leave a locally protected draft when durability is proven;
- explicit discard is the destructive action;
- leaving an in-flight authoritative command must not imply cancellation if none exists;
- returning to a result-unknown command restores reconciliation state;
- organization/account switch cannot restore a previous-scope draft into the new scope.

---

## 18. Cloud evidence strategy

Because this project uses CI as the development lab, prototype acceptance must not rely on one local developer screenshot.

### Windows evidence layers

Prefer multiple complementary layers:

1. deterministic Core/ViewModel/layout-policy tests for semantic decisions and boundary calculations;
2. WinUI build + UI Automation/Appium-style interaction tests where stable enough for keyboard/navigation/focus flows;
3. representative rendered screenshots captured as CI artifacts for human visual review;
4. accessibility/UI Automation inspection evidence for names, roles and focus paths;
5. exact-head manual review notes for behavior that cloud automation cannot yet prove reliably.

No single Windows UI automation framework is treated as the sole visual truth.

### Android evidence layers

Prefer:

1. Compose unit/semantics tests;
2. Compose screenshot tests across representative `uiMode` / font-scale / size configurations;
3. device/emulator tests for IME, predictive Back, state restoration and process death;
4. baseline-profile/macrobenchmark evidence when the architecture spike reaches the performance gate;
5. CI screenshot/report artifacts attached to the exact tested head.

Golden screenshots supplement semantic/layout assertions; they do not justify freezing a breakpoint by themselves.

---

## 19. Evidence to capture in CI / review

Each native UX prototype PR should provide reproducible evidence, not only screenshots.

Recommended evidence:

- exact head SHA;
- build/test result;
- deterministic fixture version;
- viewport/text/theme matrix result;
- interaction-state semantic matrix result;
- automated layout/semantics checks where feasible;
- screenshots or test artifacts for representative matrices;
- keyboard/focus test evidence on Windows;
- unknown-result/conflict/scope-switch evidence for representative writes;
- macrobenchmark/baseline-profile evidence for Android startup/Today/Student/Quick Capture when the architecture spike reaches that gate;
- explicit known limitations rather than hiding untested states.

Prototype acceptance applies only to the exact tested head.

---

## 20. Conditions to freeze UX & Visual Foundation v1

The Candidate may move to accepted v1 only when:

1. the Windows representative prototype passes the required width, DPI/text, Light/Dark/High Contrast and keyboard/focus gates;
2. the Android representative prototype passes phone/adaptive, 200% text, IME, predictive-back, lifecycle and accessibility gates;
3. Today semantics remain explicit-Action-first;
4. Student detail remains current-focus-first rather than archive/dashboard-first;
5. Case remains a readable professional narrative;
6. Quick Capture proves no-lost-input behavior under the supported lifecycle matrix;
7. Organization Management proves density can degrade by priority without unreadable compression;
8. representative write flows pass `INTERACTION_STATE_CONTRACT.md`, including local-vs-remote guarantee wording, unknown-result reconciliation, authorization/scope changes and Durable Intent survival;
9. no prototype requires card soup, giant KPI dashboards or AI-like decorative patterns to remain understandable;
10. measured evidence justifies any frozen breakpoint/pane-width/reading-width values;
11. documentation is updated with the tested values and the exact evidence before the word `Candidate` is removed.
