using System.Text.Json;

namespace Xueqing.Windows.Core.Models;

public enum PaperKind
{
    Homework,
    Quiz,
    UnitTest,
    Exam,
}

public enum PaperGradingSource
{
    TeacherMarked,
    AiTentativeUnmarked,
    HumanVerified,
}

public enum PaperConfirmationState
{
    AwaitingExtraction,
    DraftReady,
    HumanConfirmed,
    ExtractionFailed,
}

public enum PaperErrorRootCause
{
    ConceptGap,
    MisreadCondition,
    StrategyStuck,
    CalculationSlip,
    TimePressure,
    Unclassified,
}

public enum PaperSectionPosition
{
    Early,
    Middle,
    Late,
    Unknown,
}

public sealed record PaperPageSummary(
    int PageIndex,
    Guid AttachmentId,
    bool HeaderRedacted,
    string BucketId,
    string ObjectName,
    string ContentType,
    long ByteSize);

public sealed record PaperMistakeItemSummary(
    Guid MistakeId,
    int PageIndex,
    string QuestionLabel,
    IReadOnlyList<string> TopicTags,
    PaperErrorRootCause ErrorRootCause,
    PaperSectionPosition PaperSectionPosition,
    decimal? PointsLost,
    bool? CorrectedOnPaper,
    string Summary,
    string Provenance,
    Guid? LinkedCaseId);

public sealed record PaperSubmissionSummary(
    Guid PaperSubmissionId,
    Guid? SourceObservationId,
    DateOnly PaperDate,
    PaperKind PaperKind,
    string? Title,
    PaperGradingSource GradingSource,
    PaperConfirmationState ConfirmationState,
    int? QuestionCount,
    int? MistakeCount,
    decimal? ScoreEarned,
    decimal? ScoreTotal,
    int? DurationMinutes,
    string? ExtractionSummary,
    long Version,
    DateTimeOffset CreatedAtServer,
    DateTimeOffset? ConfirmedAtServer,
    IReadOnlyList<PaperPageSummary> Pages,
    IReadOnlyList<PaperMistakeItemSummary> Mistakes)
{
    public string KindLabel => PaperKind switch
    {
        PaperKind.Homework => "日常作业",
        PaperKind.Quiz => "随堂小测",
        PaperKind.UnitTest => "单元周测",
        PaperKind.Exam => "期中期末",
        _ => "试卷",
    };

    public string ConfirmationLabel => ConfirmationState switch
    {
        PaperConfirmationState.AwaitingExtraction => "待解析",
        PaperConfirmationState.DraftReady => "AI 草案待核对",
        PaperConfirmationState.HumanConfirmed => "已确认",
        PaperConfirmationState.ExtractionFailed => "解析失败",
        _ => "未知",
    };

    public string ScoreDisplay =>
        ScoreEarned is { } earned && ScoreTotal is { } total
            ? GradingSource == PaperGradingSource.AiTentativeUnmarked
                ? $"{earned:0.##}/{total:0.##}（AI 疑似未核对）"
                : $"{earned:0.##}/{total:0.##}"
            : "未计分";
}

public sealed record PaperLongitudinalSummary(
    int TotalPapers,
    int ConfirmedPapers,
    int DraftReadyPapers,
    int AwaitingExtractionPapers,
    int TotalMistakesConfirmed,
    int TotalMistakesAll,
    decimal? TeacherMarkedAvgScoreRatio,
    decimal? LateSectionMistakeRatio,
    decimal? DraftAvgScoreRatio = null,
    decimal? ConfirmedLateSectionMistakeRatio = null,
    int? PriorWindowDays = null);

public sealed record PaperWeeklyCadencePoint(
    DateOnly WeekStart,
    int PaperCount,
    int ConfirmedCount,
    int MistakeCount,
    int HomeworkCount,
    int QuizCount,
    int UnitTestCount,
    int ExamCount);

public sealed record PaperRootCauseTrend(
    PaperErrorRootCause ErrorRootCause,
    int Recent30dCount,
    int PriorWindowCount,
    int TotalCount)
{
    public string Label => ErrorRootCause switch
    {
        PaperErrorRootCause.ConceptGap => "概念不清",
        PaperErrorRootCause.MisreadCondition => "审题漏条件",
        PaperErrorRootCause.StrategyStuck => "思路卡壳",
        PaperErrorRootCause.CalculationSlip => "计算失误",
        PaperErrorRootCause.TimePressure => "时间不够",
        _ => "未分类",
    };
}

