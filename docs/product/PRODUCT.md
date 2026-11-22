# Product Definition

## One sentence

Xueqing is a teaching judgment and action workspace that helps teachers and tutoring organizations turn observations into traceable intervention/verification loops with an explicit next action.

## Core questions

1. What does this student most need to solve now?
2. What should the responsible teacher do next?
3. What evidence shows whether the previous action worked?

## Core loop

```text
Student
→ Subject Profile
→ discover/record problem
→ Learning Case
→ Evidence
→ Intervention
→ Assessment / Verification
→ Next Action
→ Stable
→ Closed
```

`reopen` is a controlled command/event for a genuine recurrence after closure, not a seventh Case state.

## Primary users

- teacher: owns personal teaching responsibility and fast classroom capture;
- organization owner: may also be a teacher, but organization supervision and personal teaching remain separate projections;
- organization admin: manages organization teaching operations without automatically owning teaching responsibility.

## Phase 2 Longitudinal Paper Evidence Loop

Exam papers and homework sheets feed into the core loop as structured longitudinal evidence without bypassing human teaching authority:

```text
Multi-page Paper Photo (Android, optional header redaction)
→ Paper Submission + Private Attachments
→ AI Structured Mistake Draft (single-paper extraction, store=false)
→ Human Review & Confirmation (locks confirmed facts)
→ Deterministic Longitudinal Metrics (cadence, volume, root-cause shifts, recurring topics)
→ AI Stage Brief & Study Plan Draft (never mutates Cases silently)
→ Teacher Explicit Adoption into Learning Case / Next Action
```

## Platform responsibility

- **Android = Capture**: Today, quick capture, multi-page exam/homework photos with on-device header redaction, students, current focus, next action.
- **Desktop (Windows & Linux native) = Organize + Think**: list/detail workspaces, timeline, longitudinal paper trends and confirmation, study plan draft adoption, search/filter, bulk-safe administration, responsibility overview, and organization management.
- **Local AI Agent Surface (`Xueqing.Mcp`) = Read + Draft Assistant**: read-only authorized teaching/longitudinal projections, encrypted local observation drafts, server-side study plan drafts (`status = 'draft'`), and `xueqing://` handoff links back to the native desktop client for teacher review and explicit action.

## Non-goals for the initial product

Do not expand the core into tuition/billing, enrollment CRM, full timetable scheduling, finance, large question bank, parent/student standalone apps, generalized document workspace or autonomous AI diagnosis.

AI assists with single-paper extraction drafts, deterministic-metric longitudinal summarization, and candidate study plan drafts, but may never silently create formal diagnoses, mutate Learning Cases or Next Actions, overwrite human-confirmed records, or bypass teacher responsibility.

## Product quality gates

- data correctness before visual polish;
- authorization before convenience;
- no lost teacher input;
- important actions recoverable after timeout/network interruption;
- history and responsibility remain explainable;
- Android classroom operations are low-friction;
- Windows workflows are keyboard/mouse efficient and accessible;
- failure states are visible and actionable.