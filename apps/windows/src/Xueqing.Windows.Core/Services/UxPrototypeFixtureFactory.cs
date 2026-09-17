using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public static class UxPrototypeFixtureFactory
{
    private static readonly DateOnly FixtureToday = new(2026, 9, 17);

    public static IReadOnlyList<TodayActionItem> CreateTodayActions()
    {
        var states = Enum.GetValues<PrototypeInteractionState>();

        var result = new List<TodayActionItem>(28);
        for (var index = 0; index < 28; index++)
        {
            var ordinal = index + 1;
            var bucket = index switch
            {
                < 7 => TodayActionBucket.Overdue,
                < 14 => TodayActionBucket.Today,
                < 20 => TodayActionBucket.Undated,
                _ => TodayActionBucket.Future,
            };

            DateOnly? dueDate = bucket switch
            {
                TodayActionBucket.Overdue => FixtureToday.AddDays(-(index % 5 + 1)),
                TodayActionBucket.Today => FixtureToday,
                TodayActionBucket.Undated => null,
                TodayActionBucket.Future => FixtureToday.AddDays(index % 6 + 1),
                _ => null,
            };

            var studentOrdinal = index < 5 ? 7 : ((index * 37) % 1_000) + 1;
            var title = index % 6 == 0
                ? "复核本周阅读训练中长篇文本信息筛选与概括策略是否能够迁移到新的陌生材料，并记录可观察证据"
                : $"跟进教学行动 {ordinal:00}：确认上一次干预后的可观察变化";

            result.Add(new TodayActionItem(
                Id: $"action-{ordinal:000}",
                StudentId: $"student-{studentOrdinal:000000}",
                StudentDisplayName: $"虚构学生{studentOrdinal:0000}",
                Subject: index % 2 == 0 ? "语文" : "数学",
                Title: title,
                Bucket: bucket,
                DueDate: dueDate,
                InteractionState: states[index % states.Length]));
        }

        return result;
    }

    public static LearningCasePrototype CreateLearningCase()
    {
        var entries = new List<CaseTimelineEntry>(100);
        var kinds = Enum.GetValues<CaseTimelineEntryKind>();
        var anchor = new DateTimeOffset(2026, 9, 17, 9, 30, 0, TimeSpan.FromHours(8));

        for (var index = 0; index < 100; index++)
        {
            var ordinal = index + 1;
            var kind = kinds[index % kinds.Length];
            var body = index % 9 == 0
                ? "在新的陌生文本中能够主动定位关键限制条件，但对多层因果关系的组织仍需要教师提示。记录保留原始观察、干预过程与验证结果，不用总结性标签替代事实。"
                : $"第 {ordinal:000} 条确定性虚构教学记录，用于验证长时间线、文本换行、阅读节奏和历史可追溯性。";

            entries.Add(new CaseTimelineEntry(
                Id: $"timeline-{ordinal:000}",
                OccurredAt: anchor.AddHours(-ordinal * 6),
                Kind: kind,
                Heading: kind switch
                {
                    CaseTimelineEntryKind.Observation => "课堂观察",
                    CaseTimelineEntryKind.Evidence => "学习证据",
                    CaseTimelineEntryKind.Intervention => "教学干预",
                    CaseTimelineEntryKind.Assessment => "验证记录",
                    CaseTimelineEntryKind.Lifecycle => index == 4 ? "重新打开" : "状态记录",
                    _ => "记录",
                },
                Body: body,
                ActorDisplayName: $"虚构教师{index % 4 + 1}"));
        }

        return new LearningCasePrototype(
            Id: "case-fictional-001",
            StudentDisplayName: "虚构学生0007",
            Subject: "语文",
            Title: "长文本阅读中多层信息关系的提取与组织",
            StateLabel: "待验证",
            ResponsibleTeacher: "虚构教师1",
            NextAction: "下次课使用不同体裁材料进行迁移验证，并只记录可观察证据。",
            Timeline: entries);
    }

    public static IReadOnlyList<OrganizationMemberRow> CreateOrganizationMembers(int count = 80)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var roles = new[] { "负责人 / 任课教师", "管理员", "语文教师", "数学教师" };
        var statuses = new[] { "正常", "邀请待接受", "权限已调整", "已停用" };

        var rows = new OrganizationMemberRow[count];
        for (var index = 0; index < count; index++)
        {
            var ordinal = index + 1;
            rows[index] = new OrganizationMemberRow(
                Id: $"member-{ordinal:000}",
                DisplayName: index % 11 == 0 ? $"虚构教师姓名较长用于列宽降级验证{ordinal:00}" : $"虚构教师{ordinal:00}",
                RoleLabel: roles[index % roles.Length],
                StatusLabel: statuses[index % statuses.Length],
                RecentActivity: $"2026-09-{17 - index % 12:00} · 更新虚构教学责任范围",
                CanUseBulkSafeAction: index % 3 == 0);
        }

        return rows;
    }
}
