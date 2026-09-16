namespace Xueqing.Windows.Core.Models;

public sealed record StudentSummary(
    string Id,
    string DisplayName,
    string StudentCode,
    string PrimarySubject,
    int ActiveCaseCount);
