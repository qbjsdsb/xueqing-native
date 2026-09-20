namespace Xueqing.Windows.Core.Models;

public sealed record StudentLearningCaseSummary(
    Guid CaseId,
    string Title,
    LearningCaseState State,
    long Version,
    Guid ResponsibleTeacherAppUserId,
    Guid OwnerAssignmentId,
    bool IsCurrentActorResponsibility,
    DateTimeOffset CreatedAtServer,
    DateTimeOffset UpdatedAtServer,
    LearningPrimaryAction? PrimaryAction);

public sealed record StudentLearningCasesSnapshot(
    DateTimeOffset GeneratedAtServer,
    Guid ActorAppUserId,
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationTimeZone,
    DateOnly OrganizationBusinessDate,
    Guid StudentId,
    string StudentDisplayName,
    Guid SubjectProfileId,
    string SubjectKey,
    Guid AssignmentId,
    IReadOnlyList<StudentLearningCaseSummary> Cases,
    bool HasMore);

public sealed record StudentLearningCasesReadResult(
    StudentLearningCasesSnapshot? Snapshot,
    LearningReadFailure? Failure)
{
    public bool IsSuccess => Snapshot is not null && Failure is null;

    public static StudentLearningCasesReadResult Success(StudentLearningCasesSnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null);

    public static StudentLearningCasesReadResult Failed(LearningReadFailureKind kind, string code) =>
        new(null, new LearningReadFailure(kind, code));
}
