using System.Collections.Concurrent;
using Windows.Storage;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.LocalData;

internal sealed class WindowsOrganizationInvitationRecoveryStore :
    IOrganizationInvitationRecoveryStore
{
    private readonly string _environmentId;
    private readonly ApplicationData _applicationData;
    private readonly ConcurrentDictionary<
        (Guid ActorId, Guid OrganizationId),
        SqliteOrganizationInvitationRecoveryStore> _stores = new();

    public WindowsOrganizationInvitationRecoveryStore(
        string environmentId,
        ApplicationData applicationData)
    {
        _environmentId = string.IsNullOrWhiteSpace(environmentId)
            ? throw new ArgumentException("Environment id is required.", nameof(environmentId))
            : environmentId;
        _applicationData = applicationData ??
            throw new ArgumentNullException(nameof(applicationData));
    }

    public Task SaveAsync(
        Guid actorAppUserId,
        OrganizationInvitationRecoveryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return GetStore(actorAppUserId, intent.OrganizationId)
            .SaveAsync(intent, cancellationToken);
    }

    public Task<IReadOnlyList<OrganizationInvitationRecoveryIntent>> ListAsync(
        Guid actorAppUserId,
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, organizationId)
            .ListAsync(organizationId, cancellationToken);

    public Task RemoveAsync(
        Guid actorAppUserId,
        Guid organizationId,
        Guid createOperationId,
        CancellationToken cancellationToken = default) =>
        GetStore(actorAppUserId, organizationId)
            .RemoveAsync(createOperationId, cancellationToken);

    private SqliteOrganizationInvitationRecoveryStore GetStore(
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
                var path =
                    WindowsLocalStatePaths.GetOrganizationInvitationRecoveryDatabasePath(
                        _applicationData.LocalFolder,
                        scope);
                return new SqliteOrganizationInvitationRecoveryStore(path);
            });
    }
}
