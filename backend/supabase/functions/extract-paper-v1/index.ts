import { createClient } from "npm:@supabase/supabase-js@2.116.0";
import {
  ALLOWED_ATTACHMENT_BUCKET,
  DEFAULT_GEMINI_API_BASE_URL,
  DEFAULT_GEMINI_MODEL,
  DEFAULT_HOSTED_REQUIRED_EDGE_REGION,
  GEMINI_REQUEST_TIMEOUT_MS,
  MAX_EXTRACTION_PAGES,
  MAX_PAGE_BYTES,
  MAX_TOTAL_IMAGE_BYTES,
} from "./deployment.ts";
import {
  buildPaperExtractionPrompt,
  extractModelTextFromInteraction,
  type JsonObject,
  PAPER_EXTRACTION_JSON_SCHEMA,
  PAPER_EXTRACTION_SYSTEM_INSTRUCTION,
  validateAndNormalizeExtraction,
} from "./schema.ts";

const responseHeaders = {
  "Access-Control-Allow-Headers": "authorization, apikey, content-type, x-client-info",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
  "Access-Control-Allow-Origin": "*",
  "Content-Type": "application/json; charset=utf-8",
};

const ALLOWED_IMAGE_MIME_TYPES = new Set<string>([
  "image/jpeg",
  "image/png",
  "image/webp",
]);

class ExtractionError extends Error {
  readonly code: string;
  readonly status: number;

  constructor(code: string, status = 400) {
    super(code);
    this.name = "ExtractionError";
    this.code = code;
    this.status = status;
  }
}

function response(body: JsonObject, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: responseHeaders,
  });
}

function optionalEnvironment(...names: string[]): string | null {
  for (const name of names) {
    const value = Deno.env.get(name)?.trim();
    if (value) return value;
  }
  return null;
}

function requiredEnvironment(name: string): string {
  const value = Deno.env.get(name)?.trim();
  if (!value) {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_UNAVAILABLE", 503);
  }
  return value;
}

function stringValue(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized ? normalized : null;
}

function mappedKey(environmentName: string): string | null {
  const raw = Deno.env.get(environmentName)?.trim();
  if (!raw) return null;

  try {
    const parsed = JSON.parse(raw) as JsonObject;
    const candidate = stringValue(parsed.default);
    if (!candidate) return null;

    const indirect = Deno.env.get(candidate)?.trim();
    if (indirect) return indirect;
    if (candidate.startsWith("sb_") || candidate.split(".").length === 3) {
      return candidate;
    }
    return null;
  } catch {
    return null;
  }
}

function publishableKey(): string {
  const value = mappedKey("SUPABASE_PUBLISHABLE_KEYS") ??
    optionalEnvironment("SUPABASE_PUBLISHABLE_KEY", "SUPABASE_ANON_KEY");
  if (!value) {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_UNAVAILABLE", 503);
  }
  return value;
}

function localReferenceModeEnabled(): boolean {
  return optionalEnvironment("XUEQING_LOCAL_REFERENCE_MODE") === "1";
}

function enforceRequiredExecutionRegion(): void {
  // Local reference-provider CI/dev has no hosted-region identity and must opt
  // in explicitly. Production leaves this unset, so hosted execution always
  // enforces the audited Singapore policy before any auth/business processing.
  if (localReferenceModeEnabled()) return;

  const configured = optionalEnvironment("XUEQING_REQUIRED_EDGE_REGION");
  const required = configured ?? DEFAULT_HOSTED_REQUIRED_EDGE_REGION;
  const actual = optionalEnvironment("SB_REGION");
  if (!actual || actual !== required) {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_REGION_UNAVAILABLE", 503);
  }
}

function requiredBearer(request: Request): string {
  const header = request.headers.get("Authorization")?.trim() ?? "";
  if (!header.toLowerCase().startsWith("bearer ")) {
    throw new ExtractionError("XQ_AUTH_REQUIRED", 401);
  }
  const token = header.slice(7).trim();
  if (!token) throw new ExtractionError("XQ_AUTH_REQUIRED", 401);
  return token;
}

function asObject(value: unknown, errorCode: string, status = 400): JsonObject {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new ExtractionError(errorCode, status);
  }
  return value as JsonObject;
}

const uuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function requiredUuid(value: unknown): string {
  const normalized = stringValue(value);
  if (!normalized || !uuidPattern.test(normalized)) {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_INPUT_INVALID", 400);
  }
  return normalized;
}

function optionalPositiveInteger(value: unknown): number | null {
  if (value === undefined || value === null) return null;
  if (typeof value !== "number" || !Number.isInteger(value) || value < 1) {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_INPUT_INVALID", 400);
  }
  return value;
}

