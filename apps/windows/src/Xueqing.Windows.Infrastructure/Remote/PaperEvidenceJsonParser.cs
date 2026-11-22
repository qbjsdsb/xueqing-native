using System.Globalization;
using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

public static class PaperEvidenceJsonParser
{
    public static StudentPaperLongitudinalSnapshot ParseLongitudinal(
        string json,
        StudentLearningScope expectedScope,
        Guid expectedActorAppUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Longitudinal payload must be a JSON object.");
        }

        if (RequireString(root, "contract") != "student_paper_longitudinal_v1")
        {
            throw new InvalidDataException("Unexpected longitudinal projection contract.");
        }

        var actorId = RequireGuid(root, "actor_app_user_id");
        var orgId = RequireGuid(root, "organization_id");
        var studentId = RequireGuid(root, "student_id");
        var subjectProfileId = RequireGuid(root, "subject_profile_id");

        if (actorId != expectedActorAppUserId ||
            orgId != expectedScope.OrganizationId ||
            studentId != expectedScope.StudentId ||
            subjectProfileId != expectedScope.SubjectProfileId)
        {
            throw new InvalidDataException("Longitudinal projection scope does not match requested context.");
        }

        var summary = ParseSummary(RequireObject(root, "summary"));
        var weeklyCadence = ParseWeeklyCadence(RequireArray(root, "weekly_cadence"));
        var rootCauses = ParseRootCauseTrends(RequireArray(root, "root_cause_breakdown"));
        var recurringTopics = ParseRecurringTopics(RequireArray(root, "recurring_topics"));
        var papers = ParsePapers(RequireArray(root, "papers"));
        var drafts = ParseStudyPlanDrafts(RequireArray(root, "recent_study_plan_drafts"));

