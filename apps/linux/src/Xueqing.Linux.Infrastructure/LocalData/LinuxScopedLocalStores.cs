using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Xueqing.Linux.Infrastructure.Security;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.LocalData;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Linux.Infrastructure.LocalData;

/// <summary>
/// Shared scope context for Linux encrypted local databases.
/// Uses LinuxSecretServiceDatabaseKeyStore (libsecret) by default, and accepts
/// an injectable ILocalDatabaseKeyStore factory for headless unit verification.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxLocalStoreContext
{
    public LinuxLocalStoreContext(
        string environmentId,
        string? appDataRoot = null,
        Func<string, ILocalDatabaseKeyStore>? keyStoreFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);

        EnvironmentId = environmentId;
        AppDataRoot = string.IsNullOrWhiteSpace(appDataRoot)
            ? LinuxLocalStatePaths.GetAppDataRoot()
            : Path.GetFullPath(appDataRoot);
        InstallationId = LinuxLocalStatePaths.LoadOrCreateInstallationId(AppDataRoot);
        KeyStoreFactory = keyStoreFactory ?? (static path => new LinuxSecretServiceDatabaseKeyStore(path));
    }

    public string EnvironmentId { get; }
    public string AppDataRoot { get; }
    public Guid InstallationId { get; }
    public Func<string, ILocalDatabaseKeyStore> KeyStoreFactory { get; }

    public string GetOrgDbPath(Guid actorAppUserId, Guid organizationId, string fileName)
    {
        RequireActor(actorAppUserId);
        RequireOrganization(organizationId);
        return LinuxLocalStatePaths.GetScopedDatabasePath(
            EnvironmentId,
            actorAppUserId.ToString("D"),
            organizationId.ToString("D"),
            InstallationId,
            fileName,
            AppDataRoot);
    }

    public string GetActorIndexDbPath(Guid actorAppUserId)
    {
        RequireActor(actorAppUserId);
        return LinuxLocalStatePaths.GetOnlineCommandRecoveryIndexDatabasePath(
            EnvironmentId,
            actorAppUserId.ToString("D"),
            InstallationId,
            AppDataRoot);
    }

    internal static void RequireActor(Guid actorAppUserId)
    {
        if (actorAppUserId == Guid.Empty)
        {
            throw new ArgumentException("Application actor id must be non-empty.", nameof(actorAppUserId));
        }
    }

    internal static void RequireOrganization(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization id must be non-empty.", nameof(organizationId));
        }
    }
}

[SupportedOSPlatform("linux")]
public sealed class LinuxObservationDraftStore : IObservationDraftStore
{
    private readonly LinuxLocalStoreContext _context;
    private readonly ConcurrentDictionary<(Guid ActorId, Guid OrgId), SqliteObservationDraftStore> _stores = new();

    public LinuxObservationDraftStore(LinuxLocalStoreContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public Task<ObservationDraftOpenResult> OpenAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, scope.OrganizationId).OpenAsync(scope, cancellationToken);

    public Task<bool> SaveAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        long expectedEpoch,
        string text,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, scope.OrganizationId)
            .SaveAsync(scope, expectedEpoch, text, DateTimeOffset.UtcNow, cancellationToken);

    public Task<long?> DiscardAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        long expectedEpoch,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, scope.OrganizationId)
            .DiscardAsync(scope, expectedEpoch, cancellationToken);

    private SqliteObservationDraftStore GetStore(Guid actorId, Guid orgId) =>
        _stores.GetOrAdd((actorId, orgId), key =>
        {
            var path = _context.GetOrgDbPath(key.ActorId, key.OrgId, "observation-draft.db");
            return new SqliteObservationDraftStore(path, _context.KeyStoreFactory(path));
        });
}

[SupportedOSPlatform("linux")]
public sealed class LinuxCreateObservationRecoveryStore : ICreateObservationRecoveryStore
{
    private readonly LinuxLocalStoreContext _context;
    private readonly ConcurrentDictionary<(Guid ActorId, Guid OrgId), SqliteCreateObservationRecoveryStore> _stores = new();

