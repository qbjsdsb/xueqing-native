using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public sealed class ActionProgressionCommandCoordinator
{
    private readonly IReschedulePrimaryActionCommand _reschedule;
    private readonly IRecordVerificationAndNextActionCommand _verification;
    private readonly IActionProgressionRecoveryStore _recovery;

    public ActionProgressionCommandCoordinator(
        IReschedulePrimaryActionCommand reschedule,
        IRecordVerificationAndNextActionCommand verification,
        IActionProgressionRecoveryStore recovery)
    {
        _reschedule = reschedule ?? throw new ArgumentNullException(nameof(reschedule));
        _verification = verification ?? throw new ArgumentNullException(nameof(verification));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
    }

    public async Task<ActionProgressionResult<ReschedulePrimaryActionReceipt>> RescheduleAsync(
        ReschedulePrimaryActionRequest request,
        Guid actorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var intent = new ReschedulePrimaryActionRecoveryIntent(request);

        if (!await TrySaveAsync(actorAppUserId, intent, cancellationToken).ConfigureAwait(false))
        {
            return ActionProgressionResult<ReschedulePrimaryActionReceipt>.Failed(
                ActionProgressionFailureKind.LocalDurabilityFailure,
                "XQ_LOCAL_ACTION_RECOVERY_UNAVAILABLE");
        }

        var result = await _reschedule.ExecuteAsync(
            request,
            actorAppUserId,
            cancellationToken).ConfigureAwait(false);

        return await FinalizeAsync(
            actorAppUserId,
            intent,
            result,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActionProgressionResult<RecordVerificationAndNextActionReceipt>> VerifyAsync(
        RecordVerificationAndNextActionRequest request,
        Guid actorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var intent = new VerificationAndNextActionRecoveryIntent(request);

        if (!await TrySaveAsync(actorAppUserId, intent, cancellationToken).ConfigureAwait(false))
        {
            return ActionProgressionResult<RecordVerificationAndNextActionReceipt>.Failed(
                ActionProgressionFailureKind.LocalDurabilityFailure,
                "XQ_LOCAL_ACTION_RECOVERY_UNAVAILABLE");
        }

        var result = await _verification.ExecuteAsync(
            request,
            actorAppUserId,
            cancellationToken).ConfigureAwait(false);

        return await FinalizeAsync(
            actorAppUserId,
            intent,
            result,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TrySaveAsync(
        Guid actorAppUserId,
        ActionProgressionRecoveryIntent intent,
        CancellationToken cancellationToken)
    {
        try
        {
            await _recovery.SaveAsync(
                actorAppUserId,
                intent,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private async Task<ActionProgressionResult<TReceipt>> FinalizeAsync<TReceipt>(
        Guid actorAppUserId,
        ActionProgressionRecoveryIntent intent,
        ActionProgressionResult<TReceipt> result,
        CancellationToken cancellationToken)
        where TReceipt : class
    {
        if (result.Failure?.Kind is
            ActionProgressionFailureKind.ResultUnknown or
            ActionProgressionFailureKind.Transient or
            ActionProgressionFailureKind.InvalidResponse)
        {
            return result;
        }

        if (result.IsSuccess)
        {
            try
            {
                await _recovery.RemoveAsync(
                    actorAppUserId,
                    intent.OrganizationId,
                    intent.OperationId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // The authoritative command committed. Leaving the exact
                // operation pending is safe because an explicit recovery retry
                // reuses the original operation id and cannot duplicate state.
            }

            return result;
        }

        try
        {
            await _recovery.MarkRejectedAsync(
                actorAppUserId,
                intent.OrganizationId,
                intent.OperationId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ActionProgressionResult<TReceipt>.Failed(
                ActionProgressionFailureKind.LocalDurabilityFailure,
                "XQ_LOCAL_ACTION_REJECTION_QUARANTINE_FAILED");
        }

        try
        {
            await _recovery.RemoveAsync(
                actorAppUserId,
                intent.OrganizationId,
                intent.OperationId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The row is already hidden from pending discovery. A corrected
            // intent for the same Action can safely claim the pending slot.
        }

        return result;
    }
}
