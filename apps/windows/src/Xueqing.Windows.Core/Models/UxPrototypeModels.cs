namespace Xueqing.Windows.Core.Models;

public enum TodayActionBucket
{
    Overdue,
    Today,
    Undated,
    Future,
}

public enum PrototypeInteractionState
{
    Committed,
    SavingLocally,
    LocalDraftSafe,
    WaitingToSync,
    Syncing,
    ServerRejected,
    OnlineCommandPending,
    ResultUnknown,
    VersionConflict,
    LocalSaveFailed,
    ProjectionRefreshing,
    PermissionReduced,
    SessionInvalid,
    ScopeSwitching,
    AttachmentUploadFailed,
    OfflineAccessAllowed,
    OfflineAccessUnavailable,
}

public sealed record TodayActionItem(
    string Id,
    string StudentId,
    string StudentDisplayName,
    string Subject,
    string Title,
    TodayActionBucket Bucket,
    DateOnly? DueDate,
    PrototypeInteractionState InteractionState)
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

    public string InteractionStateLabel => InteractionState switch
    {
        PrototypeInteractionState.Committed => "",
        PrototypeInteractionState.SavingLocally => "正在保存草稿…",
        PrototypeInteractionState.LocalDraftSafe => "草稿已保存在本机 · 尚未提交",
        PrototypeInteractionState.WaitingToSync => "待同步",
        PrototypeInteractionState.Syncing => "正在同步…",
        PrototypeInteractionState.ServerRejected => "未能提交 · 本机内容仍保留",
        PrototypeInteractionState.OnlineCommandPending => "正在提交…",
        PrototypeInteractionState.ResultUnknown => "正在确认结果…",
        PrototypeInteractionState.VersionConflict => "这条记录已发生变化 · 本机草稿仍保留",
        PrototypeInteractionState.LocalSaveFailed => "本机草稿保存失败 · 当前输入仍保留",
        PrototypeInteractionState.ProjectionRefreshing => "正在刷新 · 本机草稿仍保留",
        PrototypeInteractionState.PermissionReduced => "当前已无法继续提交 · 本机草稿仍保留",
        PrototypeInteractionState.SessionInvalid => "登录状态已失效 · 请重新登录",
        PrototypeInteractionState.ScopeSwitching => "正在切换机构…",
        PrototypeInteractionState.AttachmentUploadFailed => "附件上传失败 · 已记录文字仍保留",
        PrototypeInteractionState.OfflineAccessAllowed => "离线 · 当前仍可访问",
        PrototypeInteractionState.OfflineAccessUnavailable => "离线访问当前不可用",
        _ => "",
    };

    public bool AuthoritativeCompletionKnown => InteractionState == PrototypeInteractionState.Committed;

    public bool RequiresSameOperationIdentity => InteractionState == PrototypeInteractionState.ResultUnknown;

    public bool PreservesRecoverableLocalContent => InteractionState is
        PrototypeInteractionState.LocalDraftSafe or
        PrototypeInteractionState.WaitingToSync or
        PrototypeInteractionState.Syncing or
        PrototypeInteractionState.ServerRejected or
        PrototypeInteractionState.ResultUnknown or
        PrototypeInteractionState.VersionConflict or
        PrototypeInteractionState.LocalSaveFailed or
        PrototypeInteractionState.ProjectionRefreshing or
        PrototypeInteractionState.PermissionReduced or
        PrototypeInteractionState.AttachmentUploadFailed;
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
