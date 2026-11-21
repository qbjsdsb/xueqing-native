export type JsonObject = Record<string, unknown>;

export const ALLOWED_AI_GRADING_SOURCES = new Set<string>([
  "teacher_marked",
  "ai_tentative_unmarked",
]);

export const ALLOWED_ERROR_ROOT_CAUSES = new Set<string>([
  "concept_gap",
  "misread_condition",
  "strategy_stuck",
  "calculation_slip",
  "time_pressure",
  "unclassified",
]);

export const ALLOWED_SECTION_POSITIONS = new Set<string>([
  "early",
  "middle",
  "late",
  "unknown",
]);

export const PAPER_EXTRACTION_SYSTEM_INSTRUCTION =
  "你是学情记录系统的单份试卷/作业视觉结构化解析助手。你的唯一任务是客观识别图片中的题目与批改痕迹，输出严格符合 JSON Schema 的草稿结果，绝不编造图片中不存在的分数或错题，也绝不输出任何学生姓名、学校、班级、学号等个人身份信息。";

export const PAPER_EXTRACTION_JSON_SCHEMA: JsonObject = {
  type: "object",
  properties: {
    grading_source: {
      type: "string",
      enum: ["teacher_marked", "ai_tentative_unmarked"],
      description:
        "If teacher red marks, checkmarks, crosses, or scores are visible, use 'teacher_marked'. If the sheet has student answers with no teacher grading marks, use 'ai_tentative_unmarked'.",
    },
    title: {
      type: "string",
      description:
        "Concise anonymous exam/homework title (e.g. '第三单元小数除法周测'). Must NOT contain student name, school, or class.",
    },
    question_count: {
      type: ["integer", "null"],
      description: "Estimated total number of questions on the paper, or null if unclear.",
    },
    mistake_count: {
      type: ["integer", "null"],
      description: "Number of incorrect or point-deducted questions, or null if unclear.",
    },
    score_earned: {
      type: ["number", "null"],
      description: "Teacher-marked score earned if clearly visible, otherwise null.",
    },
    score_total: {
      type: ["number", "null"],
      description: "Total full score of the paper if visible or inferable (e.g. 100), otherwise null.",
    },
    duration_minutes: {
      type: ["integer", "null"],
      description: "Explicitly written completion time in minutes if shown on paper, otherwise null.",
    },
    extraction_summary: {
      type: "string",
      description:
        "Concise objective Chinese summary of overall completion, main error patterns, and whether teacher marks were present (<= 500 chars). Zero personal identity info.",
    },
    mistakes: {
      type: "array",
      description: "Structured list of wrong, deducted, or corrected questions found across the pages.",
      items: {
        type: "object",
        properties: {
          page_index: {
            type: "integer",
            description: "1-based page index where this mistake appears.",
          },
          question_label: {
            type: "string",
            description: "Question number or label (e.g. '一(3)', '第14题', '计算题第2小题').",
          },
          topic_tags: {
            type: "array",
            items: { type: "string" },
            description: "1 to 6 concise Chinese knowledge/topic tags (e.g. ['小数除法', '竖式对齐']).",
          },
          error_root_cause: {
            type: "string",
            enum: [
              "concept_gap",
              "misread_condition",
              "strategy_stuck",
              "calculation_slip",
              "time_pressure",
              "unclassified",
            ],
            description: "Most likely root cause category for this mistake.",
          },
          paper_section_position: {
            type: "string",
            enum: ["early", "middle", "late", "unknown"],
            description: "Relative position in the paper (early=front/basic, middle=mid, late=final/harder).",
          },
          points_lost: {
            type: ["number", "null"],
            description: "Points deducted on this question if visible, otherwise null.",
          },
          corrected_on_paper: {
            type: "boolean",
            description: "Whether visible correction/rework marks exist next to the mistake.",
          },
          summary: {
            type: "string",
            description: "Concise objective Chinese explanation of what went wrong (<= 300 chars).",
          },
        },
        required: [
          "page_index",
          "question_label",
          "topic_tags",
          "error_root_cause",
          "paper_section_position",
          "corrected_on_paper",
          "summary",
        ],
      },
    },
  },
  required: ["grading_source", "extraction_summary", "mistakes"],
};

export interface AnonymousPaperPromptContext {
  subjectKey: string;
  paperKind: string;
  paperDate: string;
  existingTitle: string | null;
  pageIndices: number[];
  redactedPageIndices: number[];
}

export interface SanitizedMistakeDraft {
  page_index: number;
  question_label: string;
  topic_tags: string[];
  error_root_cause: string;
  paper_section_position: string;
  points_lost: number | null;
  corrected_on_paper: boolean;
  summary: string;
}

