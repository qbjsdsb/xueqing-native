namespace Xueqing.Windows.Core.Models;

// Formal Action progression stays online and is keyed to the exact Case, Action
// and active assignment observed in an authoritative projection.
public sealed record ReschedulePrimaryActionRequest(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid OwnerAssignmentId,
    Guid CaseId,
    Guid PrimaryActionId,
    long ExpectedCaseVersion,
    long ExpectedActionVersion,
    DateOnly? NewDueOn);

public sealed record RecordVerificationAndNextActionRequest(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid OwnerAssignmentId,
    Guid CaseId,
    Guid CurrentPrimaryActionId,
    long ExpectedCaseVersion,
    long ExpectedActionVersion,
    VerificationOutcome Outcome,
    string VerificationSummary,
    string NextActionText,
    DateOnly? NextActionDueOn);

public enum VerificationOutcome
{
    Met,
    PartiallyMet,
    NotMet,
    Uncertain,
}

public enum ActionProgressionFailureKind
{
    AuthenticationRequired,
    AuthorityChanged,
    VersionConflict,
    Validation,
    OperationConflict,
    ResultUnknown,
    Transient,
    InvalidResponse,
}

public sealed record ActionProgressionFailure(ActionProgressionFailureKind Kind, string Code);

public sealed record ReschedulePrimaryActionReceipt(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid OwnerAssignmentId,
    Guid ResponsibleTeacherAppUserId,
    Guid CaseId,
    LearningCaseState CaseState,
    long CaseVersion,
    Guid PrimaryActionId,
    long ActionVersion,
    DateOnly? PreviousDueOn,
    DateOnly? DueOn,
    Guid CaseEventId,
    DateTimeOffset ServerCommittedAt);

public sealed record RecordVerificationAndNextActionReceipt(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid OwnerAssignmentId,
    Guid ResponsibleTeacherAppUserId,
    Guid CaseId,
    LearningCaseState CaseState,
    long CaseVersion,
    Guid CompletedPrimaryActionId,
    long CompletedActionVersion,
    Guid VerificationId,
    VerificationOutcome Outcome,
    string VerificationSummary,
    Guid NextPrimaryActionId,
    long NextActionVersion,
    string NextActionText,
    DateOnly? NextActionDueOn,
    Guid CaseEventId,
    DateTimeOffset ServerCommittedAt);

public sealed record ActionProgressionResult<TReceipt>(TReceipt? Receipt, ActionProgressionFailure? Failure)
    where TReceipt : class
{
    public bool IsSuccess => Receipt is not null && Failure is null;
    public bool MustRetrySameOperation => Failure?.Kind == ActionProgressionFailureKind.ResultUnknown;

    public static ActionProgressionResult<TReceipt> Success(TReceipt receipt) =>
        new(receipt ?? throw new ArgumentNullException(nameof(receipt)), null);

    public static ActionProgressionResult<TReceipt> Failed(ActionProgressionFailureKind kind, string code) =>
        new(null, new ActionProgressionFailure(kind, code));
}
