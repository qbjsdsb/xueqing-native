using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IOrganizationInvitationRecoveryStore
{
    Task SaveAsync(
        Guid actorAppUserId,
        OrganizationInvitationRecoveryIntent intent,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationInvitationRecoveryIntent>> ListAsync(
        Guid actorAppUserId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid createOperationId,
        CancellationToken cancellationToken = default);
}