export interface SanitizedPaperExtraction {
  grading_source: "teacher_marked" | "ai_tentative_unmarked";
  title: string | null;
  question_count: number | null;
  mistake_count: number | null;
  score_earned: number | null;
  score_total: number | null;
  duration_minutes: number | null;
  extraction_summary: string;
  mistakes: SanitizedMistakeDraft[];
}

export function buildPaperExtractionPrompt(context: AnonymousPaperPromptContext): string {
  const redactedNote = context.redactedPageIndices.length > 0
    ? `第 ${context.redactedPageIndices.join("、")} 页顶部姓名区已做隐私遮挡。`
    : "即使卷头存在未遮挡的姓名、班级或学校文字，也必须完全忽略，严禁在输出中提及。";

  const lines = [
    "请按顺序阅读随附的试卷/作业页面图片，并提取单份试卷结构化草稿 JSON：",
    `- 学科标识: ${context.subjectKey}`,
    `- 试卷类型: ${context.paperKind}`,
    `- 试卷日期: ${context.paperDate}`,
    `- 总页数及页码顺序: 共 ${context.pageIndices.length} 页（页码 ${context.pageIndices.join(", ")}）`,
    context.existingTitle ? `- 已知参考标题: ${context.existingTitle}` : "- 已知参考标题: 无（请根据卷面标题提取匿名标题）",
    `- 隐私约束: ${redactedNote}`,
    "",
    "提取规则：",
    "1. 优先识别老师红笔批改、对错号、扣分和订正痕迹；若卷面有明确批改痕迹，grading_source 填 teacher_marked；若为未批改的空白/已做作业，grading_source 填 ai_tentative_unmarked 且仅在把握高时给出试判错题草稿，切勿伪造总分。",
    "2. 兼容中小学语文、数学、英语及其他学科（物理、化学等），知识点标签请用简洁规范的中文短语（每题 1-6 个标签）。",
    "3. 明确标注每道错题所在的 page_index（必须属于本次提供的页码）以及 paper_section_position（early/middle/late/unknown），便于后续分析后半段疲劳度。",
    "4. 所有计数与分数保持自洽（score_earned <= score_total，mistake_count <= question_count）。",
    "5. 严禁输出任何学生真实姓名、学校、班级或联系方式。",
  ];

  return lines.join("\n");
}

function asNonEmptyString(value: unknown, maxLength: number): string | null {
  if (typeof value !== "string") return null;
  const trimmed = value.trim();
  if (!trimmed) return null;
  return trimmed.length > maxLength ? trimmed.slice(0, maxLength) : trimmed;
}

function asBoundedInteger(value: unknown, min: number, max: number): number | null {
  if (value === null || value === undefined) return null;
  if (typeof value !== "number" || !Number.isFinite(value)) return null;
  const rounded = Math.round(value);
  if (rounded < min || rounded > max) return null;
  return rounded;
}

function asBoundedNumber(value: unknown, min: number, max: number): number | null {
  if (value === null || value === undefined) return null;
  if (typeof value !== "number" || !Number.isFinite(value)) return null;
  if (value < min || value > max) return null;
  return Math.round(value * 100) / 100;
}

export function extractModelTextFromInteraction(payload: unknown): string | null {
  if (!payload || typeof payload !== "object" || Array.isArray(payload)) {
    return null;
  }
  const obj = payload as JsonObject;

  if (typeof obj.output_text === "string" && obj.output_text.trim().length > 0) {
    return obj.output_text.trim();
  }

  if (Array.isArray(obj.steps)) {
    const trailingTexts: string[] = [];
    for (let i = obj.steps.length - 1; i >= 0; i--) {
      const step = obj.steps[i];
      if (!step || typeof step !== "object" || Array.isArray(step)) continue;
      const stepObj = step as JsonObject;
      if (stepObj.type !== "model_output") {
        if (trailingTexts.length > 0) break;
        continue;
      }
      const content = stepObj.content;
      if (!Array.isArray(content)) continue;
      const stepParts: string[] = [];
      for (const item of content) {
        if (!item || typeof item !== "object" || Array.isArray(item)) continue;
        const itemObj = item as JsonObject;
        if (itemObj.type === "text" && typeof itemObj.text === "string") {
          stepParts.push(itemObj.text);
        }
      }
      if (stepParts.length > 0) {
        trailingTexts.unshift(stepParts.join(""));
      }
    }
    const combined = trailingTexts.join("").trim();
    if (combined.length > 0) return combined;
  }

  return null;
}

