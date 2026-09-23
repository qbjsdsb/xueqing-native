using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IObservationDraftStore
{
    Task<ObservationDraftOpenResult> OpenAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        CancellationToken cancellationToken = default);

    Task<bool> SaveAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        long expectedEpoch,
        string text,
        CancellationToken cancellationToken = default);

    Task<long?> DiscardAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        long expectedEpoch,
        CancellationToken cancellationToken = default);
}
