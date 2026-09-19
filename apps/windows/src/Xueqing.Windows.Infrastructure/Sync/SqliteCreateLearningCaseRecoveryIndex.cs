using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Windows.Infrastructure.Sync;

public sealed class SqliteCreateLearningCaseRecoveryIndex
{
    private const long CurrentSchemaVersion = 1;
    private const int DefaultBusyTimeoutSeconds = 5;

    private readonly EncryptedSqliteConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private volatile bool _initialized;

    [SupportedOSPlatform("windows")]
    public SqliteCreateLearningCaseRecoveryIndex(string databasePath)
    {
        var fullPath = PrepareDatabasePath(databasePath);
        _connectionFactory = new EncryptedSqliteConnectionFactory(
            fullPath,
            DefaultBusyTimeoutSeconds);
    }

    internal SqliteCreateLearningCaseRecoveryIndex(
        string databasePath,
        ReadOnlySpan<byte> testMasterKey)
    {
        var fullPath = PrepareDatabasePath(databasePath);
        _connectionFactory = new EncryptedSqliteConnectionFactory(
            fullPath,
            testMasterKey,
            DefaultBusyTimeoutSeconds);
    }

    public async Task RegisterOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        RequireGuid(organizationId, nameof(organizationId));
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO recovery_organizations (
                organization_id,
                first_seen_unix_ms
            )
            VALUES (
                $organization_id,
                $first_seen_unix_ms
            )
            ON CONFLICT(organization_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$organization_id", organizationId.ToString("D"));
        command.Parameters.AddWithValue(
            "$first_seen_unix_ms",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }

    public async Task<IReadOnlyList<Guid>> ListOrganizationsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT organization_id
            FROM recovery_organizations
            ORDER BY first_seen_unix_ms, organization_id;
            """;

        var organizations = new List<Guid>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Guid.TryParse(reader.GetString(0), out var organizationId) ||
                organizationId == Guid.Empty)
            {
                throw new InvalidDataException(
                    "Recovery organization index contains an invalid organization id.");
            }

            organizations.Add(organizationId);
        }

        return organizations;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
            using var transaction = connection.BeginTransaction(deferred: false);
            using var versionCommand = connection.CreateCommand();
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt64(
                await versionCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);

            if (version == 0)
            {
                using var schemaCommand = connection.CreateCommand();
                schemaCommand.Transaction = transaction;
                schemaCommand.CommandText = """
                    CREATE TABLE recovery_organizations (
                        organization_id TEXT PRIMARY KEY NOT NULL,
                        first_seen_unix_ms INTEGER NOT NULL
                    ) STRICT;

                    PRAGMA user_version = 1;
                    """;
                await schemaCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (version != CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported CreateLearningCase recovery-index schema version: {version}.");
            }

            transaction.Commit();
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConfiguredConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.OpenAsync(cancellationToken);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys = ON;
                PRAGMA synchronous = FULL;
                PRAGMA journal_mode = WAL;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("UUID must be non-empty.", parameterName);
        }
    }

    private static string PrepareDatabasePath(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }
}
