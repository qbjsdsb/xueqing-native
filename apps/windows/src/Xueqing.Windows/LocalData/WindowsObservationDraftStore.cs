using System.Collections.Concurrent;
using Windows.Storage;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.LocalData;

internal sealed class WindowsObservationDraftStore : IObservationDraftStore
{
    private readonly string _environmentId;
    private readonly ApplicationData _applicationData;
    private readonly ConcurrentDictionary<(Guid ActorId, Guid OrganizationId), SqliteObservationDraftStore> _stores = new();

    public WindowsObservationDraftStore(
        string environmentId,
        ApplicationData applicationData)
    {
        _environmentId = string.IsNullOrWhiteSpace(environmentId)
            ? throw new ArgumentException("Environment id is required.", nameof(environmentId))
            : environmentId;
        _applicationData = applicationData ?? throw new ArgumentNullException(nameof(applicationData));
    }

    public Task<ObservationDraftOpenResult> OpenAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, scope.OrganizationId)
            .OpenAsync(scope, cancellationToken);

    public Task<bool> SaveAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        long expectedEpoch,
        string text,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, scope.OrganizationId)
            .SaveAsync(
                scope,
                expectedEpoch,
                text,
                DateTimeOffset.UtcNow,
                cancellationToken);

    public Task<long?> DiscardAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        long expectedEpoch,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, scope.OrganizationId)
            .DiscardAsync(scope, expectedEpoch, cancellationToken);

    private SqliteObservationDraftStore GetStore(
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
                return new SqliteObservationDraftStore(
                    WindowsLocalStatePaths.GetObservationDraftDatabasePath(
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
