using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface ITransitionLearningCaseStateCommand
{
    Task<CaseLifecycleResult<TransitionLearningCaseStateReceipt>> ExecuteAsync(
        TransitionLearningCaseStateRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default);
}

public interface ICloseLearningCaseCommand
{
    Task<CaseLifecycleResult<CloseLearningCaseReceipt>> ExecuteAsync(
        CloseLearningCaseRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default);
}

public interface IReopenLearningCaseCommand
{
    Task<CaseLifecycleResult<ReopenLearningCaseReceipt>> ExecuteAsync(
        ReopenLearningCaseRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default);
}
