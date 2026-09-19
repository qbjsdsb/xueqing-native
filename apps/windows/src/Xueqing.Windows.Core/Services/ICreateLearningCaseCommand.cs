using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface ICreateLearningCaseCommand
{
    Task<CreateLearningCaseResult> ExecuteAsync(
        CreateLearningCaseRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default);
}
