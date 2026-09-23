using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface ICreateObservationRecoveryStore
{
    Task SaveAsync(
        Guid actorAppUserId,
        CreateObservationRequest request,
        CancellationToken cancellationToken = default);

    Task<CreateObservationRequest?> FindPendingAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        CancellationToken cancellationToken = default);

    Task MarkRejectedAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default);
}
