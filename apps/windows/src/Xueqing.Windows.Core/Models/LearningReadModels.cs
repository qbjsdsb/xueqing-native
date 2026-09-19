namespace Xueqing.Windows.Core.Models;

public sealed record StudentLearningScope(
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId);

public enum LearningCaseState
{
    New,
    Confirmed,
    Intervening,
    PendingVerification,
    Stable,
}

public enum ActionDueBucket
{
    Overdue,
    Today,
    Undated,
    Future,
}

public sealed record LearningPrimaryAction(
    Guid ActionId,
    string ActionText,
    DateOnly? DueOn,
    ActionDueBucket DueBucket,
    long Version);

public sealed record StudentLearningCaseFocus(
    Guid CaseId,
    string Title,
    LearningCaseState State,
    long Version,
    Guid ResponsibleTeacherAppUserId,
    Guid OwnerAssignmentId,
    DateTimeOffset CreatedAtServer,
    DateTimeOffset UpdatedAtServer,
    LearningPrimaryAction PrimaryAction);

public sealed record StudentLearningFocusSnapshot(
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
    IReadOnlyList<StudentLearningCaseFocus> Cases,
    bool HasMore);

public sealed record PersonalTodayAction(
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationTimeZone,
    DateOnly OrganizationBusinessDate,
    Guid StudentId,
    string StudentDisplayName,
    Guid SubjectProfileId,
    string SubjectKey,
    Guid AssignmentId,
    Guid CaseId,
    string CaseTitle,
    LearningCaseState CaseState,
    long CaseVersion,
    Guid ActionId,
    string ActionText,
    DateOnly? DueOn,
    ActionDueBucket DueBucket,
    long ActionVersion,
    DateTimeOffset CaseUpdatedAtServer);

public sealed record PersonalTodayActionsSnapshot(
    DateTimeOffset GeneratedAtServer,
    Guid ActorAppUserId,
    IReadOnlyList<PersonalTodayAction> Actions,
    bool HasMore);

public enum LearningReadFailureKind
{
    AuthenticationRequired,
    AccessDenied,
    ServerInvariant,
    Transient,
    InvalidResponse,
}

public sealed record LearningReadFailure(
    LearningReadFailureKind Kind,
    string Code);

public sealed record StudentLearningFocusReadResult(
    StudentLearningFocusSnapshot? Snapshot,
    LearningReadFailure? Failure)
{
    public bool IsSuccess => Snapshot is not null && Failure is null;

    public static StudentLearningFocusReadResult Success(StudentLearningFocusSnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null);

    public static StudentLearningFocusReadResult Failed(LearningReadFailureKind kind, string code) =>
        new(null, new LearningReadFailure(kind, code));
}

public sealed record PersonalTodayActionsReadResult(
    PersonalTodayActionsSnapshot? Snapshot,
    LearningReadFailure? Failure)
{
    public bool IsSuccess => Snapshot is not null && Failure is null;

    public static PersonalTodayActionsReadResult Success(PersonalTodayActionsSnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null);

    public static PersonalTodayActionsReadResult Failed(LearningReadFailureKind kind, string code) =>
        new(null, new LearningReadFailure(kind, code));
}
