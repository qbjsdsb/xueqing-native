using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public interface IPersonalBootstrapReader
{
    Task<PersonalBootstrapReadResult> ReadAsync(CancellationToken cancellationToken = default);
}
