using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface ICaseLifecycleRecoveryStore
{
    Task SaveAsync(
        Guid actorAppUserId,
        CaseLifecycleRecoveryIntent intent,
        CancellationToken cancellationToken = default);

    Task<CaseLifecycleRecoveryIntent?> FindByCaseAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid caseId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CaseLifecycleRecoveryIntent>> ListPendingAsync(
        Guid actorAppUserId,
        IReadOnlyCollection<Guid> currentOrganizationIds,
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
