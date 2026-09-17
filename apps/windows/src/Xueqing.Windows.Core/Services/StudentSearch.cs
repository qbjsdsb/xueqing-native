using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public static class StudentSearch
{
    public static IReadOnlyList<StudentSummary> Filter(
        IEnumerable<StudentSummary> students,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(students);

        var normalized = query?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return students.ToArray();
        }

        return students
            .Where(student =>
                student.DisplayName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                student.StudentCode.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                student.PrimarySubject.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
