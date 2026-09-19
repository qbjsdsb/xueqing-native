using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Windows.Infrastructure.Sync;

public sealed class SqliteCreateLearningCaseRecoveryStore
{
    private const long CurrentSchemaVersion = 1;
    private const int DefaultBusyTimeoutSeconds = 5;

    private readonly EncryptedSqliteConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private volatile bool _initialized;

    [SupportedOSPlatform("windows")]
    public SqliteCreateLearningCaseRecoveryStore(string databasePath)
    {
        var fullPath = PrepareDatabasePath(databasePath);
        _connectionFactory = new EncryptedSqliteConnectionFactory(
            fullPath,
            DefaultBusyTimeoutSeconds);
    }

    internal SqliteCreateLearningCaseRecoveryStore(
        string databasePath,
        ReadOnlySpan<byte> testMasterKey)
    {
        var fullPath = PrepareDatabasePath(databasePath);
        _connectionFactory = new EncryptedSqliteConnectionFactory(
            fullPath,
            testMasterKey,
            DefaultBusyTimeoutSeconds);
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
            using var transaction = connection.BeginTransaction(deferred: false);

            using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction = transaction;
                versionCommand.CommandText = "PRAGMA user_version;";
                var version = Convert.ToInt64(
                    await versionCommand.ExecuteScalarAsync(cancellationToken),
                    CultureInfo.InvariantCulture);

                if (version == 0)
                {
                    await CreateSchemaAsync(connection, transaction, cancellationToken);
                }
                else if (version != CurrentSchemaVersion)
                {
                    throw new InvalidOperationException(
                        $"Unsupported CreateLearningCase recovery schema version: {version}.");
                }
            }

            transaction.Commit();
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task SaveAsync(
        CreateLearningCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        await InitializeAsync(cancellationToken);

        var payloadJson = JsonSerializer.Serialize(request);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO pending_create_learning_case (
                operation_id,
                organization_id,
                student_id,
                subject_profile_id,
                source_observation_id,
                payload_json,
                saved_at_unix_ms
            )
            VALUES (
                $operation_id,
                $organization_id,
                $student_id,
                $subject_profile_id,
                $source_observation_id,
                $payload_json,
                $saved_at_unix_ms
            )
            ON CONFLICT(operation_id) DO NOTHING;
            """;
        insert.Parameters.AddWithValue("$operation_id", request.OperationId.ToString("D"));
        insert.Parameters.AddWithValue("$organization_id", request.OrganizationId.ToString("D"));
        insert.Parameters.AddWithValue("$student_id", request.StudentId.ToString("D"));
        insert.Parameters.AddWithValue("$subject_profile_id", request.SubjectProfileId.ToString("D"));
        insert.Parameters.AddWithValue("$source_observation_id", request.SourceObservationId!.Value.ToString("D"));
        insert.Parameters.AddWithValue("$payload_json", payloadJson);
        insert.Parameters.AddWithValue("$saved_at_unix_ms", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        int inserted;
        try
        {
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "A different unresolved CreateLearningCase intent already owns this source Observation.",
                exception);
        }

        var stored = await ReadByOperationIdAsync(
            connection,
            transaction,
            request.OperationId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "CreateLearningCase recovery insert completed without a readable row.");

        if (inserted == 0 && stored != request)
        {
            throw new InvalidOperationException(
                $"Operation id '{request.OperationId:D}' is already bound to a different CreateLearningCase intent.");
        }

        transaction.Commit();
    }

    public async Task<CreateLearningCaseRequest?> FindBySourceObservationAsync(
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid sourceObservationId,
        CancellationToken cancellationToken = default)
    {
        RequireGuid(organizationId, nameof(organizationId));
        RequireGuid(studentId, nameof(studentId));
        RequireGuid(subjectProfileId, nameof(subjectProfileId));
        RequireGuid(sourceObservationId, nameof(sourceObservationId));
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json
            FROM pending_create_learning_case
            WHERE organization_id = $organization_id
              AND student_id = $student_id
              AND subject_profile_id = $subject_profile_id
              AND source_observation_id = $source_observation_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$organization_id", organizationId.ToString("D"));
        command.Parameters.AddWithValue("$student_id", studentId.ToString("D"));
        command.Parameters.AddWithValue("$subject_profile_id", subjectProfileId.ToString("D"));
        command.Parameters.AddWithValue("$source_observation_id", sourceObservationId.ToString("D"));

        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null
            ? null
            : Deserialize(payload);
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
            DELETE FROM pending_create_learning_case
            WHERE operation_id = $operation_id;
            """;
        command.Parameters.AddWithValue("$operation_id", operationId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
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

    private static async Task CreateSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE pending_create_learning_case (
                operation_id TEXT PRIMARY KEY NOT NULL,
                organization_id TEXT NOT NULL,
                student_id TEXT NOT NULL,
                subject_profile_id TEXT NOT NULL,
                source_observation_id TEXT NOT NULL,
                payload_json TEXT NOT NULL CHECK(length(payload_json) > 0),
                saved_at_unix_ms INTEGER NOT NULL
            ) STRICT;

            CREATE UNIQUE INDEX ux_pending_create_learning_case_source
                ON pending_create_learning_case (
                    organization_id,
                    student_id,
                    subject_profile_id,
                    source_observation_id
                );

            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CreateLearningCaseRequest?> ReadByOperationIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT payload_json
            FROM pending_create_learning_case
            WHERE operation_id = $operation_id;
            """;
        command.Parameters.AddWithValue("$operation_id", operationId.ToString("D"));
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null
            ? null
            : Deserialize(payload);
    }

    private static CreateLearningCaseRequest Deserialize(string payloadJson) =>
        JsonSerializer.Deserialize<CreateLearningCaseRequest>(payloadJson)
        ?? throw new InvalidDataException(
            "Stored CreateLearningCase recovery payload deserialized to null.");

    private static void ValidateRequest(CreateLearningCaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireGuid(request.OperationId, nameof(request.OperationId));
        RequireGuid(request.OrganizationId, nameof(request.OrganizationId));
        RequireGuid(request.StudentId, nameof(request.StudentId));
        RequireGuid(request.SubjectProfileId, nameof(request.SubjectProfileId));
        RequireGuid(request.OwnerAssignmentId, nameof(request.OwnerAssignmentId));

        if (request.SourceObservationId is not Guid sourceObservationId ||
            sourceObservationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Observation-to-Case recovery requires a non-empty source Observation.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Title) ||
            string.IsNullOrWhiteSpace(request.PrimaryActionText))
        {
            throw new ArgumentException(
                "CreateLearningCase recovery cannot persist a blank formal intent.",
                nameof(request));
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
