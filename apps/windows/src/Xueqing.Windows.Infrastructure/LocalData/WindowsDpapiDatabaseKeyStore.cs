using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Xueqing.Windows.Infrastructure.LocalData;

internal sealed class LocalDatabaseKeyUnavailableException : InvalidOperationException
{
    public LocalDatabaseKeyUnavailableException(string message)
        : base(message)
    {
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsDpapiDatabaseKeyStore
{
    private const int MasterKeyLengthBytes = 32;
    private const int ExistingKeyReadAttempts = 8;
    private const int ExistingKeyReadRetryDelayMilliseconds = 20;
    private const string EntropyPrefix = "xueqing-native/windows/local-database-key/v1/";

    private readonly string _databasePath;
    private readonly string _wrappedKeyPath;
    private readonly byte[] _entropy;

    public WindowsDpapiDatabaseKeyStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = Path.GetFullPath(databasePath);
        _wrappedKeyPath = _databasePath + ".key";
        _entropy = SHA256.HashData(Encoding.UTF8.GetBytes(EntropyPrefix + Path.GetFileName(_databasePath)));
    }

    public async Task<byte[]> LoadOrCreateAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_wrappedKeyPath))
        {
            return await LoadExistingAsync(cancellationToken);
        }

        if (File.Exists(_databasePath))
        {
            throw new LocalDatabaseKeyUnavailableException(
                "Encrypted local database exists but its DPAPI-wrapped key is missing. Refusing to create a replacement key.");
        }

        var directory = Path.GetDirectoryName(_wrappedKeyPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var masterKey = RandomNumberGenerator.GetBytes(MasterKeyLengthBytes);
        byte[]? wrappedKey = null;
        var temporaryPath = _wrappedKeyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            wrappedKey = ProtectedData.Protect(masterKey, _entropy, DataProtectionScope.CurrentUser);
            await WriteTemporaryKeyAsync(temporaryPath, wrappedKey, cancellationToken);

            try
            {
                File.Move(temporaryPath, _wrappedKeyPath, overwrite: false);
                return masterKey;
            }
            catch (IOException) when (File.Exists(_wrappedKeyPath))
            {
                CryptographicOperations.ZeroMemory(masterKey);
                return await LoadExistingAsync(cancellationToken);
            }
        }
        catch
        {
            CryptographicOperations.ZeroMemory(masterKey);
            throw;
        }
        finally
        {
            if (wrappedKey is not null)
            {
                CryptographicOperations.ZeroMemory(wrappedKey);
            }

            TryDelete(temporaryPath);
        }
    }

    private async Task<byte[]> LoadExistingAsync(CancellationToken cancellationToken)
    {
        var wrappedKey = await ReadExistingWrappedKeyAsync(cancellationToken);
        try
        {
            var masterKey = ProtectedData.Unprotect(wrappedKey, _entropy, DataProtectionScope.CurrentUser);
            if (masterKey.Length == MasterKeyLengthBytes)
            {
                return masterKey;
            }

            CryptographicOperations.ZeroMemory(masterKey);
            throw new CryptographicException("DPAPI-unwrapped local database key has an invalid length.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappedKey);
        }
    }

    private async Task<byte[]> ReadExistingWrappedKeyAsync(CancellationToken cancellationToken)
    {
        IOException? lastSharingFailure = null;

        for (var attempt = 1; attempt <= ExistingKeyReadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await File.ReadAllBytesAsync(_wrappedKeyPath, cancellationToken);
            }
            catch (IOException exception)
                when (File.Exists(_wrappedKeyPath) && attempt < ExistingKeyReadAttempts)
            {
                lastSharingFailure = exception;
                await Task.Delay(ExistingKeyReadRetryDelayMilliseconds, cancellationToken);
            }
        }

        if (lastSharingFailure is not null)
        {
            throw new IOException(
                "The authoritative DPAPI-wrapped database key remained temporarily unavailable after bounded retry.",
                lastSharingFailure);
        }

        // The loop either returns or throws from File.ReadAllBytesAsync. This is defensive only.
        throw new IOException("The authoritative DPAPI-wrapped database key could not be read.");
    }

    private static async Task WriteTemporaryKeyAsync(
        string path,
        ReadOnlyMemory<byte> wrappedKey,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            });

        await stream.WriteAsync(wrappedKey, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A stale temporary key file contains only DPAPI-protected bytes and is never treated as authoritative.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup failure must not replace or invalidate the authoritative wrapped key.
        }
    }
}
