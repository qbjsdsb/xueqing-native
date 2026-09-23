using System.Collections.Concurrent;
using Windows.Storage;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.LocalData;

internal sealed class WindowsCreateObservationRecoveryStore :
    ICreateObservationRecoveryStore
{
    private readonly string _environmentId;
    private readonly ApplicationData _applicationData;
    private readonly ConcurrentDictionary<(Guid ActorId, Guid OrganizationId), SqliteCreateObservationRecoveryStore> _stores = new();

    public WindowsCreateObservationRecoveryStore(
        string environmentId,
        ApplicationData applicationData)
    {
        _environmentId = string.IsNullOrWhiteSpace(environmentId)
            ? throw new ArgumentException("Environment id is required.", nameof(environmentId))
            : environmentId;
        _applicationData = applicationData ?? throw new ArgumentNullException(nameof(applicationData));
    }

    public Task SaveAsync(
        Guid actorAppUserId,
        CreateObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GetStore(actorAppUserId, request.OrganizationId)
            .SaveAsync(request, cancellationToken);
    }

    public Task<CreateObservationRequest?> FindPendingAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return GetStore(actorAppUserId, scope.OrganizationId)
            .FindPendingAsync(scope, cancellationToken);
    }

    public Task MarkRejectedAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, organizationId)
            .MarkRejectedAsync(operationId, cancellationToken);

    public Task RemoveAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, organizationId)
            .RemoveAsync(operationId, cancellationToken);

    private SqliteCreateObservationRecoveryStore GetStore(
        Guid actorAppUserId,
        Guid organizationId)
    {
        RequireActor(actorAppUserId);
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id must be non-empty.",
                nameof(organizationId));
        }

        return _stores.GetOrAdd(
            (actorAppUserId, organizationId),
            key =>
            {
                var scope = WindowsLocalDataScope.Create(
                    _applicationData,
                    _environmentId,
                    key.ActorId.ToString("D"),
                    key.OrganizationId.ToString("D"));
                return new SqliteCreateObservationRecoveryStore(
                    WindowsLocalStatePaths.GetCreateObservationRecoveryDatabasePath(
                        _applicationData.LocalFolder,
                        scope));
            });
    }

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
