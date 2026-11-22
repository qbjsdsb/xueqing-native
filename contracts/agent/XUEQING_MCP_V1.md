# Xueqing Local MCP Surface v1 (`XUEQING_MCP_V1`)

Status: **Active V1 candidate contract**

## Purpose

Expose a narrow, auditable Model Context Protocol (`stdio`) interface so AI agents can smoothly inspect authorized teaching context and draft classroom observations without bypassing teacher responsibility or desktop security boundaries.

## Authority & Safety Invariants

1. **Same auth boundary as native clients**: Production mode loads the installed `deployment_profile.json`, restores the user's encrypted Refresh Token from the OS keyring (`WindowsDpapiRefreshTokenVault` or `LinuxSecretServiceRefreshTokenVault`), and executes all remote reads through `Postgrest*` adapters with RLS and `IdentityLink`.
2. **Draft-Only Write Policy (`read_local_observation_draft_and_server_study_plan_draft_only`)**:
   - Allowed: bounded projection reads, encrypted local observation draft read/write (with `expected_epoch` optimistic protection), server-side study plan draft creation (`save_study_plan_draft_v1`, strictly `status = 'draft'`), and canonical `xueqing://` deep-link generation.
   - Forbidden: direct SQL access, `service_role` keys, or silent server-side consequential writes (`create_observation`, `create_learning_case`, action progression, case lifecycle, organization invitations, or overwriting `human_confirmed` papers).
   - Multi-subject disambiguation: when a teacher has multiple authorized `TeachingContexts` for the same student and omits `subjectProfileId`, production tools fail closed with `XQ_SUBJECT_DISAMBIGUATION_REQUIRED` and return candidate contexts instead of guessing.
3. **Human handoff**: After drafting an observation with `xueqing_save_observation_draft`, the agent returns the canonical deep link from `xueqing_build_deep_link` so the teacher inspects and explicitly submits it in the native Windows/Linux client.
4. **Prototype mode (`--prototype`)**: Uses deterministic synthetic fixtures (`MainWindowViewModel` prototype mode) plus an isolated encrypted local draft database so CI and local MCP clients can verify the full protocol without production credentials or real student data.

## Tool Surface (12 Tools)

| Tool | Read-Only | Purpose |
| :--- | :---: | :--- |
| `xueqing_status` | Yes | Mode (`prototype`/`production`), OS platform, session status, draft store readiness (no secrets). |
| `xueqing_list_students` | Yes | Search and paginate students (`query`, `page`, `pageSize` <= 50). |
| `xueqing_get_today_actions` | Yes | Return explicit Today action queue with bucket, due label, and interaction state. |
| `xueqing_get_student_context` | Yes | Return teaching contexts, current focus, and recent observations for one student. |
| `xueqing_get_student_cases` | Yes | Return Learning Cases (state, responsibility, next action, due date, timeline when available). |
| `xueqing_get_observation_draft` | Yes | Open/read encrypted local observation draft and current `epoch` for `(organizationId, studentId, subjectId)`. |
| `xueqing_save_observation_draft` | No (local draft only) | Save observation draft text into encrypted `SqliteObservationDraftStore` using optimistic `expectedEpoch`. |
| `xueqing_list_student_papers` | Yes | List photographed exam/homework paper submissions ordered by `paperDate` descending. |
| `xueqing_get_paper_detail` | Yes | Retrieve structured mistake items, root causes, section positions, and page references for one paper. |
| `xueqing_get_student_longitudinal_metrics` | Yes | Retrieve deterministic longitudinal KPIs, weekly cadence, root-cause shifts, recurring topics, and recent study plan drafts. |
| `xueqing_save_study_plan_draft` | No (draft only) | Save an AI stage brief and candidate Learning Case / Next Action drafts (`status='draft'`, never mutates authoritative Cases) and return canonical `xueqing://` deep link. |
| `xueqing_build_deep_link` | Yes | Validate and format canonical `xueqing://today`, `xueqing://student/<uuid>`, or `xueqing://learning/<uuid>`. |
