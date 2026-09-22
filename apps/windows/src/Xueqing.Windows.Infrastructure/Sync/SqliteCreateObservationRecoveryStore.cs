using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Windows.Infrastructure.Sync;

public sealed class SqliteCreateObservationRecoveryStore
{
    private const long CurrentSchemaVersion = 1;
    private const int DefaultBusyTimeoutSeconds = 5;

    private readonly EncryptedSqliteConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private volatile bool _initialized;

    [SupportedOSPlatform("windows")]
    public SqliteCreateObservationRecoveryStore(string databasePath)
    {
        _connectionFactory = new EncryptedSqliteConnectionFactory(
            PrepareDatabasePath(databasePath),
            DefaultBusyTimeoutSeconds);
    }

    internal SqliteCreateObservationRecoveryStore(
        string databasePath,
        ReadOnlySpan<byte> testMasterKey)
    {
        _connectionFactory = new EncryptedSqliteConnectionFactory(
            PrepareDatabasePath(databasePath),
            testMasterKey,
            DefaultBusyTimeoutSeconds);
    }

    public async Task SaveAsync(
        CreateObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        using (var cleanup = connection.CreateCommand())
        {
            cleanup.Transaction = transaction;
            cleanup.CommandText = """
                DELETE FROM pending_create_observation
                WHERE organization_id = $organization_id
                  AND student_id = $student_id
                  AND subject_profile_id = $subject_profile_id
                  AND assignment_id = $assignment_id
                  AND disposition = 'rejected_cleanup';
                """;
            AddContextParameters(cleanup, request);
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO pending_create_observation (
                operation_id,
                organization_id,
                student_id,
                subject_profile_id,
                assignment_id,
                raw_text,
                client_captured_at,
                metadata_json,
                saved_at_unix_ms
            )
            VALUES (
                $operation_id,
                $organization_id,
                $student_id,
                $subject_profile_id,
                $assignment_id,
                $raw_text,
                $client_captured_at,
                $metadata_json,
                $saved_at_unix_ms
            )
            ON CONFLICT(operation_id) DO NOTHING;
            """;
        insert.Parameters.AddWithValue("$operation_id", request.OperationId.ToString("D"));
        AddContextParameters(insert, request);
        insert.Parameters.AddWithValue("$raw_text", request.RawText);
        insert.Parameters.AddWithValue(
            "$client_captured_at",
            request.ClientCapturedAt?.ToString("O", CultureInfo.InvariantCulture)
                ?? (object)DBNull.Value);
        insert.Parameters.AddWithValue(
            "$metadata_json",
            SerializeMetadata(request.ClientCaptureMetadata));
        insert.Parameters.AddWithValue(
            "$saved_at_unix_ms",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        int inserted;
        try
        {
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "A different unresolved CreateObservation intent already owns this teaching context.",
                exception);
        }

        var stored = await ReadByOperationIdAsync(
            connection,
            transaction,
            request.OperationId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "CreateObservation recovery insert completed without a readable row.");

        if (inserted == 0 && !SemanticallyEquals(stored, request))
        {
            throw new InvalidOperationException(
                $"Operation id '{request.OperationId:D}' is already bound to a different CreateObservation intent.");
        }

        transaction.Commit();
    }

    public async Task<CreateObservationRequest?> FindPendingAsync(
        ObservationDraftScope scope,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                operation_id,
                organization_id,
                student_id,
                subject_profile_id,
                assignment_id,
                raw_text,
                client_captured_at,
                metadata_json
            FROM pending_create_observation
            WHERE organization_id = $organization_id
              AND student_id = $student_id
              AND subject_profile_id = $subject_profile_id
              AND assignment_id = $assignment_id
              AND disposition = 'pending'
            LIMIT 1;
            """;
        AddScopeParameters(command, scope);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? Deserialize(reader)
            : null;
    }

    public async Task MarkRejectedAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        RequireGuid(operationId, nameof(operationId));
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE pending_create_observation
            SET disposition = 'rejected_cleanup'
            WHERE operation_id = $operation_id
              AND disposition = 'pending';
            """;
        command.Parameters.AddWithValue("$operation_id", operationId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }

    public async Task RemoveAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        RequireGuid(operationId, nameof(operationId));
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM pending_create_observation
            WHERE operation_id = $operation_id;
            """;
        command.Parameters.AddWithValue("$operation_id", operationId.ToString("D"));
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
                using var schema = connection.CreateCommand();
                schema.Transaction = transaction;
                schema.CommandText = """
                    CREATE TABLE pending_create_observation (
                        operation_id TEXT PRIMARY KEY NOT NULL,
                        organization_id TEXT NOT NULL,
                        student_id TEXT NOT NULL,
                        subject_profile_id TEXT NOT NULL,
                        assignment_id TEXT NOT NULL,
                        raw_text TEXT NOT NULL,
                        client_captured_at TEXT NULL,
                        metadata_json TEXT NOT NULL,
                        saved_at_unix_ms INTEGER NOT NULL,
                        disposition TEXT NOT NULL DEFAULT 'pending'
                            CHECK(disposition IN ('pending', 'rejected_cleanup'))
                    ) STRICT;

                    CREATE UNIQUE INDEX ux_pending_create_observation_context
                        ON pending_create_observation (
                            organization_id,
                            student_id,
                            subject_profile_id,
                            assignment_id
                        )
                        WHERE disposition = 'pending';

                    PRAGMA user_version = 1;
                    """;
                await schema.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (current != CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported CreateObservation recovery schema version: {current}.");
            }

            transaction.Commit();
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static async Task<CreateObservationRequest?> ReadByOperationIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                operation_id,
                organization_id,
                student_id,
                subject_profile_id,
                assignment_id,
                raw_text,
                client_captured_at,
                metadata_json
            FROM pending_create_observation
            WHERE operation_id = $operation_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation_id", operationId.ToString("D"));

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? Deserialize(reader)
            : null;
    }

