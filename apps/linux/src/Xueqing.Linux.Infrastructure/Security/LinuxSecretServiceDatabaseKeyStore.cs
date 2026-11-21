using System.Runtime.Versioning;
using System.Security.Cryptography;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Linux.Infrastructure.Security;

/// <summary>
/// Production Linux implementation of ILocalDatabaseKeyStore backed by
/// libsecret / org.freedesktop.secrets (gnome-keyring / KWallet).
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSecretServiceDatabaseKeyStore : ILocalDatabaseKeyStore
{
    private const int MasterKeyLengthBytes = 32;
    private const string SchemaName = "com.xueqing.app.local-database-key.v1";
    private const string PurposeValue = "local-database-key";

    private readonly string _databasePath;
    private readonly string _scopeHash;

    public LinuxSecretServiceDatabaseKeyStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = Path.GetFullPath(databasePath);
        _scopeHash = LinuxSecretServiceNative.ComputeScopeHash(
            "xueqing-native/linux/local-database-key/v1/" + _databasePath);
    }

    public Task<byte[]> LoadOrCreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existingSecret = LinuxSecretServiceNative.LookupUtf8Secret(
            SchemaName,
            PurposeValue,
            _scopeHash,
            static msg => new LocalDatabaseKeyUnavailableException(msg));

        if (existingSecret is not null)
        {
            return Task.FromResult(DecodeMasterKey(existingSecret));
        }

        if (File.Exists(_databasePath))
        {
            throw new LocalDatabaseKeyUnavailableException(
                "Encrypted local database exists but its Linux Secret Service key is missing. Refusing to create a replacement key.");
        }

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var masterKey = RandomNumberGenerator.GetBytes(MasterKeyLengthBytes);
        try
        {
            var encoded = Convert.ToBase64String(masterKey);
            LinuxSecretServiceNative.StoreUtf8Secret(
                SchemaName,
                PurposeValue,
                _scopeHash,
                $"Xueqing Local Database Key ({_scopeHash[..12]})",
                encoded,
                static msg => new LocalDatabaseKeyUnavailableException(msg));
            return Task.FromResult(masterKey);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(masterKey);
            throw;
        }
    }

    public bool Purge() =>
        LinuxSecretServiceNative.ClearSecret(
            SchemaName,
            PurposeValue,
            _scopeHash,
            static msg => new LocalDatabaseKeyUnavailableException(msg));

    private static byte[] DecodeMasterKey(string secretText)
    {
        if (string.IsNullOrWhiteSpace(secretText))
        {
            throw new LocalDatabaseKeyUnavailableException(
                "Linux Secret Service returned an empty database key.");
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(secretText);
        }
        catch (FormatException ex)
        {
            throw new LocalDatabaseKeyUnavailableException(
                "Linux Secret Service returned a malformed database key.",
                ex);
        }

        if (decoded.Length != MasterKeyLengthBytes)
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new LocalDatabaseKeyUnavailableException(
                $"Linux Secret Service returned a key with invalid length ({decoded.Length} bytes).");
        }

        return decoded;
    }
}
