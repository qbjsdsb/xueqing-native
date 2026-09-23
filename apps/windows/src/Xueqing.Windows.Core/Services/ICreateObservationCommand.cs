using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface ICreateObservationCommand
{
    Task<CreateObservationResult> ExecuteAsync(
        CreateObservationRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default);
}