    public LinuxCreateObservationRecoveryStore(LinuxLocalStoreContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public Task SaveAsync(
        Guid actorAppUserId,
        CreateObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GetStore(actorAppUserId, request.OrganizationId).SaveAsync(request, cancellationToken);
    }

    public Task<CreateObservationRequest?> FindPendingAsync(
        Guid actorAppUserId,
        ObservationDraftScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return GetStore(actorAppUserId, scope.OrganizationId).FindPendingAsync(scope, cancellationToken);
    }

    public Task MarkRejectedAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, organizationId).MarkRejectedAsync(operationId, cancellationToken);

    public Task RemoveAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, organizationId).RemoveAsync(operationId, cancellationToken);

    private SqliteCreateObservationRecoveryStore GetStore(Guid actorId, Guid orgId) =>
        _stores.GetOrAdd((actorId, orgId), key =>
        {
            var path = _context.GetOrgDbPath(key.ActorId, key.OrgId, "create-observation-recovery.db");
            return new SqliteCreateObservationRecoveryStore(path, _context.KeyStoreFactory(path));
        });
}

[SupportedOSPlatform("linux")]
public sealed class LinuxCreateLearningCaseRecoveryStore : ICreateLearningCaseRecoveryStore
{
    private readonly LinuxLocalStoreContext _context;
    private readonly ConcurrentDictionary<Guid, SqliteCreateLearningCaseRecoveryIndex> _indexes = new();
    private readonly ConcurrentDictionary<(Guid ActorId, Guid OrgId), SqliteCreateLearningCaseRecoveryStore> _stores = new();

    public LinuxCreateLearningCaseRecoveryStore(LinuxLocalStoreContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task SaveAsync(
        Guid actorAppUserId,
        CreateLearningCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await GetIndex(actorAppUserId).RegisterOrganizationAsync(request.OrganizationId, cancellationToken);
        await GetStore(actorAppUserId, request.OrganizationId).SaveAsync(request, cancellationToken);
    }

    public async Task<CreateLearningCaseRequest?> FindBySourceObservationAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid sourceObservationId,
        CancellationToken cancellationToken = default)
    {
        await GetIndex(actorAppUserId).RegisterOrganizationAsync(organizationId, cancellationToken);
        return await GetStore(actorAppUserId, organizationId)
            .FindBySourceObservationAsync(organizationId, studentId, subjectProfileId, sourceObservationId, cancellationToken);
    }

    public async Task<IReadOnlyList<CreateLearningCaseRequest>> ListPendingAsync(
        Guid actorAppUserId,
        IReadOnlyCollection<Guid> currentOrganizationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentOrganizationIds);

        var index = GetIndex(actorAppUserId);
        foreach (var organizationId in currentOrganizationIds.Distinct())
        {
            LinuxLocalStoreContext.RequireOrganization(organizationId);
            await index.RegisterOrganizationAsync(organizationId, cancellationToken);
        }

        var indexedOrganizations = await index.ListOrganizationsAsync(cancellationToken);
        var pending = new List<CreateLearningCaseRequest>();
        foreach (var organizationId in indexedOrganizations)
        {
            var store = TryGetExistingStore(actorAppUserId, organizationId);
            if (store is null)
            {
                continue;
            }

            pending.AddRange(await store.ListPendingAsync(organizationId, cancellationToken));
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
        GetStore(actorAppUserId, organizationId).MarkRejectedAsync(operationId, cancellationToken);

    public Task RemoveAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, organizationId).RemoveAsync(operationId, cancellationToken);

    private SqliteCreateLearningCaseRecoveryIndex GetIndex(Guid actorId) =>
        _indexes.GetOrAdd(actorId, id =>
        {
            var path = _context.GetActorIndexDbPath(id);
            return new SqliteCreateLearningCaseRecoveryIndex(path, _context.KeyStoreFactory(path));
        });

    private SqliteCreateLearningCaseRecoveryStore GetStore(Guid actorId, Guid orgId) =>
        _stores.GetOrAdd((actorId, orgId), key =>
        {
            var path = _context.GetOrgDbPath(key.ActorId, key.OrgId, "online-command-recovery.db");
            return new SqliteCreateLearningCaseRecoveryStore(path, _context.KeyStoreFactory(path));
        });

    private SqliteCreateLearningCaseRecoveryStore? TryGetExistingStore(Guid actorId, Guid orgId)
    {
        if (_stores.TryGetValue((actorId, orgId), out var existing))
        {
            return existing;
        }

        var path = _context.GetOrgDbPath(actorId, orgId, "online-command-recovery.db");
        return File.Exists(path)
            ? _stores.GetOrAdd((actorId, orgId), _ => new SqliteCreateLearningCaseRecoveryStore(path, _context.KeyStoreFactory(path)))
            : null;
    }
}

