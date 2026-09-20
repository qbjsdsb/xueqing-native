using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Windows.Infrastructure.Sync;

public sealed class SqliteOrganizationInvitationRecoveryStore
{
    private const long CurrentSchemaVersion = 1;
    private const int DefaultBusyTimeoutSeconds = 5;

    private readonly EncryptedSqliteConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private volatile bool _initialized;

    [SupportedOSPlatform("windows")]
    public SqliteOrganizationInvitationRecoveryStore(string databasePath)
    {
        _connectionFactory = new EncryptedSqliteConnectionFactory(
            PrepareDatabasePath(databasePath),
            DefaultBusyTimeoutSeconds);
    }

    internal SqliteOrganizationInvitationRecoveryStore(
        string databasePath,
        ReadOnlySpan<byte> testMasterKey)
    {
        _connectionFactory = new EncryptedSqliteConnectionFactory(
            PrepareDatabasePath(databasePath),
            testMasterKey,
            DefaultBusyTimeoutSeconds);
    }

    public async Task SaveAsync(
        OrganizationInvitationRecoveryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        intent.Validate();
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        var existing = await ReadByOperationIdAsync(
            connection,
            transaction,
            intent.CreateOperationId,
            cancellationToken);

        if (existing is not null)
        {
            ValidateTransition(existing, intent);
        }

        var payload = JsonSerializer.Serialize(intent);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO pending_organization_invitation (
                create_operation_id,
                organization_id,
                payload_json,
                updated_at_unix_ms
            )
            VALUES (
                $create_operation_id,
                $organization_id,
                $payload_json,
                $updated_at_unix_ms
            )
            ON CONFLICT(create_operation_id) DO UPDATE SET
                organization_id = excluded.organization_id,
                payload_json = excluded.payload_json,
                updated_at_unix_ms = excluded.updated_at_unix_ms;
            """;
        command.Parameters.AddWithValue(
            "$create_operation_id",
            intent.CreateOperationId.ToString("D"));
        command.Parameters.AddWithValue(
            "$organization_id",
            intent.OrganizationId.ToString("D"));
        command.Parameters.AddWithValue("$payload_json", payload);
        command.Parameters.AddWithValue(
            "$updated_at_unix_ms",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync(cancellationToken);

        transaction.Commit();
    }

    public async Task<IReadOnlyList<OrganizationInvitationRecoveryIntent>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        RequireGuid(organizationId, nameof(organizationId));
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json
            FROM pending_organization_invitation
            WHERE organization_id = $organization_id
            ORDER BY updated_at_unix_ms, create_operation_id;
            """;
        command.Parameters.AddWithValue(
            "$organization_id",
            organizationId.ToString("D"));

        var result = new List<OrganizationInvitationRecoveryIntent>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(Deserialize(reader.GetString(0)));
        }

        return result;
    }

    public async Task RemoveAsync(
        Guid createOperationId,
        CancellationToken cancellationToken = default)
    {
        RequireGuid(createOperationId, nameof(createOperationId));
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM pending_organization_invitation
            WHERE create_operation_id = $create_operation_id;
            """;
        command.Parameters.AddWithValue(
            "$create_operation_id",
            createOperationId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
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
            using var version = connection.CreateCommand();
            version.Transaction = transaction;
            version.CommandText = "PRAGMA user_version;";
            var current = Convert.ToInt64(
                await version.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);

            if (current == 0)
            {
                using var create = connection.CreateCommand();
                create.Transaction = transaction;
                create.CommandText = """
                    CREATE TABLE pending_organization_invitation (
                        create_operation_id TEXT PRIMARY KEY NOT NULL,
                        organization_id TEXT NOT NULL,
                        payload_json TEXT NOT NULL CHECK(length(payload_json) > 0),
                        updated_at_unix_ms INTEGER NOT NULL
                    ) STRICT;

                    CREATE INDEX ix_pending_organization_invitation_org
                        ON pending_organization_invitation (
                            organization_id,
                            updated_at_unix_ms,
                            create_operation_id
                        );

                    PRAGMA user_version = 1;
                    """;
                await create.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (current != CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported Organization invitation recovery schema version: {current}.");
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

    private static async Task<OrganizationInvitationRecoveryIntent?> ReadByOperationIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid createOperationId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT payload_json
            FROM pending_organization_invitation
            WHERE create_operation_id = $create_operation_id;
            """;
        command.Parameters.AddWithValue(
            "$create_operation_id",
            createOperationId.ToString("D"));

        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null ? null : Deserialize(payload);
    }

    private static void ValidateTransition(
        OrganizationInvitationRecoveryIntent existing,
        OrganizationInvitationRecoveryIntent next)
    {
        existing.Validate();
        next.Validate();

        if (existing.CreateOperationId != next.CreateOperationId ||
            existing.DeliveryOperationId != next.DeliveryOperationId ||
            existing.OrganizationId != next.OrganizationId ||
            !string.Equals(
                existing.InvitedEmail,
                next.InvitedEmail,
                StringComparison.Ordinal) ||
            existing.TargetRole != next.TargetRole ||
            existing.TargetCanTeach != next.TargetCanTeach)
        {
            throw new InvalidOperationException(
                "Invitation recovery operation is already bound to a different intent.");
        }

        if (existing.InvitationId is Guid existingInvitationId &&
            next.InvitationId != existingInvitationId)
        {
            throw new InvalidOperationException(
                "Invitation recovery cannot be rebound to another invitation.");
        }

        if ((int)next.Stage < (int)existing.Stage)
        {
            throw new InvalidOperationException(
                "Invitation recovery stage cannot move backward.");
        }

        if (existing.Stage == OrganizationInvitationRecoveryStage.DeliveryFailed &&
            next != existing)
        {
            throw new InvalidOperationException(
                "Terminal invitation delivery recovery cannot be mutated.");
        }
    }

    private static OrganizationInvitationRecoveryIntent Deserialize(string payload)
    {
        var intent = JsonSerializer.Deserialize<OrganizationInvitationRecoveryIntent>(payload)
            ?? throw new InvalidDataException(
                "Stored Organization invitation recovery deserialized to null.");
        intent.Validate();
        return intent;
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
