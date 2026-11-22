# Paper Vision Extraction Edge Function v1 (`extract-paper-v1`)

Status: **Phase 2 cloud extraction boundary**

## Purpose

Runs single-paper multimodal extraction once per photographed exam/homework paper via the Gemini Interactions API (`POST /v1/interactions`) and persists a structured **draft** (`p_mode = 'ai_draft'`) into `public.paper_submissions` + `public.paper_mistake_items` via `public.save_paper_extraction_v1`.

## Security & Authority Invariants

1. **Zero `service_role` bypass**: The Edge Function never instantiates a service-role Supabase client. Every read (`get_paper_submission_v1`), private Storage image fetch (`/storage/v1/object/authenticated/teaching-attachments-v1/...`), and write (`save_paper_extraction_v1`) runs strictly under the caller's `Authorization: Bearer <user_jwt>`.
2. **Idempotent Retry Short-Circuit**: `get_paper_submission_v1` checks `public.operation_receipts` for `(operation_id, actor_app_user_id, 'save_paper_extraction_v1')` before downloading images or invoking Gemini. Retrying a previously committed `operation_id` returns HTTP `200` with `"idempotent_replay": true` and zero duplicate Gemini calls.
3. **Hosted Region Guard**: Every hosted non-preflight request verifies `SB_REGION === XUEQING_REQUIRED_EDGE_REGION` (default `ap-southeast-1`) before auth, body, DB, Storage, or Gemini processing, failing closed with HTTP `503 XQ_PAPER_EXTRACTION_REGION_UNAVAILABLE` on drift. Only explicit `XUEQING_LOCAL_REFERENCE_MODE=1` disables this for local CI/dev.
4. **Human-Confirmed Immutability**: If the target paper is already `confirmation_state = 'human_confirmed'`, the function fails closed with HTTP `409 XQ_PAPER_ALREADY_CONFIRMED` **before** calling Gemini, and `save_paper_extraction_v1` enforces the same guard in PostgreSQL.
5. **Minors Privacy & Zero Server Retention**:
   - Prompts carry only anonymous context (`subject_key`, `paper_kind`, `paper_date`, `page_count`, `redacted_page_indices`, optional anonymous title) — never `student_display_name` or `organization_name`.
   - Gemini Interactions API calls always send `"store": false` so exam images and extraction text are not stored for server-side conversation history.
6. **Unmarked Grading Boundary**: Output validation strictly allows `grading_source` in `{'teacher_marked', 'ai_tentative_unmarked'}` — never `'human_verified'`.
7. **Zero Side Effects on `learning_cases`**: Failures (upstream timeout, HTTP error, malformed JSON) fail closed without modifying the paper, original images, observations, or learning cases.

## Request (`POST /functions/v1/extract-paper-v1`)

Headers:
- `Authorization: Bearer <user_jwt>` (required)
- `apikey: <publishable_key>` (required)
- `Content-Type: application/json`

Body:
```json
{
  "operation_id": "<uuid>",
  "organization_id": "<uuid>",
  "student_id": "<uuid>",
  "subject_profile_id": "<uuid>",
  "paper_submission_id": "<uuid>",
  "expected_version": 1
}
```

## Success Response (`200 OK`)

```json
{
  "ok": true,
  "contract": "extract_paper_v1",
  "operation_id": "<uuid>",
  "paper_submission_id": "<uuid>",
  "model": "gemini-4-hydrogen",
  "pages_analyzed": 2,
  "confirmation_state": "draft_ready",
  "grading_source": "teacher_marked",
  "version": 2,
  "mistake_items_saved": 2,
  "server_committed_at": "2026-11-22T04:00:00Z"
}
```

## Configuration Environment Variables

- `GEMINI_API_KEY` (secret, required)
- `XUEQING_GEMINI_MODEL` (optional, default `gemini-4-hydrogen`)
- `XUEQING_REQUIRED_EDGE_REGION` (optional, default `ap-southeast-1`)
- `XUEQING_LOCAL_REFERENCE_MODE` (`1` only for local mock conformance testing)
- `XUEQING_GEMINI_API_BASE_URL` (local testing override only; hosted mode strictly requires `https://generativelanguage.googleapis.com`)

## Error Codes

- `503 XQ_PAPER_EXTRACTION_REGION_UNAVAILABLE`
- `503 XQ_PAPER_EXTRACTION_UNAVAILABLE`
- `405 XQ_METHOD_NOT_ALLOWED`
- `401 XQ_AUTH_REQUIRED`
- `400 XQ_PAPER_EXTRACTION_INPUT_INVALID`
- `403 XQ_TEACHING_CONTEXT_UNAVAILABLE`
- `404 XQ_PAPER_NOT_FOUND` / `XQ_PAPER_ATTACHMENT_NOT_FOUND`
- `409 XQ_PAPER_ALREADY_CONFIRMED` / `XQ_VERSION_CONFLICT`
- `422 XQ_PAPER_PAGES_EMPTY` / `XQ_PAPER_TOO_MANY_PAGES` / `XQ_PAPER_IMAGE_TOO_LARGE` / `XQ_PAPER_UNSUPPORTED_MEDIA_TYPE` / `XQ_PAPER_PAGE_METADATA_INVALID`
- `502 XQ_PAPER_STORAGE_FETCH_FAILED` / `XQ_PAPER_EXTRACTION_PROVIDER_FAILED` / `XQ_PAPER_EXTRACTION_MALFORMED_OUTPUT` / `XQ_PAPER_EXTRACTION_PROVIDER_BOUNDARY_FAILED`