function publicRpcError(error: { message?: string } | null | undefined): ExtractionError {
  const message = error?.message?.trim() ?? "";
  const firstLine = message.split("\n", 1)[0]?.trim() ?? "";
  if (firstLine.startsWith("XQ_") && firstLine.length <= 120) {
    const status = firstLine.includes("AUTH") ? 401
      : firstLine.includes("UNAVAILABLE") || firstLine.includes("DISABLED") || firstLine.includes("REQUIRED") ? 403
      : firstLine.includes("NOT_FOUND") ? 404
      : firstLine.includes("CONFLICT") || firstLine.includes("ALREADY") ? 409
      : 400;
    return new ExtractionError(firstLine, status);
  }
  return new ExtractionError("XQ_PAPER_EXTRACTION_PROVIDER_BOUNDARY_FAILED", 502);
}

function resolveGeminiBaseUrl(): string {
  const raw = optionalEnvironment("XUEQING_GEMINI_API_BASE_URL") ?? DEFAULT_GEMINI_API_BASE_URL;
  let parsed: URL;
  try {
    parsed = new URL(raw);
  } catch {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_UNAVAILABLE", 503);
  }

  if (!localReferenceModeEnabled()) {
    if (parsed.protocol !== "https:" || parsed.hostname !== "generativelanguage.googleapis.com") {
      throw new ExtractionError("XQ_PAPER_EXTRACTION_UNAVAILABLE", 503);
    }
  } else if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_UNAVAILABLE", 503);
  }

  return parsed.toString().replace(/\/+$/, "");
}

function encodeStoragePath(path: string): string {
  return path
    .split("/")
    .filter((segment) => segment.length > 0)
    .map((segment) => encodeURIComponent(segment))
    .join("/");
}

function bytesToBase64(bytes: Uint8Array): string {
  const chunkSize = 0x8000;
  const parts: string[] = [];
  for (let i = 0; i < bytes.length; i += chunkSize) {
    const slice = bytes.subarray(i, Math.min(i + chunkSize, bytes.length));
    let binary = "";
    for (let j = 0; j < slice.length; j++) {
      binary += String.fromCharCode(slice[j]);
    }
    parts.push(binary);
  }
  return btoa(parts.join(""));
}

interface LoadedPaperPage {
  pageIndex: number;
  headerRedacted: boolean;
  mimeType: string;
  base64Data: string;
  byteSize: number;
}

async function downloadPaperPage(
  supabaseUrl: string,
  publishableApiKey: string,
  accessToken: string,
  page: JsonObject,
): Promise<LoadedPaperPage> {
  const pageIndex = typeof page.page_index === "number" && Number.isInteger(page.page_index)
    ? page.page_index
    : 0;
  const bucketId = stringValue(page.bucket_id);
  const objectName = stringValue(page.object_name);
  const declaredContentType = stringValue(page.content_type)?.toLowerCase() ?? "";
  const headerRedacted = page.header_redacted === true;

  if (pageIndex < 1 || bucketId !== ALLOWED_ATTACHMENT_BUCKET || !objectName) {
    throw new ExtractionError("XQ_PAPER_PAGE_METADATA_INVALID", 422);
  }
  if (!ALLOWED_IMAGE_MIME_TYPES.has(declaredContentType)) {
    throw new ExtractionError("XQ_PAPER_UNSUPPORTED_MEDIA_TYPE", 422);
  }
  if (objectName.includes("..")) {
    throw new ExtractionError("XQ_PAPER_PAGE_METADATA_INVALID", 422);
  }

  const encodedPath = encodeStoragePath(objectName);
  const url =
    `${supabaseUrl.replace(/\/+$/, "")}/storage/v1/object/authenticated/${encodeURIComponent(bucketId)}/${encodedPath}`;

  let storageResponse: Response;
  try {
    storageResponse = await fetch(url, {
      method: "GET",
      headers: {
        apikey: publishableApiKey,
        Authorization: `Bearer ${accessToken}`,
      },
      signal: AbortSignal.timeout(15_000),
    });
  } catch {
    throw new ExtractionError("XQ_PAPER_STORAGE_FETCH_FAILED", 502);
  }

  if (storageResponse.status === 401 || storageResponse.status === 403) {
    throw new ExtractionError("XQ_TEACHING_CONTEXT_UNAVAILABLE", 403);
  }
  if (storageResponse.status === 404) {
    throw new ExtractionError("XQ_PAPER_ATTACHMENT_NOT_FOUND", 404);
  }
  if (!storageResponse.ok) {
    throw new ExtractionError("XQ_PAPER_STORAGE_FETCH_FAILED", 502);
  }

  const buffer = new Uint8Array(await storageResponse.arrayBuffer());
  if (buffer.byteLength === 0 || buffer.byteLength > MAX_PAGE_BYTES) {
    throw new ExtractionError("XQ_PAPER_IMAGE_TOO_LARGE", 422);
  }

  return {
    pageIndex,
    headerRedacted,
    mimeType: declaredContentType,
    base64Data: bytesToBase64(buffer),
    byteSize: buffer.byteLength,
  };
}

