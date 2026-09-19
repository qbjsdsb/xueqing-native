using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IPersonalTodayActionsReader
{
    Task<PersonalTodayActionsReadResult> ReadAsync(
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default);
}
