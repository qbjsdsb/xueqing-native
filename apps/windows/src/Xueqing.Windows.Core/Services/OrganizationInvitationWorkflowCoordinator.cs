using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public sealed class OrganizationInvitationWorkflowCoordinator
{
    private readonly IOrganizationInvitationCommand _create;
    private readonly IOrganizationInvitationDeliveryCommand _deliver;
    private readonly IOrganizationInvitationRecoveryStore _recovery;

    public OrganizationInvitationWorkflowCoordinator(
        IOrganizationInvitationCommand create,
        IOrganizationInvitationDeliveryCommand deliver,
        IOrganizationInvitationRecoveryStore recovery)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
    }

    public Task<IReadOnlyList<OrganizationInvitationRecoveryIntent>> ListAsync(
        Guid actorAppUserId,
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        _recovery.ListAsync(actorAppUserId, organizationId, cancellationToken);

    public async Task<OrganizationInvitationWorkflowResult> StartAsync(
        CreateOrganizationInvitationRequest request,
        Guid deliveryOperationId,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OrganizationInvitationRecoveryIntent intent;
        try
        {
            intent = OrganizationInvitationRecoveryIntent.Create(
                request,
                deliveryOperationId);
            intent.Validate();
            await _recovery.SaveAsync(
                expectedActorAppUserId,
                intent,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new OrganizationInvitationWorkflowResult(
                OrganizationInvitationWorkflowOutcome.LocalDurabilityFailure,
                null,
                null,
                null);
        }

        return await ResumeAsync(
            intent,
            expectedActorAppUserId,
            cancellationToken);
    }

    public async Task<OrganizationInvitationWorkflowResult> ResumeAsync(
        OrganizationInvitationRecoveryIntent intent,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        intent.Validate();

        if (intent.Stage == OrganizationInvitationRecoveryStage.DeliveryFailed)
        {
            return new OrganizationInvitationWorkflowResult(
                OrganizationInvitationWorkflowOutcome.DeliveryFailed,
                intent,
                null,
                new DeliverOrganizationInvitationFailure(
                    DeliverOrganizationInvitationFailureKind.ProviderRejected,
                    intent.TerminalFailureCode!));
        }

        if (intent.Stage == OrganizationInvitationRecoveryStage.CreatePending)
        {
            var createResult = await _create.ExecuteAsync(
                intent.CreateRequest,
                expectedActorAppUserId,
                cancellationToken);

            if (!createResult.IsSuccess)
            {
                if (createResult.MustRetrySameOperation)
                {
                    return new OrganizationInvitationWorkflowResult(
                        OrganizationInvitationWorkflowOutcome.CreatePendingConfirmation,
                        intent,
                        createResult.Failure,
                        null);
                }

                await TryRemoveKnownRejectedAsync(
                    expectedActorAppUserId,
                    intent,
                    cancellationToken);
                return new OrganizationInvitationWorkflowResult(
                    OrganizationInvitationWorkflowOutcome.Rejected,
                    null,
                    createResult.Failure,
                    null);
            }

            var deliveryIntent = intent.BeginDelivery(
                createResult.Receipt!.InvitationId);
            try
            {
                await _recovery.SaveAsync(
                    expectedActorAppUserId,
                    deliveryIntent,
                    cancellationToken);
                intent = deliveryIntent;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // The old CreatePending record deliberately remains. Replaying the
                // exact create operation will recover the same invitation receipt.
                return new OrganizationInvitationWorkflowResult(
                    OrganizationInvitationWorkflowOutcome.LocalDurabilityFailure,
                    intent,
                    null,
                    null);
            }
        }

        var deliveryResult = await _deliver.ExecuteAsync(
            intent.DeliveryRequest,
            cancellationToken);

        if (deliveryResult.IsSuccess)
        {
            try
            {
                await _recovery.RemoveAsync(
                    expectedActorAppUserId,
                    intent.OrganizationId,
                    intent.CreateOperationId,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Keeping DeliveryPending is safe: the same delivery operation
                // replays the authoritative sent state and cannot send again.
                return new OrganizationInvitationWorkflowResult(
                    OrganizationInvitationWorkflowOutcome.LocalDurabilityFailure,
                    intent,
                    null,
                    null);
            }

            return new OrganizationInvitationWorkflowResult(
                OrganizationInvitationWorkflowOutcome.Sent,
                null,
                null,
                null);
        }

        if (deliveryResult.MustRetrySameOperation)
        {
            return new OrganizationInvitationWorkflowResult(
                OrganizationInvitationWorkflowOutcome.DeliveryPendingConfirmation,
                intent,
                null,
                deliveryResult.Failure);
        }

        if (deliveryResult.Failure?.Kind is
            DeliverOrganizationInvitationFailureKind.AuthenticationRequired or
            DeliverOrganizationInvitationFailureKind.AuthorityChanged)
        {
            return new OrganizationInvitationWorkflowResult(
                OrganizationInvitationWorkflowOutcome.DeliveryBlocked,
                intent,
                null,
                deliveryResult.Failure);
        }

        if (deliveryResult.Failure?.Kind is
            DeliverOrganizationInvitationFailureKind.ProviderRejected or
            DeliverOrganizationInvitationFailureKind.AlreadyDelivered)
        {
            var failedIntent = intent.MarkDeliveryFailed(
                deliveryResult.Failure.Code);
            try
            {
                await _recovery.SaveAsync(
                    expectedActorAppUserId,
                    failedIntent,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Leaving DeliveryPending is fail-safe. Replaying the same
                // operation returns the durable provider state without dispatch.
                return new OrganizationInvitationWorkflowResult(
                    OrganizationInvitationWorkflowOutcome.LocalDurabilityFailure,
                    intent,
                    null,
                    deliveryResult.Failure);
            }

            return new OrganizationInvitationWorkflowResult(
                OrganizationInvitationWorkflowOutcome.DeliveryFailed,
                failedIntent,
                null,
                deliveryResult.Failure);
        }

        return new OrganizationInvitationWorkflowResult(
            OrganizationInvitationWorkflowOutcome.DeliveryPendingConfirmation,
            intent,
            null,
            deliveryResult.Failure);
    }

    private async Task TryRemoveKnownRejectedAsync(
        Guid actorAppUserId,
        OrganizationInvitationRecoveryIntent intent,
        CancellationToken cancellationToken)
    {
        try
        {
            await _recovery.RemoveAsync(
                actorAppUserId,
                intent.OrganizationId,
                intent.CreateOperationId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The deterministic server rejection can be rediscovered locally;
            // keeping the same operation is safer than fabricating a new intent.
        }
    }
}
