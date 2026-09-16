using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xueqing.Windows.Core.Sync;

namespace Xueqing.Windows.Infrastructure.Sync;

public sealed class SqliteDurableOutboxStore : IOutboxStore
{
    private const long CurrentSchemaVersion = 1;
    private const int DefaultBusyTimeoutSeconds = 5;

    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private volatile bool _initialized;

    public SqliteDurableOutboxStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
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

            using (var walCommand = connection.CreateCommand())
            {
                walCommand.CommandText = "PRAGMA journal_mode = WAL;";
                var journalMode = Convert.ToString(
                    await walCommand.ExecuteScalarAsync(cancellationToken),
                    CultureInfo.InvariantCulture);

                if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"SQLite refused WAL mode; actual journal mode is '{journalMode ?? "<null>"}'.");
                }
            }

            using var transaction = connection.BeginTransaction(deferred: false);
            var schemaVersion = await ReadSchemaVersionAsync(connection, transaction, cancellationToken);

            switch (schemaVersion)
            {
                case 0:
                    await CreateSchemaV1Async(connection, transaction, cancellationToken);
                    break;
                case CurrentSchemaVersion:
                    break;
                default:
                    throw new UnsupportedLocalSchemaVersionException(schemaVersion);
            }

            transaction.Commit();
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<OutboxEnqueueResult> EnqueueAsync(
        OutboxCommandIntent intent,
        CancellationToken cancellationToken = default)
    {
        ValidateIntent(intent);
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO outbox_operations (
                operation_id,
                command_type,
                aggregate_id,
                scope_key,
                expected_version,
                freshness_token,
                payload_json,
                created_at_client_unix_ms,
                queue_status)
            VALUES (
                $operation_id,
                $command_type,
                $aggregate_id,
                $scope_key,
                $expected_version,
                $freshness_token,
                $payload_json,
                $created_at_client_unix_ms,
                'pending')
            ON CONFLICT(operation_id) DO NOTHING;
            """;
        BindIntent(command, intent);

        var insertedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        var stored = await ReadByOperationIdAsync(
            connection,
            transaction,
            intent.OperationId,
            cancellationToken)
            ?? throw new InvalidOperationException("Outbox insert completed without a readable row.");

        if (insertedRows == 0 && !IsSameDurableIntent(stored.Intent, intent))
        {
            throw new OutboxOperationIdCollisionException(intent.OperationId);
        }

        transaction.Commit();
        return new OutboxEnqueueResult(
            insertedRows == 1
                ? OutboxEnqueueDisposition.Inserted
                : OutboxEnqueueDisposition.AlreadyPresent,
            stored);
    }

    public async Task<OutboxRecord?> GetAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        return await ReadByOperationIdAsync(connection, null, operationId, cancellationToken);
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM outbox_operations;";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    public async Task<OutboxRecord?> TryClaimNextReadyAsync(
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        }

        var leaseExpiresAtUtc = nowUtc.Add(leaseDuration);
        var nowUnixMs = nowUtc.ToUnixTimeMilliseconds();
        var leaseExpiresUnixMs = leaseExpiresAtUtc.ToUnixTimeMilliseconds();

        await InitializeAsync(cancellationToken);
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        Guid? operationId = null;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT operation_id
                FROM outbox_operations
                WHERE queue_status = 'pending'
                   OR (queue_status = 'retry'
                       AND (next_attempt_at_unix_ms IS NULL OR next_attempt_at_unix_ms <= $now))
                   OR (queue_status = 'in_flight'
                       AND lease_expires_at_unix_ms IS NOT NULL
                       AND lease_expires_at_unix_ms <= $now)
                ORDER BY local_sequence
                LIMIT 1;
                """;
            select.Parameters.AddWithValue("$now", nowUnixMs);
            var selected = await select.ExecuteScalarAsync(cancellationToken);
            if (selected is string selectedOperationId)
            {
                operationId = Guid.Parse(selectedOperationId);
            }
        }

        if (operationId is null)
        {
            transaction.Commit();
            return null;
        }

        var leaseId = Guid.NewGuid();
        using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = """
                UPDATE outbox_operations
                SET queue_status = 'in_flight',
                    attempt_count = attempt_count + 1,
                    lease_id = $lease_id,
                    lease_expires_at_unix_ms = $lease_expires_at_unix_ms,
                    next_attempt_at_unix_ms = NULL
                WHERE operation_id = $operation_id;
                """;
            claim.Parameters.AddWithValue("$lease_id", leaseId.ToString("D"));
            claim.Parameters.AddWithValue("$lease_expires_at_unix_ms", leaseExpiresUnixMs);
            claim.Parameters.AddWithValue("$operation_id", operationId.Value.ToString("D"));

            if (await claim.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Outbox claim lost the selected row inside an immediate transaction.");
            }
        }

        var record = await ReadByOperationIdAsync(
            connection,
            transaction,
            operationId.Value,
            cancellationToken)
            ?? throw new InvalidOperationException("Claimed outbox row could not be read back.");

        transaction.Commit();
        return record;
    }

    public async Task<OutboxRecord> ScheduleRetryAsync(
        Guid operationId,
        Guid leaseId,
        string errorClass,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorClass);
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE outbox_operations
            SET queue_status = 'retry',
                last_error_class = $last_error_class,
                next_attempt_at_unix_ms = $next_attempt_at_unix_ms,
                lease_id = NULL,
                lease_expires_at_unix_ms = NULL
            WHERE operation_id = $operation_id
              AND queue_status = 'in_flight'
              AND lease_id = $lease_id;
            """;
        command.Parameters.AddWithValue("$last_error_class", errorClass);
        command.Parameters.AddWithValue("$next_attempt_at_unix_ms", nextAttemptAtUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$operation_id", operationId.ToString("D"));
        command.Parameters.AddWithValue("$lease_id", leaseId.ToString("D"));

        await RequireOwnedLeaseMutationAsync(command, operationId, leaseId, cancellationToken);
        var record = await ReadRequiredAsync(connection, transaction, operationId, cancellationToken);
        transaction.Commit();
        return record;
    }

    public async Task<OutboxRecord> MarkAcknowledgedAsync(
        Guid operationId,
        Guid leaseId,
        DateTimeOffset acknowledgedAtUtc,
        string? serverReceiptJson,
        CancellationToken cancellationToken = default)
    {
        ValidateOptionalJson(serverReceiptJson, nameof(serverReceiptJson));
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE outbox_operations
            SET queue_status = 'acknowledged',
                last_error_class = NULL,
                next_attempt_at_unix_ms = NULL,
                lease_id = NULL,
                lease_expires_at_unix_ms = NULL,
                acknowledged_at_unix_ms = $acknowledged_at_unix_ms,
                server_receipt_json = $server_receipt_json
            WHERE operation_id = $operation_id
              AND queue_status = 'in_flight'
              AND lease_id = $lease_id;
            """;
        command.Parameters.AddWithValue("$acknowledged_at_unix_ms", acknowledgedAtUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$server_receipt_json", (object?)serverReceiptJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$operation_id", operationId.ToString("D"));
        command.Parameters.AddWithValue("$lease_id", leaseId.ToString("D"));

        await RequireOwnedLeaseMutationAsync(command, operationId, leaseId, cancellationToken);
        var record = await ReadRequiredAsync(connection, transaction, operationId, cancellationToken);
        transaction.Commit();
        return record;
    }

    public async Task<OutboxRecord> MoveToDeadLetterAsync(
        Guid operationId,
        Guid leaseId,
        string errorClass,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorClass);
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE outbox_operations
            SET queue_status = 'dead_letter',
                last_error_class = $last_error_class,
                next_attempt_at_unix_ms = NULL,
                lease_id = NULL,
                lease_expires_at_unix_ms = NULL
            WHERE operation_id = $operation_id
              AND queue_status = 'in_flight'
              AND lease_id = $lease_id;
            """;
        command.Parameters.AddWithValue("$last_error_class", errorClass);
        command.Parameters.AddWithValue("$operation_id", operationId.ToString("D"));
        command.Parameters.AddWithValue("$lease_id", leaseId.ToString("D"));

        await RequireOwnedLeaseMutationAsync(command, operationId, leaseId, cancellationToken);
        var record = await ReadRequiredAsync(connection, transaction, operationId, cancellationToken);
        transaction.Commit();
        return record;
    }

    private async Task<SqliteConnection> OpenConfiguredConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString)
        {
            DefaultTimeout = DefaultBusyTimeoutSeconds,
        };

        try
        {
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys = ON;
                PRAGMA synchronous = FULL;
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

    private static async Task<long> ReadSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task CreateSchemaV1Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS outbox_operations (
                local_sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                operation_id TEXT NOT NULL UNIQUE,
                command_type TEXT NOT NULL CHECK(length(command_type) > 0),
                aggregate_id TEXT NULL,
                scope_key TEXT NOT NULL CHECK(length(scope_key) > 0),
                expected_version INTEGER NULL CHECK(expected_version IS NULL OR expected_version >= 0),
                freshness_token TEXT NULL,
                payload_json TEXT NOT NULL CHECK(length(payload_json) > 0),
                created_at_client_unix_ms INTEGER NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0 CHECK(attempt_count >= 0),
                queue_status TEXT NOT NULL DEFAULT 'pending'
                    CHECK(queue_status IN ('pending', 'retry', 'in_flight', 'acknowledged', 'dead_letter')),
                last_error_class TEXT NULL,
                next_attempt_at_unix_ms INTEGER NULL,
                lease_id TEXT NULL,
                lease_expires_at_unix_ms INTEGER NULL,
                acknowledged_at_unix_ms INTEGER NULL,
                server_receipt_json TEXT NULL
            ) STRICT;

            CREATE INDEX IF NOT EXISTS ix_outbox_ready
                ON outbox_operations(queue_status, next_attempt_at_unix_ms, lease_expires_at_unix_ms, local_sequence);

            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void BindIntent(SqliteCommand command, OutboxCommandIntent intent)
    {
        command.Parameters.AddWithValue("$operation_id", intent.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$command_type", intent.CommandType);
        command.Parameters.AddWithValue("$aggregate_id", (object?)intent.AggregateId ?? DBNull.Value);
        command.Parameters.AddWithValue("$scope_key", intent.ScopeKey);
        command.Parameters.AddWithValue("$expected_version", (object?)intent.ExpectedVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$freshness_token", (object?)intent.FreshnessToken ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload_json", intent.PayloadJson);
        command.Parameters.AddWithValue("$created_at_client_unix_ms", intent.CreatedAtClientUtc.ToUnixTimeMilliseconds());
    }

    private static async Task RequireOwnedLeaseMutationAsync(
        SqliteCommand command,
        Guid operationId,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new OutboxLeaseLostException(operationId, leaseId);
        }
    }

    private static async Task<OutboxRecord> ReadRequiredAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
        => await ReadByOperationIdAsync(connection, transaction, operationId, cancellationToken)
            ?? throw new InvalidOperationException($"Outbox operation '{operationId:D}' disappeared during a transaction.");

    private static async Task<OutboxRecord?> ReadByOperationIdAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                local_sequence,
                operation_id,
                command_type,
                aggregate_id,
                scope_key,
                expected_version,
                freshness_token,
                payload_json,
                created_at_client_unix_ms,
                attempt_count,
                queue_status,
                last_error_class,
                next_attempt_at_unix_ms,
                lease_id,
                lease_expires_at_unix_ms,
                acknowledged_at_unix_ms,
                server_receipt_json
            FROM outbox_operations
            WHERE operation_id = $operation_id;
            """;
        command.Parameters.AddWithValue("$operation_id", operationId.ToString("D"));

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapRecord(reader);
    }

    private static OutboxRecord MapRecord(SqliteDataReader reader)
    {
        var intent = new OutboxCommandIntent(
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(8)));

        return new OutboxRecord(
            reader.GetInt64(0),
            intent,
            reader.GetInt32(9),
            ParseStatus(reader.GetString(10)),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            ReadTimestamp(reader, 12),
            reader.IsDBNull(13) ? null : Guid.Parse(reader.GetString(13)),
            ReadTimestamp(reader, 14),
            ReadTimestamp(reader, 15),
            reader.IsDBNull(16) ? null : reader.GetString(16));
    }

    private static DateTimeOffset? ReadTimestamp(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(ordinal));

    private static OutboxQueueStatus ParseStatus(string value)
        => value switch
        {
            "pending" => OutboxQueueStatus.Pending,
            "retry" => OutboxQueueStatus.Retry,
            "in_flight" => OutboxQueueStatus.InFlight,
            "acknowledged" => OutboxQueueStatus.Acknowledged,
            "dead_letter" => OutboxQueueStatus.DeadLetter,
            _ => throw new InvalidOperationException($"Unknown outbox queue status '{value}'."),
        };

    private static bool IsSameDurableIntent(OutboxCommandIntent stored, OutboxCommandIntent candidate)
        => stored.OperationId == candidate.OperationId
            && string.Equals(stored.CommandType, candidate.CommandType, StringComparison.Ordinal)
            && string.Equals(stored.AggregateId, candidate.AggregateId, StringComparison.Ordinal)
            && string.Equals(stored.ScopeKey, candidate.ScopeKey, StringComparison.Ordinal)
            && stored.ExpectedVersion == candidate.ExpectedVersion
            && string.Equals(stored.FreshnessToken, candidate.FreshnessToken, StringComparison.Ordinal)
            && string.Equals(stored.PayloadJson, candidate.PayloadJson, StringComparison.Ordinal);

    private static void ValidateIntent(OutboxCommandIntent intent)
    {
        if (intent.OperationId == Guid.Empty)
        {
            throw new ArgumentException("Operation id must be a non-empty UUID.", nameof(intent));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(intent.CommandType);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.ScopeKey);

        if (intent.AggregateId is not null && string.IsNullOrWhiteSpace(intent.AggregateId))
        {
            throw new ArgumentException("Aggregate id cannot be blank when supplied.", nameof(intent));
        }

        if (intent.ExpectedVersion is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intent), "Expected version cannot be negative.");
        }

        if (intent.FreshnessToken is not null && string.IsNullOrWhiteSpace(intent.FreshnessToken))
        {
            throw new ArgumentException("Freshness token cannot be blank when supplied.", nameof(intent));
        }

        ValidateRequiredJson(intent.PayloadJson, nameof(intent.PayloadJson));
    }

    private static void ValidateRequiredJson(string json, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json, parameterName);
        using var _ = JsonDocument.Parse(json);
    }

    private static void ValidateOptionalJson(string? json, string parameterName)
    {
        if (json is null)
        {
            return;
        }

        ValidateRequiredJson(json, parameterName);
    }
}
