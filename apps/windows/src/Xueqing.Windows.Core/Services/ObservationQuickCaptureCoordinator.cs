using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public sealed class ObservationQuickCaptureCoordinator
{
    private readonly IObservationDraftStore _drafts;
    private readonly ICreateObservationRecoveryStore _recovery;
    private readonly ICreateObservationCommand _command;

    public ObservationQuickCaptureCoordinator(
        IObservationDraftStore drafts,
        ICreateObservationRecoveryStore recovery,
        ICreateObservationCommand command)
    {
        _drafts = drafts ?? throw new ArgumentNullException(nameof(drafts));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _command = command ?? throw new ArgumentNullException(nameof(command));
    }

    public async Task<ObservationQuickCaptureOpenState> OpenAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actorAppUserId);
        ArgumentNullException.ThrowIfNull(scope);

        var draftTask = _drafts.OpenAsync(
            actorAppUserId,
            scope,
            cancellationToken);
        var recoveryTask = _recovery.FindPendingAsync(
            actorAppUserId,
            scope,
            cancellationToken);

        await Task.WhenAll(draftTask, recoveryTask).ConfigureAwait(false);
        return new ObservationQuickCaptureOpenState(
            await draftTask.ConfigureAwait(false),
            await recoveryTask.ConfigureAwait(false));
    }

    public Task<bool> SaveDraftAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        long expectedEpoch,
        string text,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actorAppUserId);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(text);

        return _drafts.SaveAsync(
            actorAppUserId,
            scope,
            expectedEpoch,
            text,
            cancellationToken);
    }

    public Task<long?> DiscardDraftAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        long expectedEpoch,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actorAppUserId);
        ArgumentNullException.ThrowIfNull(scope);

        return _drafts.DiscardAsync(
            actorAppUserId,
            scope,
            expectedEpoch,
            cancellationToken);
    }

    public async Task<CreateObservationResult> SubmitAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        long expectedDraftEpoch,
        Guid operationId,
        string finalText,
        DateTimeOffset? capturedAt = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actorAppUserId);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(finalText);

        if (operationId == Guid.Empty)
        {
            return CreateObservationResult.Failed(
                CreateObservationFailureKind.Validation,
                "XQ_OPERATION_ID_REQUIRED");
        }

        bool draftSaved;
        try
        {
            draftSaved = await _drafts.SaveAsync(
                actorAppUserId,
                scope,
                expectedDraftEpoch,
                finalText,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return LocalDurabilityFailure("XQ_LOCAL_OBSERVATION_DRAFT_UNAVAILABLE");
        }

        if (!draftSaved)
        {
            return LocalDurabilityFailure("XQ_LOCAL_OBSERVATION_DRAFT_STALE");
        }

        var request = new CreateObservationRequest(
            operationId,
            scope.OrganizationId,
            scope.StudentId,
            scope.SubjectProfileId,
            scope.AssignmentId,
            finalText,
            capturedAt,
            metadata);

        try
        {
            await _recovery.SaveAsync(
                actorAppUserId,
                request,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return LocalDurabilityFailure("XQ_LOCAL_OBSERVATION_RECOVERY_UNAVAILABLE");
        }

        var result = await _command.ExecuteAsync(
            request,
            actorAppUserId,
            cancellationToken).ConfigureAwait(false);

        return await FinalizeAsync(
            actorAppUserId,
            scope,
            expectedDraftEpoch,
            request,
            result,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CreateObservationResult> RetryPendingAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actorAppUserId);
        ArgumentNullException.ThrowIfNull(scope);

        CreateObservationRequest? pending;
        try
        {
            pending = await _recovery.FindPendingAsync(
                actorAppUserId,
                scope,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return LocalDurabilityFailure("XQ_LOCAL_OBSERVATION_RECOVERY_UNAVAILABLE");
        }

        if (pending is null)
        {
            return CreateObservationResult.Failed(
                CreateObservationFailureKind.Validation,
                "XQ_NO_PENDING_OBSERVATION_RECOVERY");
        }

        var draft = await _drafts.OpenAsync(
            actorAppUserId,
            scope,
            cancellationToken).ConfigureAwait(false);

        var result = await _command.ExecuteAsync(
            pending,
            actorAppUserId,
            cancellationToken).ConfigureAwait(false);

        return await FinalizeAsync(
            actorAppUserId,
            scope,
            draft.Epoch,
            pending,
            result,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CreateObservationResult> FinalizeAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        long expectedDraftEpoch,
        CreateObservationRequest request,
        CreateObservationResult result,
        CancellationToken cancellationToken)
    {
        if (ShouldRetainForRetry(result.Failure))
        {
            return result;
        }

        if (result.IsSuccess)
        {
            var draftRetired = await TryRetireMatchingDraftAsync(
                actorAppUserId,
                scope,
                expectedDraftEpoch,
                request.RawText,
                cancellationToken).ConfigureAwait(false);

            if (!draftRetired)
            {
                // The server commit is authoritative. Keep the exact recovery
                // operation so a later explicit retry can reconcile local
                // cleanup without creating another Observation.
                return result;
            }

            try
            {
                await _recovery.RemoveAsync(
                    actorAppUserId,
                    request.OrganizationId,
                    request.OperationId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Safe: the remaining operation is idempotent and can only
                // resolve to the same authoritative Observation receipt.
            }

            return result;
        }

        try
        {
            await _recovery.MarkRejectedAsync(
                actorAppUserId,
                request.OrganizationId,
                request.OperationId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return LocalDurabilityFailure(
                "XQ_LOCAL_OBSERVATION_REJECTION_QUARANTINE_FAILED");
        }

        try
        {
            await _recovery.RemoveAsync(
                actorAppUserId,
                request.OrganizationId,
                request.OperationId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The rejected row is already hidden from pending discovery. The
            // teacher can correct the still-durable draft and submit a new
            // operation without colliding with the rejected intent.
        }

        return result;
    }

    private async Task<bool> TryRetireMatchingDraftAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        long expectedDraftEpoch,
        string committedText,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await _drafts.OpenAsync(
                actorAppUserId,
                scope,
                cancellationToken).ConfigureAwait(false);

            if (current.Recovered is null)
            {
                return true;
            }

            if (current.Epoch != expectedDraftEpoch ||
                !string.Equals(
                    current.Recovered.Text,
                    committedText,
                    StringComparison.Ordinal))
            {
                return false;
            }

            return await _drafts.DiscardAsync(
                actorAppUserId,
                scope,
                current.Epoch,
                cancellationToken).ConfigureAwait(false) is not null;
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

    private static bool ShouldRetainForRetry(CreateObservationFailure? failure) =>
        failure?.Kind is
            CreateObservationFailureKind.AuthenticationRequired or
            CreateObservationFailureKind.ResultUnknown or
            CreateObservationFailureKind.Transient or
            CreateObservationFailureKind.InvalidResponse;

    private static CreateObservationResult LocalDurabilityFailure(string code) =>
        CreateObservationResult.Failed(
            CreateObservationFailureKind.LocalDurabilityFailure,
            code);

    private static void RequireActor(Guid actorAppUserId)
    {
        if (actorAppUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Application actor id must be non-empty.",
                nameof(actorAppUserId));
        }
    }
}