    private static CreateObservationRequest Deserialize(SqliteDataReader reader)
    {
        var capturedText = reader.IsDBNull(6) ? null : reader.GetString(6);
        DateTimeOffset? capturedAt = capturedText is null
            ? null
            : DateTimeOffset.Parse(
                capturedText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);

        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(
            reader.GetString(7))
            ?? throw new InvalidDataException(
                "Stored CreateObservation metadata deserialized to null.");

        return new CreateObservationRequest(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            Guid.Parse(reader.GetString(3)),
            Guid.Parse(reader.GetString(4)),
            reader.GetString(5),
            capturedAt,
            metadata);
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

    private static bool SemanticallyEquals(
        CreateObservationRequest left,
        CreateObservationRequest right) =>
        left.OperationId == right.OperationId &&
        left.OrganizationId == right.OrganizationId &&
        left.StudentId == right.StudentId &&
        left.SubjectProfileId == right.SubjectProfileId &&
        left.AssignmentId == right.AssignmentId &&
        string.Equals(left.RawText, right.RawText, StringComparison.Ordinal) &&
        left.ClientCapturedAt == right.ClientCapturedAt &&
        MetadataEquals(left.ClientCaptureMetadata, right.ClientCaptureMetadata);

    private static bool MetadataEquals(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        var leftMap = left ?? EmptyMetadata;
        var rightMap = right ?? EmptyMetadata;
        if (leftMap.Count != rightMap.Count)
        {
            return false;
        }

        foreach (var pair in leftMap)
        {
            if (!rightMap.TryGetValue(pair.Key, out var value) ||
                !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string SerializeMetadata(
        IReadOnlyDictionary<string, string>? metadata) =>
        JsonSerializer.Serialize(
            (metadata ?? EmptyMetadata)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal));

    private static void AddContextParameters(
        SqliteCommand command,
        CreateObservationRequest request)
    {
        command.Parameters.AddWithValue("$organization_id", request.OrganizationId.ToString("D"));
        command.Parameters.AddWithValue("$student_id", request.StudentId.ToString("D"));
        command.Parameters.AddWithValue("$subject_profile_id", request.SubjectProfileId.ToString("D"));
        command.Parameters.AddWithValue("$assignment_id", request.AssignmentId.ToString("D"));
    }

    private static void AddScopeParameters(
        SqliteCommand command,
        ObservationDraftScope scope)
    {
        command.Parameters.AddWithValue("$organization_id", scope.OrganizationId.ToString("D"));
        command.Parameters.AddWithValue("$student_id", scope.StudentId.ToString("D"));
        command.Parameters.AddWithValue("$subject_profile_id", scope.SubjectProfileId.ToString("D"));
        command.Parameters.AddWithValue("$assignment_id", scope.AssignmentId.ToString("D"));
    }

    private static void ValidateRequest(CreateObservationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireGuid(request.OperationId, nameof(request.OperationId));
        ValidateScope(new ObservationDraftScope(
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.AssignmentId));

        var length = request.RawText?.Trim().Length ?? 0;
        if (length is < 1 or > 10_000)
        {
            throw new ArgumentException(
                "CreateObservation recovery requires 1-10000 characters of formal text.",
                nameof(request));
        }
    }

    private static void ValidateScope(ObservationDraftScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        RequireGuid(scope.OrganizationId, nameof(scope.OrganizationId));
        RequireGuid(scope.StudentId, nameof(scope.StudentId));
        RequireGuid(scope.SubjectProfileId, nameof(scope.SubjectProfileId));
        RequireGuid(scope.AssignmentId, nameof(scope.AssignmentId));
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

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>();
}
