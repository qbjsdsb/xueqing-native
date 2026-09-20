using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IOrganizationInvitationCommand
{
    Task<CreateOrganizationInvitationResult> ExecuteAsync(
        CreateOrganizationInvitationRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default);
}
