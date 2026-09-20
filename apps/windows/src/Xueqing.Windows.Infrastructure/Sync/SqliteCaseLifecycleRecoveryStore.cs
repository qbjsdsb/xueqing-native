using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Windows.Infrastructure.Sync;

public sealed class SqliteCaseLifecycleRecoveryStore
{
    private const long CurrentSchemaVersion = 1;
    private const int DefaultBusyTimeoutSeconds = 5;
    private const string TransitionKind = "transition_case_state";
    private const string CloseKind = "close_learning_case";
    private const string ReopenKind = "reopen_learning_case";

    private readonly EncryptedSqliteConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private volatile bool _initialized;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public SqliteCaseLifecycleRecoveryStore(string databasePath)
    {
        var fullPath = PrepareDatabasePath(databasePath);
        _connectionFactory = new EncryptedSqliteConnectionFactory(
            fullPath,
            DefaultBusyTimeoutSeconds);
    }

    internal SqliteCaseLifecycleRecoveryStore(
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
                    CREATE TABLE pending_case_lifecycle (
                        operation_id TEXT PRIMARY KEY NOT NULL,
                        organization_id TEXT NOT NULL,
                        case_id TEXT NOT NULL,
                        intent_kind TEXT NOT NULL
                            CHECK(intent_kind IN (
                                'transition_case_state',
                                'close_learning_case',
                                'reopen_learning_case'
                            )),
                        payload_json TEXT NOT NULL CHECK(length(payload_json) > 0),
                        saved_at_unix_ms INTEGER NOT NULL,
                        disposition TEXT NOT NULL DEFAULT 'pending'
                            CHECK(disposition IN ('pending', 'rejected_cleanup'))
                    ) STRICT;

                    CREATE UNIQUE INDEX ux_pending_case_lifecycle_case
                        ON pending_case_lifecycle (organization_id, case_id)
                        WHERE disposition = 'pending';

                    PRAGMA user_version = 1;
                    """;
                await schemaCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (version != CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported Case lifecycle recovery schema version: {version}.");
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
        CaseLifecycleRecoveryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ValidateIntent(intent);
        await InitializeAsync(cancellationToken);

        var (kind, payloadJson) = Serialize(intent);
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        using (var cleanupRejected = connection.CreateCommand())
        {
            cleanupRejected.Transaction = transaction;
            cleanupRejected.CommandText = """
                DELETE FROM pending_case_lifecycle
                WHERE organization_id = $organization_id
                  AND case_id = $case_id
                  AND disposition = 'rejected_cleanup';
                """;
            cleanupRejected.Parameters.AddWithValue(
                "$organization_id",
                intent.OrganizationId.ToString("D"));
            cleanupRejected.Parameters.AddWithValue(
                "$case_id",
                intent.CaseId.ToString("D"));
            await cleanupRejected.ExecuteNonQueryAsync(cancellationToken);
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO pending_case_lifecycle (
                operation_id,
                organization_id,
                case_id,
                intent_kind,
                payload_json,
                saved_at_unix_ms
            )
            VALUES (
                $operation_id,
                $organization_id,
                $case_id,
                $intent_kind,
                $payload_json,
                $saved_at_unix_ms
            )
            ON CONFLICT(operation_id) DO NOTHING;
            """;
        insert.Parameters.AddWithValue("$operation_id", intent.OperationId.ToString("D"));
        insert.Parameters.AddWithValue("$organization_id", intent.OrganizationId.ToString("D"));
        insert.Parameters.AddWithValue("$case_id", intent.CaseId.ToString("D"));
        insert.Parameters.AddWithValue("$intent_kind", kind);
        insert.Parameters.AddWithValue("$payload_json", payloadJson);
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
                "A different unresolved Case lifecycle intent already owns this Case.",
                exception);
        }

        var stored = await ReadByOperationIdAsync(
            connection,
            transaction,
            intent.OperationId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Case lifecycle recovery insert completed without a readable row.");

        if (inserted == 0 && stored != intent)
        {
            throw new InvalidOperationException(
                $"Operation id '{intent.OperationId:D}' is already bound to a different Case lifecycle intent.");
        }

        transaction.Commit();
    }

    public async Task<CaseLifecycleRecoveryIntent?> FindByCaseAsync(
        Guid organizationId,
        Guid caseId,
        CancellationToken cancellationToken = default)
    {
        RequireGuid(organizationId, nameof(organizationId));
        RequireGuid(caseId, nameof(caseId));
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT intent_kind, payload_json
            FROM pending_case_lifecycle
            WHERE organization_id = $organization_id
              AND case_id = $case_id
              AND disposition = 'pending'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$organization_id", organizationId.ToString("D"));
        command.Parameters.AddWithValue("$case_id", caseId.ToString("D"));

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return Deserialize(reader.GetString(0), reader.GetString(1));
    }

    public async Task<IReadOnlyList<CaseLifecycleRecoveryIntent>> ListPendingAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        RequireGuid(organizationId, nameof(organizationId));
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT intent_kind, payload_json
            FROM pending_case_lifecycle
            WHERE organization_id = $organization_id
              AND disposition = 'pending'
            ORDER BY saved_at_unix_ms, operation_id;
            """;
        command.Parameters.AddWithValue("$organization_id", organizationId.ToString("D"));

        var pending = new List<CaseLifecycleRecoveryIntent>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pending.Add(Deserialize(reader.GetString(0), reader.GetString(1)));
        }

        return pending;
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
            UPDATE pending_case_lifecycle
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
            DELETE FROM pending_case_lifecycle
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

    private static async Task<CaseLifecycleRecoveryIntent?> ReadByOperationIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT intent_kind, payload_json
            FROM pending_case_lifecycle
            WHERE operation_id = $operation_id;
            """;
        command.Parameters.AddWithValue("$operation_id", operationId.ToString("D"));

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return Deserialize(reader.GetString(0), reader.GetString(1));
    }

    private static (string Kind, string PayloadJson) Serialize(
        CaseLifecycleRecoveryIntent intent) =>
        intent switch
        {
            TransitionCaseRecoveryIntent transition =>
                (TransitionKind, JsonSerializer.Serialize(transition.Request)),
            CloseCaseRecoveryIntent close =>
                (CloseKind, JsonSerializer.Serialize(close.Request)),
            ReopenCaseRecoveryIntent reopen =>
                (ReopenKind, JsonSerializer.Serialize(reopen.Request)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(intent),
                $"Unsupported Case lifecycle recovery intent type '{intent.GetType().FullName}'."),
        };

    private static CaseLifecycleRecoveryIntent Deserialize(
        string kind,
        string payloadJson) =>
        kind switch
        {
            TransitionKind => new TransitionCaseRecoveryIntent(
                JsonSerializer.Deserialize<TransitionLearningCaseStateRequest>(payloadJson)
                ?? throw new InvalidDataException(
                    "Stored transition recovery payload deserialized to null.")),
            CloseKind => new CloseCaseRecoveryIntent(
                JsonSerializer.Deserialize<CloseLearningCaseRequest>(payloadJson)
                ?? throw new InvalidDataException(
                    "Stored close recovery payload deserialized to null.")),
            ReopenKind => new ReopenCaseRecoveryIntent(
                JsonSerializer.Deserialize<ReopenLearningCaseRequest>(payloadJson)
                ?? throw new InvalidDataException(
                    "Stored reopen recovery payload deserialized to null.")),
            _ => throw new InvalidDataException(
                $"Stored Case lifecycle recovery intent kind '{kind}' is unsupported."),
        };

    private static void ValidateIntent(CaseLifecycleRecoveryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        switch (intent)
        {
            case TransitionCaseRecoveryIntent transition:
                ValidateCommon(
                    transition.Request.OperationId,
                    transition.Request.OrganizationId,
                    transition.Request.StudentId,
                    transition.Request.SubjectProfileId,
                    transition.Request.OwnerAssignmentId,
                    transition.Request.CaseId,
                    transition.Request.ExpectedCaseVersion);
                if (transition.Request.TargetState is not (
                    LearningCaseState.Confirmed or
                    LearningCaseState.Intervening or
                    LearningCaseState.PendingVerification or
                    LearningCaseState.Stable))
                {
                    throw new ArgumentException(
                        "Lifecycle transition recovery contains an invalid target state.",
                        nameof(intent));
                }
                break;

            case CloseCaseRecoveryIntent close:
                ValidateCommon(
                    close.Request.OperationId,
                    close.Request.OrganizationId,
                    close.Request.StudentId,
                    close.Request.SubjectProfileId,
                    close.Request.OwnerAssignmentId,
                    close.Request.CaseId,
                    close.Request.ExpectedCaseVersion);
                RequireGuid(close.Request.PrimaryActionId, nameof(close.Request.PrimaryActionId));
                if (close.Request.ExpectedActionVersion <= 0)
                {
                    throw new ArgumentException(
                        "Close recovery requires a positive Action version.",
                        nameof(intent));
                }
                break;

            case ReopenCaseRecoveryIntent reopen:
                ValidateCommon(
                    reopen.Request.OperationId,
                    reopen.Request.OrganizationId,
                    reopen.Request.StudentId,
                    reopen.Request.SubjectProfileId,
                    reopen.Request.OwnerAssignmentId,
                    reopen.Request.CaseId,
                    reopen.Request.ExpectedCaseVersion);
                if (string.IsNullOrWhiteSpace(reopen.Request.NewPrimaryActionText) ||
                    reopen.Request.NewPrimaryActionText.Trim().Length > 1000)
                {
                    throw new ArgumentException(
                        "Reopen recovery requires valid next-Action text.",
                        nameof(intent));
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(intent),
                    $"Unsupported Case lifecycle recovery intent type '{intent.GetType().FullName}'.");
        }
    }

    private static void ValidateCommon(
        Guid operationId,
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid ownerAssignmentId,
        Guid caseId,
        long expectedCaseVersion)
    {
        RequireGuid(operationId, nameof(operationId));
        RequireGuid(organizationId, nameof(organizationId));
        RequireGuid(studentId, nameof(studentId));
        RequireGuid(subjectProfileId, nameof(subjectProfileId));
        RequireGuid(ownerAssignmentId, nameof(ownerAssignmentId));
        RequireGuid(caseId, nameof(caseId));
        if (expectedCaseVersion <= 0)
        {
            throw new ArgumentException(
                "Case lifecycle recovery requires a positive Case version.");
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
