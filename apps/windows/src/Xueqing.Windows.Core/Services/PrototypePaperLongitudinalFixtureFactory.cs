using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

/// <summary>
/// Deterministic fictional multi-week exam paper progression for prototype
/// desktop & MCP verification. Contains strictly fictional data only.
/// </summary>
public static class PrototypePaperLongitudinalFixtureFactory
{
    public static StudentPaperLongitudinalSnapshot Create(
        Guid studentId,
        string studentDisplayName,
        string subjectKey,
        Guid organizationId,
        Guid subjectProfileId,
        Guid assignmentId,
        IReadOnlyList<StudyPlanDraftSummary>? recentDrafts = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var windowStart = today.AddDays(-89);
        var nowUtc = DateTimeOffset.UtcNow;

        var isMath = subjectKey.Contains("数学", StringComparison.OrdinalIgnoreCase) ||
                     subjectKey.Contains("math", StringComparison.OrdinalIgnoreCase);

        var topicA = isMath ? "一元二次方程配方法" : "长文本因果关系";
        var topicB = isMath ? "应用题限制条件" : "题干限制条件圈画";
        var topicC = isMath ? "压轴几何综合" : "概括压缩表达";

        var p1Id = DeriveId(studentId, "paper-1");
        var p2Id = DeriveId(studentId, "paper-2");
        var p3Id = DeriveId(studentId, "paper-3");
        var p4Id = DeriveId(studentId, "paper-4");

        var papers = new List<PaperSubmissionSummary>
        {
            new(
                PaperSubmissionId: p4Id,
                SourceObservationId: null,
                PaperDate: today.AddDays(-2),
                PaperKind: PaperKind.Homework,
                Title: $"{subjectKey}周末巩固练习",
                GradingSource: PaperGradingSource.AiTentativeUnmarked,
                ConfirmationState: PaperConfirmationState.DraftReady,
                QuestionCount: 12,
                MistakeCount: 2,
                ScoreEarned: 78,
                ScoreTotal: 100,
                DurationMinutes: 40,
                ExtractionSummary: "识别出 2 处疑似失分，待核对确认。",
                Version: 2,
                CreatedAtServer: nowUtc.AddDays(-2),
                ConfirmedAtServer: null,
                Pages:
                [
                    new(1, DeriveId(p4Id, "att-1"), true, "teaching-attachments-v1", $"v1/org/{organizationId:D}/student/{studentId:D}/profile/{subjectProfileId:D}/observation/{p4Id:D}/attachment/{DeriveId(p4Id, "att-1"):D}", "image/jpeg", 184320),
                    new(2, DeriveId(p4Id, "att-2"), false, "teaching-attachments-v1", $"v1/org/{organizationId:D}/student/{studentId:D}/profile/{subjectProfileId:D}/observation/{p4Id:D}/attachment/{DeriveId(p4Id, "att-2"):D}", "image/jpeg", 192512),
                ],
                Mistakes:
                [
                    new(DeriveId(p4Id, "m-1"), 1, "Q5", [topicB], PaperErrorRootCause.MisreadCondition, PaperSectionPosition.Middle, null, false, "疑似遗漏题干中的范围限制词。", "ai_draft", null),
                    new(DeriveId(p4Id, "m-2"), 2, "Q12", [topicA, topicC], PaperErrorRootCause.TimePressure, PaperSectionPosition.Late, null, false, "末尾综合题第二问作答不完整。", "ai_draft", null),
                ]),
            new(
                PaperSubmissionId: p3Id,
                SourceObservationId: null,
                PaperDate: today.AddDays(-9),
                PaperKind: PaperKind.Quiz,
                Title: $"{subjectKey}随堂限时小测 #3",
                GradingSource: PaperGradingSource.TeacherMarked,
                ConfirmationState: PaperConfirmationState.HumanConfirmed,
                QuestionCount: 10,
                MistakeCount: 1,
                ScoreEarned: 90,
                ScoreTotal: 100,
                DurationMinutes: 25,
                ExtractionSummary: "基础与中档题全部稳住，仅末尾压轴题因控时偏紧失分。",
                Version: 3,
                CreatedAtServer: nowUtc.AddDays(-9),
                ConfirmedAtServer: nowUtc.AddDays(-9).AddHours(2),
                Pages:
                [
                    new(1, DeriveId(p3Id, "att-1"), true, "teaching-attachments-v1", $"v1/org/{organizationId:D}/student/{studentId:D}/profile/{subjectProfileId:D}/observation/{p3Id:D}/attachment/{DeriveId(p3Id, "att-1"):D}", "image/jpeg", 172032),
                ],
                Mistakes:
                [
                    new(DeriveId(p3Id, "m-1"), 1, "Q10", [topicA], PaperErrorRootCause.TimePressure, PaperSectionPosition.Late, 10, true, "末尾综合题思路正确但未写完结论步骤。", "human_confirmed", null),
                ]),
            new(
                PaperSubmissionId: p2Id,
                SourceObservationId: null,
                PaperDate: today.AddDays(-23),
                PaperKind: PaperKind.UnitTest,
                Title: $"{subjectKey}第二单元测验",
                GradingSource: PaperGradingSource.TeacherMarked,
                ConfirmationState: PaperConfirmationState.HumanConfirmed,
                QuestionCount: 16,
                MistakeCount: 3,
                ScoreEarned: 82,
                ScoreTotal: 100,
                DurationMinutes: 50,
                ExtractionSummary: "计算类失误较上月下降，但审题限制条件与后半段控时仍反复出现。",
                Version: 3,
                CreatedAtServer: nowUtc.AddDays(-23),
                ConfirmedAtServer: nowUtc.AddDays(-22),
                Pages:
                [
                    new(1, DeriveId(p2Id, "att-1"), true, "teaching-attachments-v1", $"v1/org/{organizationId:D}/student/{studentId:D}/profile/{subjectProfileId:D}/observation/{p2Id:D}/attachment/{DeriveId(p2Id, "att-1"):D}", "image/jpeg", 204800),
                    new(2, DeriveId(p2Id, "att-2"), false, "teaching-attachments-v1", $"v1/org/{organizationId:D}/student/{studentId:D}/profile/{subjectProfileId:D}/observation/{p2Id:D}/attachment/{DeriveId(p2Id, "att-2"):D}", "image/jpeg", 198656),
                ],
                Mistakes:
                [
                    new(DeriveId(p2Id, "m-1"), 1, "Q7", [topicB], PaperErrorRootCause.MisreadCondition, PaperSectionPosition.Middle, 5, true, "漏看否定/边界限定词。", "human_confirmed", null),
                    new(DeriveId(p2Id, "m-2"), 2, "Q15", [topicA], PaperErrorRootCause.StrategyStuck, PaperSectionPosition.Late, 6, true, "跨段/跨步骤迁移时中间推导断链。", "human_confirmed", null),
                    new(DeriveId(p2Id, "m-3"), 2, "Q16", [topicA, topicC], PaperErrorRootCause.TimePressure, PaperSectionPosition.Late, 7, false, "最后 8 分钟仓促收尾。", "human_confirmed", null),
                ]),
            new(
                PaperSubmissionId: p1Id,
                SourceObservationId: null,
                PaperDate: today.AddDays(-45),
                PaperKind: PaperKind.UnitTest,
                Title: $"{subjectKey}第一单元测验",
                GradingSource: PaperGradingSource.TeacherMarked,
                ConfirmationState: PaperConfirmationState.HumanConfirmed,
                QuestionCount: 15,
                MistakeCount: 4,
                ScoreEarned: 74,
                ScoreTotal: 100,
                DurationMinutes: 50,
                ExtractionSummary: "粗心与审题并存，后半段综合题失分集中。",
                Version: 3,
                CreatedAtServer: nowUtc.AddDays(-45),
                ConfirmedAtServer: nowUtc.AddDays(-44),
                Pages:
                [
                    new(1, DeriveId(p1Id, "att-1"), true, "teaching-attachments-v1", $"v1/org/{organizationId:D}/student/{studentId:D}/profile/{subjectProfileId:D}/observation/{p1Id:D}/attachment/{DeriveId(p1Id, "att-1"):D}", "image/jpeg", 210944),
                ],
                Mistakes:
                [
                    new(DeriveId(p1Id, "m-1"), 1, "Q3", ["基础稳固"], PaperErrorRootCause.CalculationSlip, PaperSectionPosition.Early, 4, true, "基础运算符号抄错。", "human_confirmed", null),
                    new(DeriveId(p1Id, "m-2"), 1, "Q9", [topicB], PaperErrorRootCause.MisreadCondition, PaperSectionPosition.Middle, 6, true, "未圈画关键限定条件。", "human_confirmed", null),
                    new(DeriveId(p1Id, "m-3"), 1, "Q14", [topicA], PaperErrorRootCause.ConceptGap, PaperSectionPosition.Late, 8, true, "核心关系组织不清晰。", "human_confirmed", null),
                    new(DeriveId(p1Id, "m-4"), 1, "Q15", [topicC], PaperErrorRootCause.TimePressure, PaperSectionPosition.Late, 8, false, "末题时间不足只写一半。", "human_confirmed", null),
                ]),
        };

        var summary = new PaperLongitudinalSummary(
            TotalPapers: 4,
            ConfirmedPapers: 3,
            DraftReadyPapers: 1,
            AwaitingExtractionPapers: 0,
            TotalMistakesConfirmed: 8,
            TotalMistakesAll: 10,
            TeacherMarkedAvgScoreRatio: 0.8200m,
            LateSectionMistakeRatio: 0.6000m);

        var weeklyCadence = papers
            .GroupBy(p => StartOfIsoWeek(p.PaperDate))
            .OrderBy(g => g.Key)
            .Select(g => new PaperWeeklyCadencePoint(
                WeekStart: g.Key,
                PaperCount: g.Count(),
                ConfirmedCount: g.Count(x => x.ConfirmationState == PaperConfirmationState.HumanConfirmed),
                MistakeCount: g.Sum(x => x.MistakeCount ?? 0),
                HomeworkCount: g.Count(x => x.PaperKind == PaperKind.Homework),
                QuizCount: g.Count(x => x.PaperKind == PaperKind.Quiz),
                UnitTestCount: g.Count(x => x.PaperKind == PaperKind.UnitTest),
                ExamCount: g.Count(x => x.PaperKind == PaperKind.Exam)))
            .ToArray();

        var recent30dStart = today.AddDays(-29);
        var allMistakesWithDate = papers
            .SelectMany(p => p.Mistakes.Select(m => (Paper: p, Mistake: m)))
            .ToArray();

        var rootCauses = allMistakesWithDate
            .GroupBy(x => x.Mistake.ErrorRootCause)
            .Select(g => new PaperRootCauseTrend(
                ErrorRootCause: g.Key,
                Recent30dCount: g.Count(x => x.Paper.PaperDate >= recent30dStart),
                PriorWindowCount: g.Count(x => x.Paper.PaperDate < recent30dStart),
                TotalCount: g.Count()))
            .OrderByDescending(x => x.TotalCount)
            .ToArray();

        var recurringTopics = allMistakesWithDate
            .SelectMany(x => x.Mistake.TopicTags.Select(t => (Tag: t, Paper: x.Paper)))
            .GroupBy(x => x.Tag)
            .Select(g => new PaperRecurringTopic(
                TopicTag: g.Key,
                OccurrenceCount: g.Count(),
                PaperCount: g.Select(x => x.Paper.PaperSubmissionId).Distinct().Count(),
                FirstSeenDate: g.Min(x => x.Paper.PaperDate),
                LastSeenDate: g.Max(x => x.Paper.PaperDate),
                LinkedCaseIds: Array.Empty<Guid>()))
            .OrderByDescending(x => x.OccurrenceCount)
            .ThenByDescending(x => x.LastSeenDate)
            .ToArray();

        return new StudentPaperLongitudinalSnapshot(
            GeneratedAtServer: nowUtc,
            ActorAppUserId: Guid.Parse("10000000-0000-0000-0000-000000000001"),
            OrganizationId: organizationId,
            OrganizationName: "测试机构",
            OrganizationTimeZone: "Asia/Shanghai",
            OrganizationBusinessDate: today,
            StudentId: studentId,
            StudentDisplayName: studentDisplayName,
            SubjectProfileId: subjectProfileId,
            SubjectKey: subjectKey,
            AssignmentId: assignmentId,
            WindowDays: 90,
            WindowStartDate: windowStart,
            Summary: summary,
            WeeklyCadence: weeklyCadence,
            RootCauseBreakdown: rootCauses,
            RecurringTopics: recurringTopics,
            Papers: papers,
            RecentStudyPlanDrafts: recentDrafts ?? Array.Empty<StudyPlanDraftSummary>());
    }

    private static DateOnly StartOfIsoWeek(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-diff);
    }

    private static Guid DeriveId(Guid seed, string salt)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"xueqing-proto-paper-v1/{seed:D}/{salt}"));
        var guidBytes = new byte[16];
        Array.Copy(bytes, 0, guidBytes, 0, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
