using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IOrganizationInvitationDeliveryCommand
{
    Task<DeliverOrganizationInvitationResult> ExecuteAsync(
        DeliverOrganizationInvitationRequest request,
        CancellationToken cancellationToken = default);
}
