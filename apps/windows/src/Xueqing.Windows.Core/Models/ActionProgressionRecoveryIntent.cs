namespace Xueqing.Windows.Core.Models;

public enum ActionProgressionRecoveryIntentKind
{
    ReschedulePrimaryAction,
    RecordVerificationAndNextAction,
}

public abstract record ActionProgressionRecoveryIntent
{
    public abstract ActionProgressionRecoveryIntentKind Kind { get; }
    public abstract Guid OperationId { get; }
    public abstract Guid OrganizationId { get; }
    public abstract Guid CaseId { get; }
    public abstract Guid PrimaryActionId { get; }
}

public sealed record ReschedulePrimaryActionRecoveryIntent(
    ReschedulePrimaryActionRequest Request) : ActionProgressionRecoveryIntent
{
    public override ActionProgressionRecoveryIntentKind Kind =>
        ActionProgressionRecoveryIntentKind.ReschedulePrimaryAction;

    public override Guid OperationId => Request.OperationId;
    public override Guid OrganizationId => Request.OrganizationId;
    public override Guid CaseId => Request.CaseId;
    public override Guid PrimaryActionId => Request.PrimaryActionId;
}

public sealed record VerificationAndNextActionRecoveryIntent(
    RecordVerificationAndNextActionRequest Request) : ActionProgressionRecoveryIntent
{
    public override ActionProgressionRecoveryIntentKind Kind =>
        ActionProgressionRecoveryIntentKind.RecordVerificationAndNextAction;

    public override Guid OperationId => Request.OperationId;
    public override Guid OrganizationId => Request.OrganizationId;
    public override Guid CaseId => Request.CaseId;
    public override Guid PrimaryActionId => Request.CurrentPrimaryActionId;
}
