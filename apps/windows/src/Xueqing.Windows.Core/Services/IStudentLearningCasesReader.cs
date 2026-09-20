using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IStudentLearningCasesReader
{
    Task<StudentLearningCasesReadResult> ReadAsync(
        StudentLearningScope scope,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default);
}