        return new StudentPaperLongitudinalSnapshot(
            GeneratedAtServer: RequireDateTimeOffset(root, "generated_at_server"),
            ActorAppUserId: actorId,
            OrganizationId: orgId,
            OrganizationName: RequireString(root, "organization_name"),
            OrganizationTimeZone: RequireString(root, "organization_time_zone"),
            OrganizationBusinessDate: RequireDateOnly(root, "organization_business_date"),
            StudentId: studentId,
            StudentDisplayName: RequireString(root, "student_display_name"),
            SubjectProfileId: subjectProfileId,
            SubjectKey: RequireString(root, "subject_key"),
            AssignmentId: RequireGuid(root, "assignment_id"),
            WindowDays: RequireInt(root, "window_days"),
            WindowStartDate: RequireDateOnly(root, "window_start_date"),
            Summary: summary,
            WeeklyCadence: weeklyCadence,
            RootCauseBreakdown: rootCauses,
            RecurringTopics: recurringTopics,
            Papers: papers,
            RecentStudyPlanDrafts: drafts);
    }

    public static SaveStudyPlanDraftReceipt ParseSaveStudyPlanDraftReceipt(
        string json,
        Guid expectedOperationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            RequireString(root, "contract") != "save_study_plan_draft_v1")
        {
            throw new InvalidDataException("Unexpected save_study_plan_draft_v1 receipt.");
        }

        var opId = RequireGuid(root, "operation_id");
        if (opId != expectedOperationId)
        {
            throw new InvalidDataException("Receipt operation_id does not match request.");
        }

        return new SaveStudyPlanDraftReceipt(
            opId,
            RequireGuid(root, "draft_id"),
            RequireString(root, "status"),
            RequireLong(root, "version"),
            RequireDateTimeOffset(root, "server_committed_at"));
    }

    public static TriggerPaperExtractionReceipt ParseTriggerExtractionReceipt(
        string json,
        Guid expectedOperationId,
        Guid expectedPaperSubmissionId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Expected JSON object.");
        }

        var ok = RequireBool(root, "ok");
        if (!ok)
        {
            throw new InvalidDataException("Expected ok=true in extract-paper-v1 receipt.");
        }

        var contract = RequireString(root, "contract");
        if (!string.Equals(contract, "extract_paper_v1", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected extract_paper contract: {contract}");
        }

        var operationId = RequireGuid(root, "operation_id");
        if (operationId != expectedOperationId)
        {
            throw new InvalidDataException("extract-paper-v1 operation_id mismatch.");
        }

        var paperSubmissionId = RequireGuid(root, "paper_submission_id");
        if (paperSubmissionId != expectedPaperSubmissionId)
        {
            throw new InvalidDataException("extract-paper-v1 paper_submission_id mismatch.");
        }

        return new TriggerPaperExtractionReceipt(
            operationId,
            paperSubmissionId,
            RequireString(root, "model"),
            RequireInt(root, "pages_analyzed"),
            RequireString(root, "confirmation_state"),
            RequireString(root, "grading_source"),
            RequireLong(root, "version"),
            RequireInt(root, "mistake_items_saved"),
            OptionalDateTimeOffset(root, "server_committed_at"));
    }

    public static SavePaperExtractionReceipt ParseSaveExtractionReceipt(
        string json,
        Guid expectedOperationId,
        Guid expectedPaperSubmissionId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Expected JSON object.");
        }

        var contract = RequireString(root, "contract");
        if (!string.Equals(contract, "save_paper_extraction_v1", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected save_paper_extraction contract: {contract}");
        }

        var operationId = RequireGuid(root, "operation_id");
        if (operationId != expectedOperationId)
        {
            throw new InvalidDataException("save_paper_extraction operation_id mismatch.");
        }

        var paperSubmissionId = RequireGuid(root, "paper_submission_id");
        if (paperSubmissionId != expectedPaperSubmissionId)
        {
            throw new InvalidDataException("save_paper_extraction paper_submission_id mismatch.");
        }

        return new SavePaperExtractionReceipt(
            operationId,
            paperSubmissionId,
            RequireString(root, "confirmation_state"),
            RequireString(root, "grading_source"),
            RequireLong(root, "version"),
            RequireInt(root, "mistake_items_saved"),
            RequireDateTimeOffset(root, "server_committed_at"));
    }

    public static UpdateStudyPlanDraftStatusReceipt ParseUpdateStudyPlanDraftStatusReceipt(
        string json,
        Guid expectedOperationId,
        Guid expectedDraftId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Expected JSON object.");
        }

        var contract = RequireString(root, "contract");
        if (!string.Equals(contract, "update_study_plan_draft_status_v1", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected update_study_plan_draft_status contract: {contract}");
        }

        var operationId = RequireGuid(root, "operation_id");
        if (operationId != expectedOperationId)
        {
            throw new InvalidDataException("update_study_plan_draft_status operation_id mismatch.");
        }

        var draftId = RequireGuid(root, "draft_id");
        if (draftId != expectedDraftId)
        {
            throw new InvalidDataException("update_study_plan_draft_status draft_id mismatch.");
        }

        return new UpdateStudyPlanDraftStatusReceipt(
            operationId,
            draftId,
            RequireString(root, "status"),
            RequireLong(root, "version"),
            RequireDateTimeOffset(root, "server_committed_at"));
    }

    private static PaperLongitudinalSummary ParseSummary(JsonElement el) =>
        new(
            TotalPapers: RequireInt(el, "total_papers"),
            ConfirmedPapers: RequireInt(el, "confirmed_papers"),
            DraftReadyPapers: RequireInt(el, "draft_ready_papers"),
            AwaitingExtractionPapers: RequireInt(el, "awaiting_extraction_papers"),
            TotalMistakesConfirmed: RequireInt(el, "total_mistakes_confirmed"),
            TotalMistakesAll: RequireInt(el, "total_mistakes_all"),
            TeacherMarkedAvgScoreRatio: OptionalDecimal(el, "teacher_marked_avg_score_ratio"),
            LateSectionMistakeRatio: OptionalDecimal(el, "late_section_mistake_ratio"),
            DraftAvgScoreRatio: OptionalDecimal(el, "draft_avg_score_ratio"),
            ConfirmedLateSectionMistakeRatio: OptionalDecimal(el, "confirmed_late_section_mistake_ratio"),
            PriorWindowDays: OptionalInt(el, "prior_window_days"));

    private static IReadOnlyList<PaperWeeklyCadencePoint> ParseWeeklyCadence(JsonElement array)
    {
        var list = new List<PaperWeeklyCadencePoint>();
        foreach (var item in array.EnumerateArray())
        {
            list.Add(new PaperWeeklyCadencePoint(
                WeekStart: RequireDateOnly(item, "week_start"),
                PaperCount: RequireInt(item, "paper_count"),
                ConfirmedCount: RequireInt(item, "confirmed_count"),
                MistakeCount: RequireInt(item, "mistake_count"),
                HomeworkCount: RequireInt(item, "homework_count"),
                QuizCount: RequireInt(item, "quiz_count"),
                UnitTestCount: RequireInt(item, "unit_test_count"),
                ExamCount: RequireInt(item, "exam_count")));
        }

        return list;
    }

    private static IReadOnlyList<PaperRootCauseTrend> ParseRootCauseTrends(JsonElement array)
    {
        var list = new List<PaperRootCauseTrend>();
        foreach (var item in array.EnumerateArray())
        {
            list.Add(new PaperRootCauseTrend(
                ErrorRootCause: ParseRootCause(RequireString(item, "error_root_cause")),
                Recent30dCount: RequireInt(item, "recent_30d_count"),
                PriorWindowCount: RequireInt(item, "prior_window_count"),
                TotalCount: RequireInt(item, "total_count")));
        }

        return list;
    }

    private static IReadOnlyList<PaperRecurringTopic> ParseRecurringTopics(JsonElement array)
    {
        var list = new List<PaperRecurringTopic>();
        foreach (var item in array.EnumerateArray())
        {
            var linkedIds = new List<Guid>();
            foreach (var idEl in RequireArray(item, "linked_case_ids").EnumerateArray())
            {
                if (idEl.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(idEl.GetString(), out var g) &&
                    g != Guid.Empty)
                {
                    linkedIds.Add(g);
                }
            }

            list.Add(new PaperRecurringTopic(
                TopicTag: RequireString(item, "topic_tag"),
                OccurrenceCount: RequireInt(item, "occurrence_count"),
                PaperCount: RequireInt(item, "paper_count"),
                FirstSeenDate: RequireDateOnly(item, "first_seen_date"),
                LastSeenDate: RequireDateOnly(item, "last_seen_date"),
                LinkedCaseIds: linkedIds));
        }

        return list;
    }

    private static IReadOnlyList<PaperSubmissionSummary> ParsePapers(JsonElement array)
    {
        var list = new List<PaperSubmissionSummary>();
        foreach (var item in array.EnumerateArray())
        {
            var pages = new List<PaperPageSummary>();
            foreach (var p in RequireArray(item, "pages").EnumerateArray())
            {
                pages.Add(new PaperPageSummary(
                    PageIndex: RequireInt(p, "page_index"),
                    AttachmentId: RequireGuid(p, "attachment_id"),
                    HeaderRedacted: RequireBool(p, "header_redacted"),
                    BucketId: RequireString(p, "bucket_id"),
                    ObjectName: RequireString(p, "object_name"),
                    ContentType: RequireString(p, "content_type"),
                    ByteSize: RequireLong(p, "byte_size")));
            }

            var mistakes = new List<PaperMistakeItemSummary>();
            foreach (var m in RequireArray(item, "mistakes").EnumerateArray())
            {
                var tags = new List<string>();
                if (m.TryGetProperty("topic_tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tagsEl.EnumerateArray())
                    {
                        if (t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                        {
                            tags.Add(t.GetString()!);
                        }
                    }
                }

                mistakes.Add(new PaperMistakeItemSummary(
                    MistakeId: RequireGuid(m, "mistake_id"),
                    PageIndex: RequireInt(m, "page_index"),
                    QuestionLabel: RequireString(m, "question_label"),
                    TopicTags: tags,
                    ErrorRootCause: ParseRootCause(RequireString(m, "error_root_cause")),
                    PaperSectionPosition: ParsePosition(RequireString(m, "paper_section_position")),
                    PointsLost: OptionalDecimal(m, "points_lost"),
                    CorrectedOnPaper: OptionalBool(m, "corrected_on_paper"),
                    Summary: RequireString(m, "summary"),
                    Provenance: RequireString(m, "provenance"),
                    LinkedCaseId: OptionalGuid(m, "linked_case_id")));
            }

            list.Add(new PaperSubmissionSummary(
                PaperSubmissionId: RequireGuid(item, "paper_submission_id"),
                SourceObservationId: OptionalGuid(item, "source_observation_id"),
                PaperDate: RequireDateOnly(item, "paper_date"),
                PaperKind: ParsePaperKind(RequireString(item, "paper_kind")),
                Title: OptionalString(item, "title"),
                GradingSource: ParseGradingSource(RequireString(item, "grading_source")),
                ConfirmationState: ParseConfirmationState(RequireString(item, "confirmation_state")),
                QuestionCount: OptionalInt(item, "question_count"),
                MistakeCount: OptionalInt(item, "mistake_count"),
                ScoreEarned: OptionalDecimal(item, "score_earned"),
                ScoreTotal: OptionalDecimal(item, "score_total"),
                DurationMinutes: OptionalInt(item, "duration_minutes"),
                ExtractionSummary: OptionalString(item, "extraction_summary"),
                Version: RequireLong(item, "version"),
                CreatedAtServer: RequireDateTimeOffset(item, "created_at_server"),
                ConfirmedAtServer: OptionalDateTimeOffset(item, "confirmed_at_server"),
                Pages: pages,
                Mistakes: mistakes));
        }

        return list;
    }

    private static IReadOnlyList<StudyPlanDraftSummary> ParseStudyPlanDrafts(JsonElement array)
    {
        var list = new List<StudyPlanDraftSummary>();
        foreach (var item in array.EnumerateArray())
        {
            list.Add(new StudyPlanDraftSummary(
                DraftId: RequireGuid(item, "draft_id"),
                Title: RequireString(item, "title"),
                BriefMarkdown: RequireString(item, "brief_markdown"),
                EvidenceRefsJson: RequireArray(item, "evidence_refs").GetRawText(),
                ProposedActionsJson: RequireArray(item, "proposed_actions").GetRawText(),
                WindowDays: RequireInt(item, "window_days"),
                PaperCountAnalyzed: RequireInt(item, "paper_count_analyzed"),
                Status: RequireString(item, "status"),
                Version: RequireLong(item, "version"),
                CreatedAtServer: RequireDateTimeOffset(item, "created_at_server"),
                UpdatedAtServer: RequireDateTimeOffset(item, "updated_at_server")));
        }

        return list;
    }

    private static PaperKind ParsePaperKind(string value) => value switch
    {
        "homework" => PaperKind.Homework,
        "quiz" => PaperKind.Quiz,
        "unit_test" => PaperKind.UnitTest,
        "exam" => PaperKind.Exam,
        _ => throw new InvalidDataException($"Unknown paper_kind: {value}"),
    };

    private static PaperGradingSource ParseGradingSource(string value) => value switch
    {
        "teacher_marked" => PaperGradingSource.TeacherMarked,
        "ai_tentative_unmarked" => PaperGradingSource.AiTentativeUnmarked,
        "human_verified" => PaperGradingSource.HumanVerified,
        _ => throw new InvalidDataException($"Unknown grading_source: {value}"),
    };

    private static PaperConfirmationState ParseConfirmationState(string value) => value switch
    {
        "awaiting_extraction" => PaperConfirmationState.AwaitingExtraction,
        "draft_ready" => PaperConfirmationState.DraftReady,
        "human_confirmed" => PaperConfirmationState.HumanConfirmed,
        "extraction_failed" => PaperConfirmationState.ExtractionFailed,
        _ => throw new InvalidDataException($"Unknown confirmation_state: {value}"),
    };

    private static PaperErrorRootCause ParseRootCause(string value) => value switch
    {
        "concept_gap" => PaperErrorRootCause.ConceptGap,
        "misread_condition" => PaperErrorRootCause.MisreadCondition,
        "strategy_stuck" => PaperErrorRootCause.StrategyStuck,
        "calculation_slip" => PaperErrorRootCause.CalculationSlip,
        "time_pressure" => PaperErrorRootCause.TimePressure,
        "unclassified" => PaperErrorRootCause.Unclassified,
        _ => throw new InvalidDataException($"Unknown error_root_cause: {value}"),
    };

    private static PaperSectionPosition ParsePosition(string value) => value switch
    {
        "early" => PaperSectionPosition.Early,
        "middle" => PaperSectionPosition.Middle,
        "late" => PaperSectionPosition.Late,
        "unknown" => PaperSectionPosition.Unknown,
        _ => throw new InvalidDataException($"Unknown paper_section_position: {value}"),
    };

    private static JsonElement RequireObject(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Object
            ? v
            : throw new InvalidDataException($"Expected JSON object for '{prop}'.");

    private static JsonElement RequireArray(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
            ? v
            : throw new InvalidDataException($"Expected JSON array for '{prop}'.");

    private static string RequireString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } s
            ? s
            : throw new InvalidDataException($"Expected string for '{prop}'.");

    private static string? OptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Guid RequireGuid(JsonElement el, string prop) =>
        Guid.TryParse(RequireString(el, prop), out var g) && g != Guid.Empty
            ? g
            : throw new InvalidDataException($"Expected non-empty UUID for '{prop}'.");

    private static Guid? OptionalGuid(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) && g != Guid.Empty
            ? g
            : null;

    private static int RequireInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i)
            ? i
            : throw new InvalidDataException($"Expected int32 for '{prop}'.");

    private static int? OptionalInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : null;

    private static long RequireLong(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt64(out var l)
            ? l
            : throw new InvalidDataException($"Expected int64 for '{prop}'.");

    private static decimal? OptionalDecimal(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)
            ? d
            : null;

    private static bool RequireBool(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : throw new InvalidDataException($"Expected bool for '{prop}'.");

    private static bool? OptionalBool(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    private static DateOnly RequireDateOnly(JsonElement el, string prop) =>
        DateOnly.TryParseExact(RequireString(el, prop), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new InvalidDataException($"Expected yyyy-MM-dd date for '{prop}'.");

    private static DateTimeOffset RequireDateTimeOffset(JsonElement el, string prop) =>
        DateTimeOffset.TryParse(RequireString(el, prop), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : throw new InvalidDataException($"Expected ISO timestamp for '{prop}'.");

    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : null;
}