export function validateAndNormalizeExtraction(
  rawText: string,
  validPageIndices: number[],
): SanitizedPaperExtraction | null {
  let parsedUnknown: unknown;
  try {
    let cleaned = rawText.trim();
    if (cleaned.startsWith("```")) {
      cleaned = cleaned.replace(/^```(?:json)?\s*/i, "").replace(/\s*```$/i, "").trim();
    }
    parsedUnknown = JSON.parse(cleaned);
  } catch {
    return null;
  }

  if (!parsedUnknown || typeof parsedUnknown !== "object" || Array.isArray(parsedUnknown)) {
    return null;
  }
  const parsed = parsedUnknown as JsonObject;

  const rawGradingSource = typeof parsed.grading_source === "string"
    ? parsed.grading_source.trim()
    : "";
  if (!ALLOWED_AI_GRADING_SOURCES.has(rawGradingSource)) {
    return null;
  }
  const gradingSource = rawGradingSource as "teacher_marked" | "ai_tentative_unmarked";

  const extractionSummary = asNonEmptyString(parsed.extraction_summary, 2000);
  if (!extractionSummary) {
    return null;
  }

  const title = asNonEmptyString(parsed.title, 200);
  let questionCount = asBoundedInteger(parsed.question_count, 1, 500);
  let mistakeCount = asBoundedInteger(parsed.mistake_count, 0, 500);
  let scoreEarned = asBoundedNumber(parsed.score_earned, 0, 9999.99);
  let scoreTotal = asBoundedNumber(parsed.score_total, 0.01, 9999.99);
  const durationMinutes = asBoundedInteger(parsed.duration_minutes, 1, 600);

  if (scoreEarned !== null && scoreTotal !== null && scoreEarned > scoreTotal) {
    scoreEarned = null;
    scoreTotal = null;
  }

  const validPagesSet = new Set(validPageIndices);
  const fallbackPageIndex = validPageIndices[0] ?? 1;
  const rawMistakes = Array.isArray(parsed.mistakes) ? parsed.mistakes : null;
  if (!rawMistakes) {
    return null;
  }

  const mistakes: SanitizedMistakeDraft[] = [];
  for (const item of rawMistakes.slice(0, 50)) {
    if (!item || typeof item !== "object" || Array.isArray(item)) continue;
    const itemObj = item as JsonObject;

    const rawPageIndex = asBoundedInteger(itemObj.page_index, 1, 50);
    const pageIndex = rawPageIndex !== null && validPagesSet.has(rawPageIndex)
      ? rawPageIndex
      : fallbackPageIndex;

    const questionLabel = asNonEmptyString(itemObj.question_label, 60);
    const summary = asNonEmptyString(itemObj.summary, 500);
    if (!questionLabel || !summary) continue;

    const rawTags = Array.isArray(itemObj.topic_tags) ? itemObj.topic_tags : [];
    const seenTags = new Set<string>();
    const topicTags: string[] = [];
    for (const rawTag of rawTags) {
      const tag = asNonEmptyString(rawTag, 60);
      if (tag && !seenTags.has(tag)) {
        seenTags.add(tag);
        topicTags.push(tag);
        if (topicTags.length >= 10) break;
      }
    }

    const rawRootCause = typeof itemObj.error_root_cause === "string"
      ? itemObj.error_root_cause.trim()
      : "unclassified";
    const errorRootCause = ALLOWED_ERROR_ROOT_CAUSES.has(rawRootCause)
      ? rawRootCause
      : "unclassified";

    const rawPosition = typeof itemObj.paper_section_position === "string"
      ? itemObj.paper_section_position.trim()
      : "unknown";
    const paperSectionPosition = ALLOWED_SECTION_POSITIONS.has(rawPosition)
      ? rawPosition
      : "unknown";

    const pointsLost = asBoundedNumber(itemObj.points_lost, 0, 9999.99);
    const correctedOnPaper = itemObj.corrected_on_paper === true;

    mistakes.push({
      page_index: pageIndex,
      question_label: questionLabel,
      topic_tags: topicTags,
      error_root_cause: errorRootCause,
      paper_section_position: paperSectionPosition,
      points_lost: pointsLost,
      corrected_on_paper: correctedOnPaper,
      summary,
    });
  }

  if (mistakeCount === null) {
    mistakeCount = mistakes.length;
  } else if (mistakeCount < mistakes.length) {
    mistakeCount = mistakes.length;
  }

  if (questionCount !== null && mistakeCount > questionCount) {
    questionCount = mistakeCount;
  }

  return {
    grading_source: gradingSource,
    title,
    question_count: questionCount,
    mistake_count: mistakeCount,
    score_earned: scoreEarned,
    score_total: scoreTotal,
    duration_minutes: durationMinutes,
    extraction_summary: extractionSummary,
    mistakes,
  };
}
