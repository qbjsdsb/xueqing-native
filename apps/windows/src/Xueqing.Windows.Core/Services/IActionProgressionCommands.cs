using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IReschedulePrimaryActionCommand
{
    Task<ActionProgressionResult<ReschedulePrimaryActionReceipt>> ExecuteAsync(
        ReschedulePrimaryActionRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default);
}

public interface IRecordVerificationAndNextActionCommand
{
    Task<ActionProgressionResult<RecordVerificationAndNextActionReceipt>> ExecuteAsync(
        RecordVerificationAndNextActionRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default);
}
