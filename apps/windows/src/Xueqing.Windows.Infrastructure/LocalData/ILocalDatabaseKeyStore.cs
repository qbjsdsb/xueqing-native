namespace Xueqing.Windows.Infrastructure.LocalData;

/// <summary>
/// Platform-neutral contract for loading or creating a 256-bit (32-byte)
/// SQLite3MC master key wrapped by the host OS key protection mechanism
/// (Windows DPAPI CurrentUser, Linux Secret Service / libsecret, etc.).
/// Implementations must fail closed if an existing database has no usable key
/// and must never fall back to plaintext SQLite.
/// </summary>
public interface ILocalDatabaseKeyStore
{
    Task<byte[]> LoadOrCreateAsync(CancellationToken cancellationToken = default);
}

public sealed class LocalDatabaseKeyUnavailableException : InvalidOperationException
{
    public LocalDatabaseKeyUnavailableException(string message)
        : base(message)
    {
    }

    public LocalDatabaseKeyUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
