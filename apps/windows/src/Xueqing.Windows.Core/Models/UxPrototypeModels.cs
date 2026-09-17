namespace Xueqing.Windows.Core.Models;

public enum TodayActionBucket
{
    Overdue,
    Today,
    Undated,
    Future,
}

public enum PrototypeSaveState
{
    Ready,
    Saving,
    SaveFailed,
    PendingSync,
    ReadOnly,
}

public sealed record TodayActionItem(
    string Id,
    string StudentId,
    string StudentDisplayName,
    string Subject,
    string Title,
    TodayActionBucket Bucket,
    DateOnly? DueDate,
    PrototypeSaveState SaveState)
{
    public string BucketLabel => Bucket switch
    {
        TodayActionBucket.Overdue => "逾期",
        TodayActionBucket.Today => "今天",
        TodayActionBucket.Undated => "待安排",
        TodayActionBucket.Future => "之后",
        _ => "",
    };

    public string DueLabel => DueDate is null
        ? "未安排日期"
        : DueDate.Value.ToString("MM月dd日");

    public string SaveStateLabel => SaveState switch
    {
        PrototypeSaveState.Ready => "",
        PrototypeSaveState.Saving => "正在保存…",
        PrototypeSaveState.SaveFailed => "保存失败 · 可重试",
        PrototypeSaveState.PendingSync => "已安全保存在本地 · 等待同步",
        PrototypeSaveState.ReadOnly => "只读 · 权限已变化",
        _ => "",
    };
}

public enum CaseTimelineEntryKind
{
    Observation,
    Evidence,
    Intervention,
    Assessment,
    Lifecycle,
}

public sealed record CaseTimelineEntry(
    string Id,
    DateTimeOffset OccurredAt,
    CaseTimelineEntryKind Kind,
    string Heading,
    string Body,
    string ActorDisplayName)
{
    public string DateLabel => OccurredAt.ToString("MM月dd日 HH:mm");
}

public sealed record LearningCasePrototype(
    string Id,
    string StudentDisplayName,
    string Subject,
    string Title,
    string StateLabel,
    string ResponsibleTeacher,
    string NextAction,
    IReadOnlyList<CaseTimelineEntry> Timeline);

public sealed record OrganizationMemberRow(
    string Id,
    string DisplayName,
    string RoleLabel,
    string StatusLabel,
    string RecentActivity,
    bool CanUseBulkSafeAction)
{
    public string BulkSafetyLabel => CanUseBulkSafeAction ? "可安全批量操作" : "需单独确认";
}