public sealed record PaperRecurringTopic(
    string TopicTag,
    int OccurrenceCount,
    int PaperCount,
    DateOnly FirstSeenDate,
    DateOnly LastSeenDate,
    IReadOnlyList<Guid> LinkedCaseIds);

public sealed record StudyPlanDraftSummary(
    Guid DraftId,
    string Title,
    string BriefMarkdown,
    string EvidenceRefsJson,
    string ProposedActionsJson,
    int WindowDays,
    int PaperCountAnalyzed,
    string Status,
    long Version,
    DateTimeOffset CreatedAtServer,
    DateTimeOffset UpdatedAtServer);

public sealed record StudentPaperLongitudinalSnapshot(
    DateTimeOffset GeneratedAtServer,
    Guid ActorAppUserId,
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationTimeZone,
    DateOnly OrganizationBusinessDate,
    Guid StudentId,
    string StudentDisplayName,
    Guid SubjectProfileId,
    string SubjectKey,
    Guid AssignmentId,
    int WindowDays,
    DateOnly WindowStartDate,
    PaperLongitudinalSummary Summary,
    IReadOnlyList<PaperWeeklyCadencePoint> WeeklyCadence,
    IReadOnlyList<PaperRootCauseTrend> RootCauseBreakdown,
    IReadOnlyList<PaperRecurringTopic> RecurringTopics,
    IReadOnlyList<PaperSubmissionSummary> Papers,
    IReadOnlyList<StudyPlanDraftSummary> RecentStudyPlanDrafts);

public sealed record SaveStudyPlanDraftRequest(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid AssignmentId,
    string Title,
    string BriefMarkdown,
    string EvidenceRefsJson,
    string ProposedActionsJson,
    int WindowDays,
    int PaperCountAnalyzed);

public sealed record SaveStudyPlanDraftReceipt(
    Guid OperationId,
    Guid DraftId,
    string Status,
    long Version,
    DateTimeOffset ServerCommittedAt);

public sealed record DownloadedAttachmentImage(
    string ContentType,
    byte[] Bytes);

public sealed record TriggerPaperExtractionRequest(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid PaperSubmissionId,
    long? ExpectedVersion = null);

public sealed record TriggerPaperExtractionReceipt(
    Guid OperationId,
    Guid PaperSubmissionId,
    string Model,
    int PagesAnalyzed,
    string ConfirmationState,
    string GradingSource,
    long Version,
    int MistakeItemsSaved,
    DateTimeOffset? ServerCommittedAt);

public sealed record PaperMistakeDraftInput(
    int PageIndex,
    string QuestionLabel,
    IReadOnlyList<string> TopicTags,
    PaperErrorRootCause ErrorRootCause,
    PaperSectionPosition PaperSectionPosition,
    decimal? PointsLost,
    bool? CorrectedOnPaper,
    string Summary,
    Guid? LinkedCaseId = null);

public sealed record SavePaperExtractionRequest(
    Guid OperationId,
    Guid PaperSubmissionId,
    long ExpectedVersion,
    string Mode,
    PaperGradingSource GradingSource,
    string? Title,
    int? QuestionCount,
    int? MistakeCount,
    decimal? ScoreEarned,
    decimal? ScoreTotal,
    int? DurationMinutes,
    string? ExtractionSummary,
    IReadOnlyList<PaperMistakeDraftInput> Mistakes);

public sealed record SavePaperExtractionReceipt(
    Guid OperationId,
    Guid PaperSubmissionId,
    string ConfirmationState,
    string GradingSource,
    long Version,
    int MistakeItemsSaved,
    DateTimeOffset ServerCommittedAt);

public sealed record UpdateStudyPlanDraftStatusRequest(
    Guid OperationId,
    Guid DraftId,
    long ExpectedVersion,
    string NextStatus);

public sealed record UpdateStudyPlanDraftStatusReceipt(
    Guid OperationId,
    Guid DraftId,
    string Status,
    long Version,
    DateTimeOffset ServerCommittedAt);

public sealed record StudyPlanProposedAction(
    string Kind,
    string Title,
    string NextAction,
    string? TargetDate,
    string? Rationale)
{
    public string KindLabel => Kind switch
    {
        "create_learning_case" => "新建跟进课题",
        "next_action" or "add_action" => "新增下一步行动",
        "review_practice" => "专项复练",
        _ => "教学行动建议",
    };
}

