using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IStudentLearningFocusReader
{
    Task<StudentLearningFocusReadResult> ReadAsync(
        StudentLearningScope scope,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default);
}
