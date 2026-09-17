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
    PrototypeSaveState SaveState);

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
    string ActorDisplayName);

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
    bool CanUseBulkSafeAction);