public static class PaperEvidenceWireNames
{
    public static string ToWire(PaperKind kind) => kind switch
    {
        PaperKind.Homework => "homework",
        PaperKind.Quiz => "quiz",
        PaperKind.UnitTest => "unit_test",
        PaperKind.Exam => "exam",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string ToWire(PaperGradingSource source) => source switch
    {
        PaperGradingSource.TeacherMarked => "teacher_marked",
        PaperGradingSource.AiTentativeUnmarked => "ai_tentative_unmarked",
        PaperGradingSource.HumanVerified => "human_verified",
        _ => "teacher_marked",
    };

    public static string ToWire(PaperErrorRootCause cause) => cause switch
    {
        PaperErrorRootCause.ConceptGap => "concept_gap",
        PaperErrorRootCause.MisreadCondition => "misread_condition",
        PaperErrorRootCause.StrategyStuck => "strategy_stuck",
        PaperErrorRootCause.CalculationSlip => "calculation_slip",
        PaperErrorRootCause.TimePressure => "time_pressure",
        PaperErrorRootCause.Unclassified => "unclassified",
        _ => "unclassified",
    };

    public static string ToWire(PaperSectionPosition pos) => pos switch
    {
        PaperSectionPosition.Early => "early",
        PaperSectionPosition.Middle => "middle",
        PaperSectionPosition.Late => "late",
        _ => "unknown",
    };

    public static string ToChineseLabel(PaperErrorRootCause cause) => cause switch
    {
        PaperErrorRootCause.ConceptGap => "概念不清",
        PaperErrorRootCause.MisreadCondition => "审题漏条件",
        PaperErrorRootCause.StrategyStuck => "思路卡壳",
        PaperErrorRootCause.CalculationSlip => "计算失误",
        PaperErrorRootCause.TimePressure => "时间不够",
        _ => "未分类",
    };

    public static string ToChineseLabel(PaperSectionPosition pos) => pos switch
    {
        PaperSectionPosition.Early => "卷首基础题",
        PaperSectionPosition.Middle => "卷中中等题",
        PaperSectionPosition.Late => "卷尾综合/大题",
        _ => "位置未知",
    };

    public static string ToChineseGradingSource(PaperGradingSource source) => source switch
    {
        PaperGradingSource.TeacherMarked => "老师已批改",
        PaperGradingSource.HumanVerified => "人工复核确认",
        PaperGradingSource.AiTentativeUnmarked => "未批改（AI 试判）",
        _ => "未知来源",
    };

    public static string ToChineseProvenance(string? provenance) => provenance?.Trim().ToLowerInvariant() switch
    {
        "human_confirmed" => "已人工确认",
        "ai_draft" => "AI 识别草稿",
        _ => "待确认",
    };

    public static string ToChineseStudyPlanStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "draft" => "待审阅草案",
        "partially_adopted" => "已部分采纳",
        "fully_adopted" => "已全部采纳",
        "dismissed" => "已忽略",
        _ => "待审阅",
    };

    public static IReadOnlyList<StudyPlanProposedAction> ParseProposedActions(string? proposedActionsJson)
    {
        if (string.IsNullOrWhiteSpace(proposedActionsJson))
        {
            return Array.Empty<StudyPlanProposedAction>();
        }

        try
        {
            using var doc = JsonDocument.Parse(proposedActionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<StudyPlanProposedAction>();
            }

            var results = new List<StudyPlanProposedAction>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var kind = ReadOptionalString(item, "kind") ?? "next_action";
                var title = ReadOptionalString(item, "title")
                    ?? ReadOptionalString(item, "case_title")
                    ?? ReadOptionalString(item, "topic")
                    ?? "阶段跟进建议";
                var nextAction = ReadOptionalString(item, "next_action")
                    ?? ReadOptionalString(item, "action")
                    ?? ReadOptionalString(item, "description")
                    ?? title;
                var targetDate = ReadOptionalString(item, "due_on")
                    ?? ReadOptionalString(item, "target_date");
                var rationale = ReadOptionalString(item, "rationale")
                    ?? ReadOptionalString(item, "reason")
                    ?? ReadOptionalString(item, "evidence");

                results.Add(new StudyPlanProposedAction(kind, title, nextAction, targetDate, rationale));
            }

            return results;
        }
        catch (JsonException)
        {
            return Array.Empty<StudyPlanProposedAction>();
        }
    }

    private static string? ReadOptionalString(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
