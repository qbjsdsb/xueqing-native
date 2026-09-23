namespace Xueqing.Windows.Core.Models;

public sealed record ObservationQuickCaptureOpenState(
    ObservationDraftOpenResult Draft,
    CreateObservationRequest? PendingIntent)
{
    public bool HasPendingIntent => PendingIntent is not null;
    public string DisplayText => PendingIntent?.RawText ?? Draft.Recovered?.Text ?? string.Empty;
}
