using System.Collections.Concurrent;
using Windows.Storage;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.LocalData;

internal sealed class WindowsActionProgressionRecoveryStore :
    IActionProgressionRecoveryStore
{
    private readonly string _environmentId;
    private readonly ApplicationData _applicationData;
    private readonly ConcurrentDictionary<Guid, SqliteCreateLearningCaseRecoveryIndex> _indexes = new();
    private readonly ConcurrentDictionary<(Guid ActorId, Guid OrganizationId), SqliteActionProgressionRecoveryStore> _stores = new();

    public WindowsActionProgressionRecoveryStore(
        string environmentId,
        ApplicationData applicationData)
    {
        _environmentId = string.IsNullOrWhiteSpace(environmentId)
            ? throw new ArgumentException("Environment id is required.", nameof(environmentId))
            : environmentId;
        _applicationData = applicationData ?? throw new ArgumentNullException(nameof(applicationData));
    }

    public async Task SaveAsync(
        Guid actorAppUserId,
        ActionProgressionRecoveryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        // The actor recovery index is shared with CreateLearningCase. Register
        // discovery before writing the organization-scoped encrypted intent so
        // membership loss cannot make a successfully persisted operation
        // undiscoverable after restart.
        await GetIndex(actorAppUserId)
            .RegisterOrganizationAsync(intent.OrganizationId, cancellationToken);

        await GetStore(actorAppUserId, intent.OrganizationId)
            .SaveAsync(intent, cancellationToken);
    }

    public async Task<ActionProgressionRecoveryIntent?> FindByPrimaryActionAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid caseId,
        Guid primaryActionId,
        CancellationToken cancellationToken = default)
    {
        await GetIndex(actorAppUserId)
            .RegisterOrganizationAsync(organizationId, cancellationToken);

        var store = TryGetExistingStore(actorAppUserId, organizationId);
        return store is null
            ? null
            : await store.FindByPrimaryActionAsync(
                organizationId,
                caseId,
                primaryActionId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<ActionProgressionRecoveryIntent>> ListPendingAsync(
        Guid actorAppUserId,
        IReadOnlyCollection<Guid> currentOrganizationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentOrganizationIds);

        var index = GetIndex(actorAppUserId);
        foreach (var organizationId in currentOrganizationIds.Distinct())
        {
            if (organizationId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Organization ids must be non-empty.",
                    nameof(currentOrganizationIds));
            }

            await index.RegisterOrganizationAsync(organizationId, cancellationToken);
        }

        var indexedOrganizations = await index.ListOrganizationsAsync(cancellationToken);
        var pending = new List<ActionProgressionRecoveryIntent>();

        foreach (var organizationId in indexedOrganizations)
        {
            var store = TryGetExistingStore(actorAppUserId, organizationId);
            if (store is null)
            {
                continue;
            }

            pending.AddRange(
                await store.ListPendingAsync(organizationId, cancellationToken));
        }

        return pending
            .OrderBy(item => item.OrganizationId)
            .ThenBy(item => item.OperationId)
            .ToArray();
    }

    public Task MarkRejectedAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var store = TryGetExistingStore(actorAppUserId, organizationId);
        return store is null
            ? Task.CompletedTask
            : store.MarkRejectedAsync(operationId, cancellationToken);
    }

    public Task RemoveAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var store = TryGetExistingStore(actorAppUserId, organizationId);
        return store is null
            ? Task.CompletedTask
            : store.RemoveAsync(operationId, cancellationToken);
    }

    private SqliteCreateLearningCaseRecoveryIndex GetIndex(Guid actorAppUserId)
    {
        RequireActor(actorAppUserId);

        return _indexes.GetOrAdd(
            actorAppUserId,
            actorId =>
            {
                var scope = WindowsActorLocalDataScope.Create(
                    _applicationData,
                    _environmentId,
                    actorId.ToString("D"));
                var path = WindowsLocalStatePaths.GetOnlineCommandRecoveryIndexDatabasePath(
                    _applicationData.LocalFolder,
                    scope);
                return new SqliteCreateLearningCaseRecoveryIndex(path);
            });
    }

    private SqliteActionProgressionRecoveryStore GetStore(
        Guid actorAppUserId,
        Guid organizationId)
    {
        RequireOrganization(organizationId);
        RequireActor(actorAppUserId);

        return _stores.GetOrAdd(
            (actorAppUserId, organizationId),
            key =>
            {
                var path = GetStorePath(key.ActorId, key.OrganizationId);
                return new SqliteActionProgressionRecoveryStore(path);
            });
    }

    private SqliteActionProgressionRecoveryStore? TryGetExistingStore(
        Guid actorAppUserId,
        Guid organizationId)
    {
        RequireOrganization(organizationId);
        RequireActor(actorAppUserId);

        if (_stores.TryGetValue(
                (actorAppUserId, organizationId),
                out var existing))
        {
            return existing;
        }

        var path = GetStorePath(actorAppUserId, organizationId);
        return File.Exists(path)
            ? _stores.GetOrAdd(
                (actorAppUserId, organizationId),
                _ => new SqliteActionProgressionRecoveryStore(path))
            : null;
    }

    private string GetStorePath(
        Guid actorAppUserId,
        Guid organizationId)
    {
        var scope = WindowsLocalDataScope.Create(
            _applicationData,
            _environmentId,
            actorAppUserId.ToString("D"),
            organizationId.ToString("D"));
        return WindowsLocalStatePaths.GetActionProgressionRecoveryDatabasePath(
            _applicationData.LocalFolder,
            scope);
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

    private static void RequireOrganization(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id must be non-empty.",
                nameof(organizationId));
        }
    }
}