async function handle(request: Request): Promise<Response> {
  if (request.method === "OPTIONS") return response({ ok: true });

  // Every hosted non-preflight request proves its runtime region before method,
  // bearer, body, database, Storage, or Gemini processing.
  enforceRequiredExecutionRegion();

  if (request.method !== "POST") {
    return response({ ok: false, error: "XQ_METHOD_NOT_ALLOWED" }, 405);
  }

  const accessToken = requiredBearer(request);
  const supabaseUrl = requiredEnvironment("SUPABASE_URL");
  const publishableApiKey = publishableKey();
  const geminiApiKey = requiredEnvironment("GEMINI_API_KEY");
  const geminiBaseUrl = resolveGeminiBaseUrl();
  const geminiModel = optionalEnvironment("XUEQING_GEMINI_MODEL") ?? DEFAULT_GEMINI_MODEL;

  let input: JsonObject;
  try {
    input = asObject(await request.json(), "XQ_PAPER_EXTRACTION_INPUT_INVALID", 400);
  } catch (error) {
    if (error instanceof ExtractionError) throw error;
    throw new ExtractionError("XQ_PAPER_EXTRACTION_INPUT_INVALID", 400);
  }

  const operationId = requiredUuid(input.operation_id);
  const organizationId = requiredUuid(input.organization_id);
  const studentId = requiredUuid(input.student_id);
  const subjectProfileId = requiredUuid(input.subject_profile_id);
  const paperSubmissionId = requiredUuid(input.paper_submission_id);
  const expectedVersion = optionalPositiveInteger(input.expected_version);

  // Strictly user-scoped Supabase client. Zero service_role bypass anywhere.
  const userClient = createClient(supabaseUrl, publishableApiKey, {
    auth: { autoRefreshToken: false, persistSession: false },
    global: { headers: { Authorization: `Bearer ${accessToken}` } },
  });

  const { data: longitudinalData, error: longitudinalError } = await userClient.rpc(
    "get_student_paper_longitudinal_v1",
    {
      p_organization_id: organizationId,
      p_student_id: studentId,
      p_subject_profile_id: subjectProfileId,
      p_window_days: 365,
      p_limit: 50,
    },
  );
  if (longitudinalError) throw publicRpcError(longitudinalError);

  const longitudinal = asObject(
    longitudinalData,
    "XQ_PAPER_EXTRACTION_PROVIDER_BOUNDARY_FAILED",
    502,
  );
  const subjectKey = stringValue(longitudinal.subject_key) ?? "general";
  const papers = Array.isArray(longitudinal.papers) ? longitudinal.papers : [];

  const targetPaperRaw = papers.find((item) => {
    if (!item || typeof item !== "object" || Array.isArray(item)) return false;
    return (item as JsonObject).paper_submission_id === paperSubmissionId;
  });
  if (!targetPaperRaw || typeof targetPaperRaw !== "object" || Array.isArray(targetPaperRaw)) {
    throw new ExtractionError("XQ_PAPER_NOT_FOUND", 404);
  }
  const targetPaper = targetPaperRaw as JsonObject;

  const confirmationState = stringValue(targetPaper.confirmation_state);
  if (confirmationState === "human_confirmed") {
    throw new ExtractionError("XQ_PAPER_ALREADY_CONFIRMED", 409);
  }

  const currentVersion = typeof targetPaper.version === "number" && Number.isInteger(targetPaper.version)
    ? targetPaper.version
    : null;
  if (currentVersion === null || currentVersion < 1) {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_PROVIDER_BOUNDARY_FAILED", 502);
  }
  if (expectedVersion !== null && expectedVersion !== currentVersion) {
    throw new ExtractionError("XQ_VERSION_CONFLICT", 409);
  }

  const rawPages = Array.isArray(targetPaper.pages) ? targetPaper.pages : [];
  if (rawPages.length === 0) {
    throw new ExtractionError("XQ_PAPER_PAGES_EMPTY", 422);
  }
  if (rawPages.length > MAX_EXTRACTION_PAGES) {
    throw new ExtractionError("XQ_PAPER_TOO_MANY_PAGES", 422);
  }

  const loadedPages: LoadedPaperPage[] = [];
  let totalBytes = 0;
  for (const rawPage of rawPages) {
    const pageObj = asObject(rawPage, "XQ_PAPER_PAGE_METADATA_INVALID", 422);
    const loaded = await downloadPaperPage(
      supabaseUrl,
      publishableApiKey,
      accessToken,
      pageObj,
    );
    totalBytes += loaded.byteSize;
    if (totalBytes > MAX_TOTAL_IMAGE_BYTES) {
      throw new ExtractionError("XQ_PAPER_IMAGE_TOO_LARGE", 422);
    }
    loadedPages.push(loaded);
  }
  loadedPages.sort((a, b) => a.pageIndex - b.pageIndex);

  const pageIndices = loadedPages.map((p) => p.pageIndex);
  const redactedPageIndices = loadedPages.filter((p) => p.headerRedacted).map((p) => p.pageIndex);

  // Build strictly anonymous prompt (never includes student_display_name or organization_name).
  const promptText = buildPaperExtractionPrompt({
    subjectKey,
    paperKind: stringValue(targetPaper.paper_kind) ?? "homework",
    paperDate: stringValue(targetPaper.paper_date) ?? "",
    existingTitle: stringValue(targetPaper.title),
    pageIndices,
    redactedPageIndices,
  });

  const interactionInput: JsonObject[] = [
    { type: "text", text: promptText },
    ...loadedPages.map((page) => ({
      type: "image",
      data: page.base64Data,
      mime_type: page.mimeType,
    })),
  ];

  // Call Gemini Interactions API v1 with store=false (zero server retention for minors' papers).
  let geminiResponse: Response;
  try {
    geminiResponse = await fetch(`${geminiBaseUrl}/v1/interactions`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-goog-api-key": geminiApiKey,
      },
      body: JSON.stringify({
        model: geminiModel,
        store: false,
        system_instruction: PAPER_EXTRACTION_SYSTEM_INSTRUCTION,
        input: interactionInput,
        response_format: {
          type: "text",
          mime_type: "application/json",
          schema: PAPER_EXTRACTION_JSON_SCHEMA,
        },
        generation_config: {
          temperature: 0.1,
        },
      }),
      signal: AbortSignal.timeout(GEMINI_REQUEST_TIMEOUT_MS),
    });
  } catch {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_PROVIDER_FAILED", 502);
  }

  if (!geminiResponse.ok) {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_PROVIDER_FAILED", 502);
  }

  let interactionPayload: unknown;
  try {
    interactionPayload = await geminiResponse.json();
  } catch {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_MALFORMED_OUTPUT", 502);
  }

  const rawModelText = extractModelTextFromInteraction(interactionPayload);
  if (!rawModelText) {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_MALFORMED_OUTPUT", 502);
  }

  const extraction = validateAndNormalizeExtraction(rawModelText, pageIndices);
  if (!extraction) {
    throw new ExtractionError("XQ_PAPER_EXTRACTION_MALFORMED_OUTPUT", 502);
  }

  // Persist strictly as ai_draft under caller's JWT. Never overwrites human_confirmed.
  const { data: saveData, error: saveError } = await userClient.rpc(
    "save_paper_extraction_v1",
    {
      p_operation_id: operationId,
      p_paper_submission_id: paperSubmissionId,
      p_expected_version: currentVersion,
      p_mode: "ai_draft",
      p_grading_source: extraction.grading_source,
      p_title: extraction.title,
      p_question_count: extraction.question_count,
      p_mistake_count: extraction.mistake_count,
      p_score_earned: extraction.score_earned,
      p_score_total: extraction.score_total,
      p_duration_minutes: extraction.duration_minutes,
      p_extraction_summary: extraction.extraction_summary,
      p_mistakes: extraction.mistakes,
    },
  );
  if (saveError) throw publicRpcError(saveError);

  const saved = asObject(saveData, "XQ_PAPER_EXTRACTION_PROVIDER_BOUNDARY_FAILED", 502);

  return response({
    ok: true,
    contract: "extract_paper_v1",
    operation_id: operationId,
    paper_submission_id: paperSubmissionId,
    model: geminiModel,
    pages_analyzed: loadedPages.length,
    confirmation_state: stringValue(saved.confirmation_state) ?? "draft_ready",
    grading_source: stringValue(saved.grading_source) ?? extraction.grading_source,
    version: saved.version ?? currentVersion + 1,
    mistake_items_saved: saved.mistake_items_saved ?? extraction.mistakes.length,
    server_committed_at: saved.server_committed_at ?? null,
  });
}

Deno.serve(async (request) => {
  try {
    return await handle(request);
  } catch (error) {
    if (error instanceof ExtractionError) {
      return response({ ok: false, error: error.code }, error.status);
    }
    return response(
      { ok: false, error: "XQ_PAPER_EXTRACTION_UNAVAILABLE" },
      503,
    );
  }
});
