using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IStudentRecentObservationsReader
{
    Task<StudentRecentObservationsReadResult> ReadAsync(
        StudentObservationScope scope,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default);
}
