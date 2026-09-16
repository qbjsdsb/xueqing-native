using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public static class SyntheticDataFactory
{
    private static readonly string[] Subjects = ["语文", "数学", "英语", "物理"];

    public static IReadOnlyList<StudentSummary> CreateStudents(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var students = new StudentSummary[count];
        for (var index = 0; index < count; index++)
        {
            var ordinal = index + 1;
            students[index] = new StudentSummary(
                Id: $"student-{ordinal:000000}",
                DisplayName: $"虚构学生{ordinal:0000}",
                StudentCode: $"S{ordinal:000000}",
                PrimarySubject: Subjects[index % Subjects.Length],
                ActiveCaseCount: index % 5);
        }

        return students;
    }
}
