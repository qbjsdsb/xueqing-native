namespace Xueqing.Windows.Core.Models;

public sealed record StudentSummary(
    string Id,
    string DisplayName,
    string StudentCode,
    string PrimarySubject,
    int ActiveCaseCount)
{
    public string SecondaryLabel => string.IsNullOrWhiteSpace(StudentCode)
        ? "当前任教学员"
        : StudentCode;

    public string ActiveCaseCountLabel => ActiveCaseCount < 0
        ? string.Empty
        : ActiveCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
