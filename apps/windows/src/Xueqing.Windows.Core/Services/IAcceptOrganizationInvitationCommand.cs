using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IAcceptOrganizationInvitationCommand
{
    Task<AcceptOrganizationInvitationResult> ExecuteAsync(
        AcceptOrganizationInvitationRequest request,
        CancellationToken cancellationToken = default);
}
