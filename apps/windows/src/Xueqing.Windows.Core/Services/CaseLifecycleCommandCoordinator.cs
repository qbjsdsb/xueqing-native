using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public sealed class CaseLifecycleCommandCoordinator
{
    private readonly ITransitionLearningCaseStateCommand _transition;
    private readonly ICloseLearningCaseCommand _close;
    private readonly IReopenLearningCaseCommand _reopen;
    private readonly ICaseLifecycleRecoveryStore _recovery;

    public CaseLifecycleCommandCoordinator(
        ITransitionLearningCaseStateCommand transition,
        ICloseLearningCaseCommand close,
        IReopenLearningCaseCommand reopen,
        ICaseLifecycleRecoveryStore recovery)
    {
        _transition = transition ?? throw new ArgumentNullException(nameof(transition));
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _reopen = reopen ?? throw new ArgumentNullException(nameof(reopen));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
    }

    public async Task<CaseLifecycleResult<TransitionLearningCaseStateReceipt>> TransitionAsync(
        TransitionLearningCaseStateRequest request,
        Guid actorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var intent = new TransitionCaseRecoveryIntent(request);

        if (!await TrySaveAsync(actorAppUserId, intent, cancellationToken).ConfigureAwait(false))
        {
            return CaseLifecycleResult<TransitionLearningCaseStateReceipt>.Failed(
                CaseLifecycleFailureKind.LocalDurabilityFailure,
                "XQ_LOCAL_CASE_LIFECYCLE_RECOVERY_UNAVAILABLE");
        }

        var result = await _transition.ExecuteAsync(
            request,
            actorAppUserId,
            cancellationToken).ConfigureAwait(false);

        return await FinalizeAsync(
            actorAppUserId,
            intent,
            result,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CaseLifecycleResult<CloseLearningCaseReceipt>> CloseAsync(
        CloseLearningCaseRequest request,
        Guid actorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var intent = new CloseCaseRecoveryIntent(request);

        if (!await TrySaveAsync(actorAppUserId, intent, cancellationToken).ConfigureAwait(false))
        {
            return CaseLifecycleResult<CloseLearningCaseReceipt>.Failed(
                CaseLifecycleFailureKind.LocalDurabilityFailure,
                "XQ_LOCAL_CASE_LIFECYCLE_RECOVERY_UNAVAILABLE");
        }

        var result = await _close.ExecuteAsync(
            request,
            actorAppUserId,
            cancellationToken).ConfigureAwait(false);

        return await FinalizeAsync(
            actorAppUserId,
            intent,
            result,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CaseLifecycleResult<ReopenLearningCaseReceipt>> ReopenAsync(
        ReopenLearningCaseRequest request,
        Guid actorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var intent = new ReopenCaseRecoveryIntent(request);

        if (!await TrySaveAsync(actorAppUserId, intent, cancellationToken).ConfigureAwait(false))
        {
            return CaseLifecycleResult<ReopenLearningCaseReceipt>.Failed(
                CaseLifecycleFailureKind.LocalDurabilityFailure,
                "XQ_LOCAL_CASE_LIFECYCLE_RECOVERY_UNAVAILABLE");
        }

        var result = await _reopen.ExecuteAsync(
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
        CaseLifecycleRecoveryIntent intent,
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

    private async Task<CaseLifecycleResult<TReceipt>> FinalizeAsync<TReceipt>(
        Guid actorAppUserId,
        CaseLifecycleRecoveryIntent intent,
        CaseLifecycleResult<TReceipt> result,
        CancellationToken cancellationToken)
        where TReceipt : class
    {
        if (result.Failure?.Kind is
            CaseLifecycleFailureKind.ResultUnknown or
            CaseLifecycleFailureKind.Transient or
            CaseLifecycleFailureKind.InvalidResponse)
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
                // operation pending is safe because explicit recovery retries
                // reuse the original operation id and cannot duplicate state.
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
            return CaseLifecycleResult<TReceipt>.Failed(
                CaseLifecycleFailureKind.LocalDurabilityFailure,
                "XQ_LOCAL_CASE_LIFECYCLE_REJECTION_QUARANTINE_FAILED");
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
            // The row is hidden from pending discovery after quarantine. A
            // corrected lifecycle intent can safely claim the Case later.
        }

        return result;
    }
}
