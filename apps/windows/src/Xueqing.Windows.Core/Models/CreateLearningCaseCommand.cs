namespace Xueqing.Windows.Core.Models;

public sealed record CreateLearningCaseRequest(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid OwnerAssignmentId,
    string Title,
    string PrimaryActionText,
    DateOnly? PrimaryActionDueOn,
    Guid? SourceObservationId);

public sealed record CreateLearningCaseReceipt(
    Guid OperationId,
    Guid CaseId,
    LearningCaseState CaseState,
    long CaseVersion,
    Guid PrimaryActionId,
    Guid CaseEventId,
    Guid ResponsibleTeacherAppUserId,
    Guid OwnerAssignmentId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    string SubjectKey,
    Guid? SourceObservationId,
    DateTimeOffset ServerCommittedAt);

public enum CreateLearningCaseFailureKind
{
    AuthenticationRequired,
    AuthorityChanged,
    Validation,
    OperationConflict,
    LocalDurabilityFailure,
    ResultUnknown,
    Transient,
    InvalidResponse,
}

public sealed record CreateLearningCaseFailure(
    CreateLearningCaseFailureKind Kind,
    string Code);

public sealed record CreateLearningCaseResult(
    CreateLearningCaseReceipt? Receipt,
    CreateLearningCaseFailure? Failure)
{
    public bool IsSuccess => Receipt is not null && Failure is null;

    public bool MustRetrySameOperation =>
        Failure?.Kind == CreateLearningCaseFailureKind.ResultUnknown;

    public static CreateLearningCaseResult Success(CreateLearningCaseReceipt receipt) =>
        new(receipt ?? throw new ArgumentNullException(nameof(receipt)), null);

    public static CreateLearningCaseResult Failed(
        CreateLearningCaseFailureKind kind,
        string code) =>
        new(null, new CreateLearningCaseFailure(kind, code));
}