[SupportedOSPlatform("linux")]
public sealed class LinuxActionProgressionRecoveryStore : IActionProgressionRecoveryStore
{
    private readonly LinuxLocalStoreContext _context;
    private readonly ConcurrentDictionary<Guid, SqliteCreateLearningCaseRecoveryIndex> _indexes = new();
    private readonly ConcurrentDictionary<(Guid ActorId, Guid OrgId), SqliteActionProgressionRecoveryStore> _stores = new();

    public LinuxActionProgressionRecoveryStore(LinuxLocalStoreContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task SaveAsync(
        Guid actorAppUserId,
        ActionProgressionRecoveryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        await GetIndex(actorAppUserId).RegisterOrganizationAsync(intent.OrganizationId, cancellationToken);
        await GetStore(actorAppUserId, intent.OrganizationId).SaveAsync(intent, cancellationToken);
    }

    public async Task<ActionProgressionRecoveryIntent?> FindByPrimaryActionAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid caseId,
        Guid primaryActionId,
        CancellationToken cancellationToken = default)
    {
        await GetIndex(actorAppUserId).RegisterOrganizationAsync(organizationId, cancellationToken);
        var store = TryGetExistingStore(actorAppUserId, organizationId);
        return store is null
            ? null
            : await store.FindByPrimaryActionAsync(organizationId, caseId, primaryActionId, cancellationToken);
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
            LinuxLocalStoreContext.RequireOrganization(organizationId);
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

            pending.AddRange(await store.ListPendingAsync(organizationId, cancellationToken));
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
        return store is null ? Task.CompletedTask : store.MarkRejectedAsync(operationId, cancellationToken);
    }

    public Task RemoveAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var store = TryGetExistingStore(actorAppUserId, organizationId);
        return store is null ? Task.CompletedTask : store.RemoveAsync(operationId, cancellationToken);
    }

    private SqliteCreateLearningCaseRecoveryIndex GetIndex(Guid actorId) =>
        _indexes.GetOrAdd(actorId, id =>
        {
            var path = _context.GetActorIndexDbPath(id);
            return new SqliteCreateLearningCaseRecoveryIndex(path, _context.KeyStoreFactory(path));
        });

    private SqliteActionProgressionRecoveryStore GetStore(Guid actorId, Guid orgId) =>
        _stores.GetOrAdd((actorId, orgId), key =>
        {
            var path = _context.GetOrgDbPath(key.ActorId, key.OrgId, "action-progression-recovery.db");
            return new SqliteActionProgressionRecoveryStore(path, _context.KeyStoreFactory(path));
        });

    private SqliteActionProgressionRecoveryStore? TryGetExistingStore(Guid actorId, Guid orgId)
    {
        if (_stores.TryGetValue((actorId, orgId), out var existing))
        {
            return existing;
        }

        var path = _context.GetOrgDbPath(actorId, orgId, "action-progression-recovery.db");
        return File.Exists(path)
            ? _stores.GetOrAdd((actorId, orgId), _ => new SqliteActionProgressionRecoveryStore(path, _context.KeyStoreFactory(path)))
            : null;
    }
}

[SupportedOSPlatform("linux")]
public sealed class LinuxCaseLifecycleRecoveryStore : ICaseLifecycleRecoveryStore
{
    private readonly LinuxLocalStoreContext _context;
    private readonly ConcurrentDictionary<Guid, SqliteCreateLearningCaseRecoveryIndex> _indexes = new();
    private readonly ConcurrentDictionary<(Guid ActorId, Guid OrgId), SqliteCaseLifecycleRecoveryStore> _stores = new();

    public LinuxCaseLifecycleRecoveryStore(LinuxLocalStoreContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task SaveAsync(
        Guid actorAppUserId,
        CaseLifecycleRecoveryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        await GetIndex(actorAppUserId).RegisterOrganizationAsync(intent.OrganizationId, cancellationToken);
        await GetStore(actorAppUserId, intent.OrganizationId).SaveAsync(intent, cancellationToken);
    }

    public async Task<CaseLifecycleRecoveryIntent?> FindByCaseAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid caseId,
        CancellationToken cancellationToken = default)
    {
        await GetIndex(actorAppUserId).RegisterOrganizationAsync(organizationId, cancellationToken);
        var store = TryGetExistingStore(actorAppUserId, organizationId);
        return store is null
            ? null
            : await store.FindByCaseAsync(organizationId, caseId, cancellationToken);
    }

