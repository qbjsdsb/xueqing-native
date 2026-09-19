using System.Collections.Concurrent;
using Windows.Storage;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.LocalData;

internal sealed class WindowsCreateLearningCaseRecoveryStore :
    ICreateLearningCaseRecoveryStore
{
    private readonly string _environmentId;
    private readonly ApplicationData _applicationData;
    private readonly ConcurrentDictionary<Guid, SqliteCreateLearningCaseRecoveryIndex> _indexes = new();
    private readonly ConcurrentDictionary<(Guid ActorId, Guid OrganizationId), SqliteCreateLearningCaseRecoveryStore> _stores = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _legacyMigrationGates = new();
    private readonly ConcurrentDictionary<Guid, byte> _legacyMigrationCompleted = new();

    public WindowsCreateLearningCaseRecoveryStore(
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
        CreateLearningCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Register discovery before persisting the organization-scoped intent.
        // If the second write fails the durable index may contain a harmless
        // stale organization, but the inverse (intent without discovery) is
        // deliberately impossible for all new saves.
        await GetIndex(actorAppUserId)
            .RegisterOrganizationAsync(request.OrganizationId, cancellationToken);

        await GetStore(actorAppUserId, request.OrganizationId)
            .SaveAsync(request, cancellationToken);
    }

    public async Task<CreateLearningCaseRequest?> FindBySourceObservationAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid sourceObservationId,
        CancellationToken cancellationToken = default)
    {
        // Reading a known current scope also backfills the actor index for
        // recovery databases created before the index existed.
        await GetIndex(actorAppUserId)
            .RegisterOrganizationAsync(organizationId, cancellationToken);

        return await GetStore(actorAppUserId, organizationId)
            .FindBySourceObservationAsync(
                organizationId,
                studentId,
                subjectProfileId,
                sourceObservationId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<CreateLearningCaseRequest>> ListPendingAsync(
        Guid actorAppUserId,
        IReadOnlyCollection<Guid> currentOrganizationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentOrganizationIds);

        var index = GetIndex(actorAppUserId);
        await MigrateLegacyRecoveryScopesAsync(
            actorAppUserId,
            index,
            cancellationToken);

        // Current organizations are migration/backfill hints only. Discovery
        // subsequently uses the durable actor index, so a later membership
        // removal does not hide an already indexed unresolved operation.
        foreach (var organizationId in currentOrganizationIds.Distinct())
        {
            if (organizationId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Organization ids must be non-empty.",
                    nameof(currentOrganizationIds));
            }

            await index.RegisterOrganizationAsync(
                organizationId,
                cancellationToken);
        }

        var indexedOrganizations = await index.ListOrganizationsAsync(cancellationToken);
        var pending = new List<CreateLearningCaseRequest>();

        foreach (var organizationId in indexedOrganizations)
        {
            var scopedPending = await GetStore(actorAppUserId, organizationId)
                .ListPendingAsync(organizationId, cancellationToken);
            pending.AddRange(scopedPending);
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

    private async Task MigrateLegacyRecoveryScopesAsync(
        Guid actorAppUserId,
        SqliteCreateLearningCaseRecoveryIndex index,
        CancellationToken cancellationToken)
    {
        if (_legacyMigrationCompleted.ContainsKey(actorAppUserId))
        {
            return;
        }

        var gate = _legacyMigrationGates.GetOrAdd(
            actorAppUserId,
            static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_legacyMigrationCompleted.ContainsKey(actorAppUserId))
            {
                return;
            }

            var actorScope = WindowsActorLocalDataScope.Create(
                _applicationData,
                _environmentId,
                actorAppUserId.ToString("D"));
            var candidates =
                WindowsLocalStatePaths.EnumerateLegacyOnlineCommandRecoveryDatabasePaths(
                    _applicationData.LocalFolder,
                    actorScope.InstallationId);

            foreach (var databasePath in candidates)
            {
                IReadOnlyList<Guid> candidateOrganizationIds;
                try
                {
                    var candidateStore =
                        new SqliteCreateLearningCaseRecoveryStore(databasePath);
                    candidateOrganizationIds =
                        await candidateStore.InspectStoredOrganizationIdsAsync(
                            cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Legacy discovery is best-effort per candidate. A damaged,
                    // foreign or otherwise unreadable store must not prevent
                    // already-indexed authoritative recoveries from loading.
                    continue;
                }

                foreach (var organizationId in candidateOrganizationIds)
                {
                    var candidateScope = new WindowsLocalDataScope(
                        _environmentId,
                        actorAppUserId.ToString("D"),
                        organizationId.ToString("D"),
                        actorScope.InstallationId);

                    if (!WindowsLocalStatePaths.MatchesOrganizationRecoveryScope(
                            databasePath,
                            candidateScope))
                    {
                        continue;
                    }

                    try
                    {
                        // Ownership is now proven by the deterministic path.
                        // Before making the scope durable in the actor index,
                        // perform the same initialization/migration and payload
                        // deserialization that normal indexed loading will use.
                        // A damaged legacy store is therefore isolated here and
                        // cannot poison all future recovery enumeration.
                        var ownedCandidateStore =
                            new SqliteCreateLearningCaseRecoveryStore(databasePath);
                        _ = await ownedCandidateStore.ListPendingAsync(
                            organizationId,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        continue;
                    }

                    await index.RegisterOrganizationAsync(
                        organizationId,
                        cancellationToken);
                }
            }

            _legacyMigrationCompleted.TryAdd(actorAppUserId, 0);
        }
        finally
        {
            gate.Release();
        }
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

    private SqliteCreateLearningCaseRecoveryStore GetStore(
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
                var path = WindowsLocalStatePaths.GetOnlineCommandRecoveryDatabasePath(
                    _applicationData.LocalFolder,
                    scope);
                return new SqliteCreateLearningCaseRecoveryStore(path);
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
