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
    private readonly ConcurrentDictionary<(Guid ActorId, Guid OrganizationId), SqliteCreateLearningCaseRecoveryStore> _stores = new();

    public WindowsCreateLearningCaseRecoveryStore(
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
        CreateLearningCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GetStore(actorAppUserId, request.OrganizationId)
            .SaveAsync(request, cancellationToken);
    }

    public Task<CreateLearningCaseRequest?> FindBySourceObservationAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid sourceObservationId,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, organizationId)
            .FindBySourceObservationAsync(
                organizationId,
                studentId,
                subjectProfileId,
                sourceObservationId,
                cancellationToken);

    public Task RemoveAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, organizationId)
            .RemoveAsync(operationId, cancellationToken);

    private SqliteCreateLearningCaseRecoveryStore GetStore(
        Guid actorAppUserId,
        Guid organizationId)
    {
        if (actorAppUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Application actor id must be non-empty.",
                nameof(actorAppUserId));
        }

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
}
