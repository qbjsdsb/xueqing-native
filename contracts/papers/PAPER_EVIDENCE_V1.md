# Paper Evidence & Longitudinal Analysis v1

Status: **Phase 2 backend contract**

## Purpose

Turns multi-page photographed student exam papers and homework sheets into an auditable, time-ordered longitudinal evidence stream without violating `docs/product/PRODUCT.md` authority boundaries:

- **Original pages** live in the existing private `teaching-attachments-v1` bucket via `public.observation_attachments` (zero change to Attachment v1 upload/commit safety).
- **Single-paper structured extraction** (from teacher marks or AI vision) is stored as an explicit draft (`ai_draft`) until a teacher/guardian confirms it (`human_confirmed`). AI extraction **never** overwrites an already-confirmed paper.
- **Unmarked paper grading boundary**: `grading_source = 'ai_tentative_unmarked'` is strictly excluded from authoritative score averages (`teacher_marked_avg_score_ratio`) until human-verified (`human_verified`).
- **Deterministic longitudinal metrics** (weekly cadence, volume, late-section fatigue ratio, root-cause evolution across recent 30d vs prior window, and recurring topic leaderboard) are computed directly in PostgreSQL so AI assistants never hallucinate counts or trends.
- **Study plan drafts** (`public.study_plan_drafts`) store AI-generated longitudinal briefs and candidate next actions with paper evidence references. They never silently create or mutate `learning_cases` or `learning_case_actions`.

## Tables (RLS enabled, zero direct client CRUD)

1. `public.paper_submissions`: Business container for one paper (`paper_date`, `paper_kind`, `grading_source`, `confirmation_state`, counts, score, duration, `version`).
2. `public.paper_pages`: Ordered (`1..50`) mapping from a `paper_submission` to committed `observation_attachments`, recording `header_redacted` (whether the student name/school header was masked before cloud vision extraction).
3. `public.paper_mistake_items`: Atomic structured mistakes (`page_index`, `question_label`, `topic_tags[]`, `error_root_cause`, `paper_section_position`, `points_lost`, `corrected_on_paper`, `summary`, `provenance`, optional `linked_case_id`).
4. `public.study_plan_drafts`: AI-assisted stage briefs and candidate action drafts (`brief_markdown`, `evidence_refs`, `proposed_actions`, `window_days`, `paper_count_analyzed`, `status`, `version`).

## Versioned RPC Surface (`authenticated` only, `SECURITY DEFINER`, `search_path = ''`)

1. `public.create_paper_submission_v1(...)`: Idempotent creation of a paper submission + ordered pages under live Teaching Fact Gate (`FOR SHARE`).
2. `public.save_paper_extraction_v1(...)`: Optimistic-locked (`expected_version`) save of either `'ai_draft'` (blocked if already `'human_confirmed'`) or `'human_confirm'` structured counts, score, summary, and mistake items.
3. `public.get_student_paper_longitudinal_v1(org_id, student_id, subject_profile_id, window_days, limit)`: Deterministic longitudinal projection returning `summary`, `weekly_cadence`, `root_cause_breakdown`, `recurring_topics`, `papers` (with pages + mistakes), and `recent_study_plan_drafts`.
4. `public.save_study_plan_draft_v1(...)`: Idempotent persistence of an AI stage brief + candidate action draft (strictly `status = 'draft'`).
5. `public.update_study_plan_draft_status_v1(...)`: Optimistic-locked transition of a study plan draft to `'partially_adopted' | 'fully_adopted' | 'dismissed'`.

## Error Codes

- `XQ_OPERATION_ID_REQUIRED`
- `XQ_TEACHING_CONTEXT_REQUIRED`
- `XQ_AUTH_REQUIRED` / `XQ_ACTOR_NOT_FOUND` / `XQ_ACTOR_DISABLED`
- `XQ_MEMBERSHIP_REQUIRED` / `XQ_MEMBERSHIP_DISABLED` / `XQ_TEACHING_CAPABILITY_REQUIRED`
- `XQ_TEACHING_CONTEXT_UNAVAILABLE`
- `XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD`
- `XQ_INVALID_PAPER_DATE` / `XQ_INVALID_PAPER_KIND` / `XQ_INVALID_PAPER_TITLE` / `XQ_INVALID_PAPER_PAGES`
- `XQ_PAPER_ATTACHMENT_SCOPE_MISMATCH` / `XQ_PAPER_OBSERVATION_SCOPE_MISMATCH`
- `XQ_PAPER_NOT_FOUND` / `XQ_VERSION_CONFLICT` / `XQ_PAPER_ALREADY_CONFIRMED`
- `XQ_INVALID_EXTRACTION_MODE` / `XQ_INVALID_GRADING_SOURCE` / `XQ_INVALID_PAPER_METRICS` / `XQ_INVALID_MISTAKE_ITEMS`
- `XQ_LINKED_CASE_SCOPE_MISMATCH`
- `XQ_INVALID_STUDY_PLAN_DRAFT` / `XQ_STUDY_PLAN_DRAFT_NOT_FOUND` / `XQ_INVALID_STUDY_PLAN_STATUS`
