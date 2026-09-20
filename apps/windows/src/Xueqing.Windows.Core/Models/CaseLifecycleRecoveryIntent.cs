namespace Xueqing.Windows.Core.Models;

public enum CaseLifecycleRecoveryIntentKind
{
    TransitionState,
    CloseCase,
    ReopenCase,
}

public abstract record CaseLifecycleRecoveryIntent
{
    public abstract CaseLifecycleRecoveryIntentKind Kind { get; }
    public abstract Guid OperationId { get; }
    public abstract Guid OrganizationId { get; }
    public abstract Guid StudentId { get; }
    public abstract Guid SubjectProfileId { get; }
    public abstract Guid OwnerAssignmentId { get; }
    public abstract Guid CaseId { get; }
}

public sealed record TransitionCaseRecoveryIntent(
    TransitionLearningCaseStateRequest Request) : CaseLifecycleRecoveryIntent
{
    public override CaseLifecycleRecoveryIntentKind Kind => CaseLifecycleRecoveryIntentKind.TransitionState;
    public override Guid OperationId => Request.OperationId;
    public override Guid OrganizationId => Request.OrganizationId;
    public override Guid StudentId => Request.StudentId;
    public override Guid SubjectProfileId => Request.SubjectProfileId;
    public override Guid OwnerAssignmentId => Request.OwnerAssignmentId;
    public override Guid CaseId => Request.CaseId;
}

public sealed record CloseCaseRecoveryIntent(
    CloseLearningCaseRequest Request) : CaseLifecycleRecoveryIntent
{
    public override CaseLifecycleRecoveryIntentKind Kind => CaseLifecycleRecoveryIntentKind.CloseCase;
    public override Guid OperationId => Request.OperationId;
    public override Guid OrganizationId => Request.OrganizationId;
    public override Guid StudentId => Request.StudentId;
    public override Guid SubjectProfileId => Request.SubjectProfileId;
    public override Guid OwnerAssignmentId => Request.OwnerAssignmentId;
    public override Guid CaseId => Request.CaseId;
}

public sealed record ReopenCaseRecoveryIntent(
    ReopenLearningCaseRequest Request) : CaseLifecycleRecoveryIntent
{
    public override CaseLifecycleRecoveryIntentKind Kind => CaseLifecycleRecoveryIntentKind.ReopenCase;
    public override Guid OperationId => Request.OperationId;
    public override Guid OrganizationId => Request.OrganizationId;
    public override Guid StudentId => Request.StudentId;
    public override Guid SubjectProfileId => Request.SubjectProfileId;
    public override Guid OwnerAssignmentId => Request.OwnerAssignmentId;
    public override Guid CaseId => Request.CaseId;
}
