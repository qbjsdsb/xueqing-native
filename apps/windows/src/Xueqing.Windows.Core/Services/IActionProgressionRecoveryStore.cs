using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IActionProgressionRecoveryStore
{
    Task SaveAsync(
        Guid actorAppUserId,
        ActionProgressionRecoveryIntent intent,
        CancellationToken cancellationToken = default);

    Task<ActionProgressionRecoveryIntent?> FindByPrimaryActionAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid caseId,
        Guid primaryActionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActionProgressionRecoveryIntent>> ListPendingAsync(
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
