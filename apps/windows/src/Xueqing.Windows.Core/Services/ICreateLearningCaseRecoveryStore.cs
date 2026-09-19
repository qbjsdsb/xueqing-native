using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface ICreateLearningCaseRecoveryStore
{
    Task SaveAsync(
        Guid actorAppUserId,
        CreateLearningCaseRequest request,
        CancellationToken cancellationToken = default);

    Task<CreateLearningCaseRequest?> FindBySourceObservationAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid sourceObservationId,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default);
}
