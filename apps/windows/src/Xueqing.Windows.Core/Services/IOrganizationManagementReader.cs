using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IOrganizationManagementReader
{
    Task<OrganizationManagementReadResult> ReadAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
