using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

internal static class CompatibilityGateMapping
{
    public static string Code(ConsequentialWriteCompatibilityResult result) =>
        string.IsNullOrWhiteSpace(result.Code)
            ? "XQ_COMPATIBILITY_UNAVAILABLE"
            : result.Code;
}

public sealed class CompatibilityGatedCreateObservationCommand(
    ICreateObservationCommand inner,
    IConsequentialWriteCompatibilityGate gate) : ICreateObservationCommand
{
    public async Task<CreateObservationResult> ExecuteAsync(
        CreateObservationRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        var decision = await gate.CheckAsync(cancellationToken).ConfigureAwait(false);
        return decision.Kind switch
        {
            ConsequentialWriteCompatibilityKind.Allowed =>
                await inner.ExecuteAsync(request, expectedActorAppUserId, cancellationToken).ConfigureAwait(false),
            ConsequentialWriteCompatibilityKind.AuthenticationRequired =>
                CreateObservationResult.Failed(CreateObservationFailureKind.AuthenticationRequired, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.Blocked =>
                CreateObservationResult.Failed(CreateObservationFailureKind.CompatibilityBlocked, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.TemporarilyUnavailable =>
                CreateObservationResult.Failed(CreateObservationFailureKind.CompatibilityUnavailable, CompatibilityGateMapping.Code(decision)),
            _ =>
                CreateObservationResult.Failed(CreateObservationFailureKind.InvalidResponse, CompatibilityGateMapping.Code(decision)),
        };
    }
}

public sealed class CompatibilityGatedCreateLearningCaseCommand(
    ICreateLearningCaseCommand inner,
    IConsequentialWriteCompatibilityGate gate) : ICreateLearningCaseCommand
{
    public async Task<CreateLearningCaseResult> ExecuteAsync(
        CreateLearningCaseRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        var decision = await gate.CheckAsync(cancellationToken).ConfigureAwait(false);
        return decision.Kind switch
        {
            ConsequentialWriteCompatibilityKind.Allowed =>
                await inner.ExecuteAsync(request, expectedActorAppUserId, cancellationToken).ConfigureAwait(false),
            ConsequentialWriteCompatibilityKind.AuthenticationRequired =>
                CreateLearningCaseResult.Failed(CreateLearningCaseFailureKind.AuthenticationRequired, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.Blocked =>
                CreateLearningCaseResult.Failed(CreateLearningCaseFailureKind.CompatibilityBlocked, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.TemporarilyUnavailable =>
                CreateLearningCaseResult.Failed(CreateLearningCaseFailureKind.CompatibilityUnavailable, CompatibilityGateMapping.Code(decision)),
            _ =>
                CreateLearningCaseResult.Failed(CreateLearningCaseFailureKind.InvalidResponse, CompatibilityGateMapping.Code(decision)),
        };
    }
}

public sealed class CompatibilityGatedReschedulePrimaryActionCommand(
    IReschedulePrimaryActionCommand inner,
    IConsequentialWriteCompatibilityGate gate) : IReschedulePrimaryActionCommand
{
    public async Task<ActionProgressionResult<ReschedulePrimaryActionReceipt>> ExecuteAsync(
        ReschedulePrimaryActionRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        var decision = await gate.CheckAsync(cancellationToken).ConfigureAwait(false);
        return decision.Kind switch
        {
            ConsequentialWriteCompatibilityKind.Allowed =>
                await inner.ExecuteAsync(request, expectedActorAppUserId, cancellationToken).ConfigureAwait(false),
            ConsequentialWriteCompatibilityKind.AuthenticationRequired =>
                ActionProgressionResult<ReschedulePrimaryActionReceipt>.Failed(ActionProgressionFailureKind.AuthenticationRequired, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.Blocked =>
                ActionProgressionResult<ReschedulePrimaryActionReceipt>.Failed(ActionProgressionFailureKind.CompatibilityBlocked, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.TemporarilyUnavailable =>
                ActionProgressionResult<ReschedulePrimaryActionReceipt>.Failed(ActionProgressionFailureKind.CompatibilityUnavailable, CompatibilityGateMapping.Code(decision)),
            _ =>
                ActionProgressionResult<ReschedulePrimaryActionReceipt>.Failed(ActionProgressionFailureKind.InvalidResponse, CompatibilityGateMapping.Code(decision)),
        };
    }
}

public sealed class CompatibilityGatedRecordVerificationAndNextActionCommand(
    IRecordVerificationAndNextActionCommand inner,
    IConsequentialWriteCompatibilityGate gate) : IRecordVerificationAndNextActionCommand
{
    public async Task<ActionProgressionResult<RecordVerificationAndNextActionReceipt>> ExecuteAsync(
        RecordVerificationAndNextActionRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        var decision = await gate.CheckAsync(cancellationToken).ConfigureAwait(false);
        return decision.Kind switch
        {
            ConsequentialWriteCompatibilityKind.Allowed =>
                await inner.ExecuteAsync(request, expectedActorAppUserId, cancellationToken).ConfigureAwait(false),
            ConsequentialWriteCompatibilityKind.AuthenticationRequired =>
                ActionProgressionResult<RecordVerificationAndNextActionReceipt>.Failed(ActionProgressionFailureKind.AuthenticationRequired, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.Blocked =>
                ActionProgressionResult<RecordVerificationAndNextActionReceipt>.Failed(ActionProgressionFailureKind.CompatibilityBlocked, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.TemporarilyUnavailable =>
                ActionProgressionResult<RecordVerificationAndNextActionReceipt>.Failed(ActionProgressionFailureKind.CompatibilityUnavailable, CompatibilityGateMapping.Code(decision)),
            _ =>
                ActionProgressionResult<RecordVerificationAndNextActionReceipt>.Failed(ActionProgressionFailureKind.InvalidResponse, CompatibilityGateMapping.Code(decision)),
        };
    }
}

public sealed class CompatibilityGatedTransitionLearningCaseStateCommand(
    ITransitionLearningCaseStateCommand inner,
    IConsequentialWriteCompatibilityGate gate) : ITransitionLearningCaseStateCommand
{
    public async Task<CaseLifecycleResult<TransitionLearningCaseStateReceipt>> ExecuteAsync(
        TransitionLearningCaseStateRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        var decision = await gate.CheckAsync(cancellationToken).ConfigureAwait(false);
        return decision.Kind switch
        {
            ConsequentialWriteCompatibilityKind.Allowed =>
                await inner.ExecuteAsync(request, expectedActorAppUserId, cancellationToken).ConfigureAwait(false),
            ConsequentialWriteCompatibilityKind.AuthenticationRequired =>
                CaseLifecycleResult<TransitionLearningCaseStateReceipt>.Failed(CaseLifecycleFailureKind.AuthenticationRequired, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.Blocked =>
                CaseLifecycleResult<TransitionLearningCaseStateReceipt>.Failed(CaseLifecycleFailureKind.CompatibilityBlocked, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.TemporarilyUnavailable =>
                CaseLifecycleResult<TransitionLearningCaseStateReceipt>.Failed(CaseLifecycleFailureKind.CompatibilityUnavailable, CompatibilityGateMapping.Code(decision)),
            _ =>
                CaseLifecycleResult<TransitionLearningCaseStateReceipt>.Failed(CaseLifecycleFailureKind.InvalidResponse, CompatibilityGateMapping.Code(decision)),
        };
    }
}

public sealed class CompatibilityGatedCloseLearningCaseCommand(
    ICloseLearningCaseCommand inner,
    IConsequentialWriteCompatibilityGate gate) : ICloseLearningCaseCommand
{
    public async Task<CaseLifecycleResult<CloseLearningCaseReceipt>> ExecuteAsync(
        CloseLearningCaseRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        var decision = await gate.CheckAsync(cancellationToken).ConfigureAwait(false);
        return decision.Kind switch
        {
            ConsequentialWriteCompatibilityKind.Allowed =>
                await inner.ExecuteAsync(request, expectedActorAppUserId, cancellationToken).ConfigureAwait(false),
            ConsequentialWriteCompatibilityKind.AuthenticationRequired =>
                CaseLifecycleResult<CloseLearningCaseReceipt>.Failed(CaseLifecycleFailureKind.AuthenticationRequired, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.Blocked =>
                CaseLifecycleResult<CloseLearningCaseReceipt>.Failed(CaseLifecycleFailureKind.CompatibilityBlocked, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.TemporarilyUnavailable =>
                CaseLifecycleResult<CloseLearningCaseReceipt>.Failed(CaseLifecycleFailureKind.CompatibilityUnavailable, CompatibilityGateMapping.Code(decision)),
            _ =>
                CaseLifecycleResult<CloseLearningCaseReceipt>.Failed(CaseLifecycleFailureKind.InvalidResponse, CompatibilityGateMapping.Code(decision)),
        };
    }
}

public sealed class CompatibilityGatedReopenLearningCaseCommand(
    IReopenLearningCaseCommand inner,
    IConsequentialWriteCompatibilityGate gate) : IReopenLearningCaseCommand
{
    public async Task<CaseLifecycleResult<ReopenLearningCaseReceipt>> ExecuteAsync(
        ReopenLearningCaseRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        var decision = await gate.CheckAsync(cancellationToken).ConfigureAwait(false);
        return decision.Kind switch
        {
            ConsequentialWriteCompatibilityKind.Allowed =>
                await inner.ExecuteAsync(request, expectedActorAppUserId, cancellationToken).ConfigureAwait(false),
            ConsequentialWriteCompatibilityKind.AuthenticationRequired =>
                CaseLifecycleResult<ReopenLearningCaseReceipt>.Failed(CaseLifecycleFailureKind.AuthenticationRequired, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.Blocked =>
                CaseLifecycleResult<ReopenLearningCaseReceipt>.Failed(CaseLifecycleFailureKind.CompatibilityBlocked, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.TemporarilyUnavailable =>
                CaseLifecycleResult<ReopenLearningCaseReceipt>.Failed(CaseLifecycleFailureKind.CompatibilityUnavailable, CompatibilityGateMapping.Code(decision)),
            _ =>
                CaseLifecycleResult<ReopenLearningCaseReceipt>.Failed(CaseLifecycleFailureKind.InvalidResponse, CompatibilityGateMapping.Code(decision)),
        };
    }
}

public sealed class CompatibilityGatedOrganizationInvitationCommand(
    IOrganizationInvitationCommand inner,
    IConsequentialWriteCompatibilityGate gate) : IOrganizationInvitationCommand
{
    public async Task<CreateOrganizationInvitationResult> ExecuteAsync(
        CreateOrganizationInvitationRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        var decision = await gate.CheckAsync(cancellationToken).ConfigureAwait(false);
        return decision.Kind switch
        {
            ConsequentialWriteCompatibilityKind.Allowed =>
                await inner.ExecuteAsync(request, expectedActorAppUserId, cancellationToken).ConfigureAwait(false),
            ConsequentialWriteCompatibilityKind.AuthenticationRequired =>
                CreateOrganizationInvitationResult.Failed(CreateOrganizationInvitationFailureKind.AuthenticationRequired, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.Blocked =>
                CreateOrganizationInvitationResult.Failed(CreateOrganizationInvitationFailureKind.CompatibilityBlocked, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.TemporarilyUnavailable =>
                CreateOrganizationInvitationResult.Failed(CreateOrganizationInvitationFailureKind.CompatibilityUnavailable, CompatibilityGateMapping.Code(decision)),
            _ =>
                CreateOrganizationInvitationResult.Failed(CreateOrganizationInvitationFailureKind.InvalidResponse, CompatibilityGateMapping.Code(decision)),
        };
    }
}

public sealed class CompatibilityGatedAcceptOrganizationInvitationCommand(
    IAcceptOrganizationInvitationCommand inner,
    IConsequentialWriteCompatibilityGate gate) : IAcceptOrganizationInvitationCommand
{
    public async Task<AcceptOrganizationInvitationResult> ExecuteAsync(
        AcceptOrganizationInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        var decision = await gate.CheckAsync(cancellationToken).ConfigureAwait(false);
        return decision.Kind switch
        {
            ConsequentialWriteCompatibilityKind.Allowed =>
                await inner.ExecuteAsync(request, cancellationToken).ConfigureAwait(false),
            ConsequentialWriteCompatibilityKind.AuthenticationRequired =>
                AcceptOrganizationInvitationResult.Failed(AcceptOrganizationInvitationFailureKind.AuthenticationRequired, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.Blocked =>
                AcceptOrganizationInvitationResult.Failed(AcceptOrganizationInvitationFailureKind.CompatibilityBlocked, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.TemporarilyUnavailable =>
                AcceptOrganizationInvitationResult.Failed(AcceptOrganizationInvitationFailureKind.CompatibilityUnavailable, CompatibilityGateMapping.Code(decision)),
            _ =>
                AcceptOrganizationInvitationResult.Failed(AcceptOrganizationInvitationFailureKind.InvalidResponse, CompatibilityGateMapping.Code(decision)),
        };
    }
}

public sealed class CompatibilityGatedOrganizationInvitationDeliveryCommand(
    IOrganizationInvitationDeliveryCommand inner,
    IConsequentialWriteCompatibilityGate gate) : IOrganizationInvitationDeliveryCommand
{
    public async Task<DeliverOrganizationInvitationResult> ExecuteAsync(
        DeliverOrganizationInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        var decision = await gate.CheckAsync(cancellationToken).ConfigureAwait(false);
        return decision.Kind switch
        {
            ConsequentialWriteCompatibilityKind.Allowed =>
                await inner.ExecuteAsync(request, cancellationToken).ConfigureAwait(false),
            ConsequentialWriteCompatibilityKind.AuthenticationRequired =>
                DeliverOrganizationInvitationResult.Failed(DeliverOrganizationInvitationFailureKind.AuthenticationRequired, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.Blocked =>
                DeliverOrganizationInvitationResult.Failed(DeliverOrganizationInvitationFailureKind.CompatibilityBlocked, CompatibilityGateMapping.Code(decision)),
            ConsequentialWriteCompatibilityKind.TemporarilyUnavailable =>
                DeliverOrganizationInvitationResult.Failed(DeliverOrganizationInvitationFailureKind.CompatibilityUnavailable, CompatibilityGateMapping.Code(decision)),
            _ =>
                DeliverOrganizationInvitationResult.Failed(DeliverOrganizationInvitationFailureKind.InvalidResponse, CompatibilityGateMapping.Code(decision)),
        };
    }
}
