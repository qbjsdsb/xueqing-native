using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Xueqing.Windows.Infrastructure.LocalData;

internal sealed class EncryptedSqliteConnectionFactory
{
    private static readonly Lazy<bool> RuntimeInitialization = new(
        static () =>
        {
            SQLitePCL.Batteries_V2.Init();
            return true;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly string _databasePath;
    private readonly Func<CancellationToken, Task<byte[]>> _loadKeyAsync;

    [SupportedOSPlatform("windows")]
    public EncryptedSqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);

        var keyStore = new WindowsDpapiDatabaseKeyStore(_databasePath);
        _loadKeyAsync = keyStore.LoadOrCreateAsync;
    }

    internal EncryptedSqliteConnectionFactory(string databasePath, ReadOnlySpan<byte> testMasterKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (testMasterKey.Length != 32)
        {
            throw new ArgumentException("SQLite3MC test master key must be exactly 32 bytes.", nameof(testMasterKey));
        }

        _databasePath = Path.GetFullPath(databasePath);
        var retainedTestKey = testMasterKey.ToArray();
        _loadKeyAsync = _ => Task.FromResult(retainedTestKey.ToArray());
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        _ = RuntimeInitialization.Value;

        var masterKey = await _loadKeyAsync(cancellationToken);
        if (masterKey.Length != 32)
        {
            CryptographicOperations.ZeroMemory(masterKey);
            throw new CryptographicException("Local database master key must be exactly 256 bits.");
        }

        string password;
        try
        {
            password = Convert.ToBase64String(masterKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            Password = password,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