    public async Task<IReadOnlyList<CaseLifecycleRecoveryIntent>> ListPendingAsync(
        Guid actorAppUserId,
        IReadOnlyCollection<Guid> currentOrganizationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentOrganizationIds);

        var index = GetIndex(actorAppUserId);
        foreach (var organizationId in currentOrganizationIds.Distinct())
        {
            LinuxLocalStoreContext.RequireOrganization(organizationId);
            await index.RegisterOrganizationAsync(organizationId, cancellationToken);
        }

        var indexedOrganizations = await index.ListOrganizationsAsync(cancellationToken);
        var pending = new List<CaseLifecycleRecoveryIntent>();
        foreach (var organizationId in indexedOrganizations)
        {
            var store = TryGetExistingStore(actorAppUserId, organizationId);
            if (store is null)
            {
                continue;
            }

            pending.AddRange(await store.ListPendingAsync(organizationId, cancellationToken));
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
        return store is null ? Task.CompletedTask : store.MarkRejectedAsync(operationId, cancellationToken);
    }

    public Task RemoveAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var store = TryGetExistingStore(actorAppUserId, organizationId);
        return store is null ? Task.CompletedTask : store.RemoveAsync(operationId, cancellationToken);
    }

    private SqliteCreateLearningCaseRecoveryIndex GetIndex(Guid actorId) =>
        _indexes.GetOrAdd(actorId, id =>
        {
            var path = _context.GetActorIndexDbPath(id);
            return new SqliteCreateLearningCaseRecoveryIndex(path, _context.KeyStoreFactory(path));
        });

    private SqliteCaseLifecycleRecoveryStore GetStore(Guid actorId, Guid orgId) =>
        _stores.GetOrAdd((actorId, orgId), key =>
        {
            var path = _context.GetOrgDbPath(key.ActorId, key.OrgId, "case-lifecycle-recovery.db");
            return new SqliteCaseLifecycleRecoveryStore(path, _context.KeyStoreFactory(path));
        });

    private SqliteCaseLifecycleRecoveryStore? TryGetExistingStore(Guid actorId, Guid orgId)
    {
        if (_stores.TryGetValue((actorId, orgId), out var existing))
        {
            return existing;
        }

        var path = _context.GetOrgDbPath(actorId, orgId, "case-lifecycle-recovery.db");
        return File.Exists(path)
            ? _stores.GetOrAdd((actorId, orgId), _ => new SqliteCaseLifecycleRecoveryStore(path, _context.KeyStoreFactory(path)))
            : null;
    }
}

[SupportedOSPlatform("linux")]
public sealed class LinuxOrganizationInvitationRecoveryStore : IOrganizationInvitationRecoveryStore
{
    private readonly LinuxLocalStoreContext _context;
    private readonly ConcurrentDictionary<(Guid ActorId, Guid OrgId), SqliteOrganizationInvitationRecoveryStore> _stores = new();

    public LinuxOrganizationInvitationRecoveryStore(LinuxLocalStoreContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public Task SaveAsync(
        Guid actorAppUserId,
        OrganizationInvitationRecoveryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return GetStore(actorAppUserId, intent.OrganizationId).SaveAsync(intent, cancellationToken);
    }

    public Task<IReadOnlyList<OrganizationInvitationRecoveryIntent>> ListAsync(
        Guid actorAppUserId,
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, organizationId).ListAsync(organizationId, cancellationToken);

    public Task RemoveAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid createOperationId,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, organizationId).RemoveAsync(createOperationId, cancellationToken);

    private SqliteOrganizationInvitationRecoveryStore GetStore(Guid actorId, Guid orgId) =>
        _stores.GetOrAdd((actorId, orgId), key =>
        {
            var path = _context.GetOrgDbPath(key.ActorId, key.OrgId, "organization-invitation-recovery.db");
            return new SqliteOrganizationInvitationRecoveryStore(path, _context.KeyStoreFactory(path));
        });
}
