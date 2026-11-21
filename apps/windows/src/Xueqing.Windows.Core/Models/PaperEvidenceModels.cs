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
    decimal? LateSectionMistakeRatio);

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
}
