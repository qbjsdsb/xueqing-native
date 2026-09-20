using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.ViewModels;

public sealed record CaseLifecycleRecoveryLookup(
    bool IsAvailable,
    CaseLifecycleTarget? Target,
    CaseLifecycleRecoveryIntent? PendingIntent);

public sealed record CaseLifecycleRetryResult(
    bool IsSuccess,
    CaseLifecycleFailure? Failure);

public sealed record PendingCaseLifecycleRecoveryItem(
    CaseLifecycleRecoveryIntent Intent,
    string StudentDisplayName,
    string SubjectDisplayName)
{
    public string Heading => Intent.Kind switch
    {
        CaseLifecycleRecoveryIntentKind.TransitionState => "尚未确认的 Case 状态变更",
        CaseLifecycleRecoveryIntentKind.CloseCase => "尚未确认的 Case 关闭",
        CaseLifecycleRecoveryIntentKind.ReopenCase => "尚未确认的 Case 重新打开",
        _ => "尚未确认的 Case 操作",
    };

    public string Detail => Intent switch
    {
        TransitionCaseRecoveryIntent transition =>
            $"目标状态：{StateLabel(transition.Request.TargetState)}",
        CloseCaseRecoveryIntent =>
            "关闭 Case，并取消当时的待办主行动",
        ReopenCaseRecoveryIntent reopen =>
            $"重新打开 · 下一步：{reopen.Request.NewPrimaryActionText}",
        _ => string.Empty,
    };

    private static string StateLabel(LearningCaseState state) => state switch
    {
        LearningCaseState.Confirmed => "已确认",
        LearningCaseState.Intervening => "跟进中",
        LearningCaseState.PendingVerification => "待验证",
        LearningCaseState.Stable => "稳定",
        LearningCaseState.Closed => "已关闭",
        _ => state.ToString(),
    };
}
