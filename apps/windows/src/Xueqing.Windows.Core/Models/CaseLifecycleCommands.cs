namespace Xueqing.Windows.Core.Models;

public sealed record TransitionLearningCaseStateRequest(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid OwnerAssignmentId,
    Guid CaseId,
    long ExpectedCaseVersion,
    LearningCaseState TargetState);

public sealed record CloseLearningCaseRequest(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid OwnerAssignmentId,
    Guid CaseId,
    Guid PrimaryActionId,
    long ExpectedCaseVersion,
    long ExpectedActionVersion);

public sealed record ReopenLearningCaseRequest(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid OwnerAssignmentId,
    Guid CaseId,
    long ExpectedCaseVersion,
    string NewPrimaryActionText,
    DateOnly? NewPrimaryActionDueOn);

public enum CaseLifecycleFailureKind
{
    AuthenticationRequired,
    AuthorityChanged,
    VersionConflict,
    InvalidTransition,
    Validation,
    OperationConflict,
    LocalDurabilityFailure,
    ResultUnknown,
    Transient,
    InvalidResponse,
}

public sealed record CaseLifecycleFailure(
    CaseLifecycleFailureKind Kind,
    string Code);

public sealed record TransitionLearningCaseStateReceipt(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid OwnerAssignmentId,
    Guid ResponsibleTeacherAppUserId,
    Guid CaseId,
    LearningCaseState PreviousCaseState,
    LearningCaseState CaseState,
    long CaseVersion,
    Guid PrimaryActionId,
    Guid CaseEventId,
    DateTimeOffset ServerCommittedAt);

public sealed record CloseLearningCaseReceipt(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid OwnerAssignmentId,
    Guid ResponsibleTeacherAppUserId,
    Guid CaseId,
    LearningCaseState PreviousCaseState,
    LearningCaseState CaseState,
    long CaseVersion,
    Guid CancelledPrimaryActionId,
    long CancelledActionVersion,
    Guid CaseEventId,
    DateTimeOffset ServerCommittedAt);

public sealed record ReopenLearningCaseReceipt(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid OwnerAssignmentId,
    Guid ResponsibleTeacherAppUserId,
    Guid CaseId,
    LearningCaseState PreviousCaseState,
    LearningCaseState CaseState,
    long CaseVersion,
    Guid NewPrimaryActionId,
    long NewActionVersion,
    string NewActionText,
    DateOnly? NewActionDueOn,
    Guid CaseEventId,
    DateTimeOffset ServerCommittedAt);

public sealed record CaseLifecycleResult<TReceipt>(
    TReceipt? Receipt,
    CaseLifecycleFailure? Failure)
    where TReceipt : class
{
    public bool IsSuccess => Receipt is not null && Failure is null;

    public bool MustRetrySameOperation =>
        Failure?.Kind is
            CaseLifecycleFailureKind.ResultUnknown or
            CaseLifecycleFailureKind.Transient or
            CaseLifecycleFailureKind.InvalidResponse;

    public static CaseLifecycleResult<TReceipt> Success(TReceipt receipt) =>
        new(receipt ?? throw new ArgumentNullException(nameof(receipt)), null);

    public static CaseLifecycleResult<TReceipt> Failed(
        CaseLifecycleFailureKind kind,
        string code) =>
        new(null, new CaseLifecycleFailure(kind, code));
}
