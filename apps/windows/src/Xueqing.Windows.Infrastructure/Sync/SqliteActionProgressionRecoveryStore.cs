using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Windows.Infrastructure.Sync;

public sealed class SqliteActionProgressionRecoveryStore
{
    private const long CurrentSchemaVersion = 1;
    private const int DefaultBusyTimeoutSeconds = 5;
    private const string RescheduleKind = "reschedule_primary_action";
    private const string VerificationKind = "record_verification_and_next_action";

    private readonly EncryptedSqliteConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private volatile bool _initialized;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public SqliteActionProgressionRecoveryStore(string databasePath)
    {
        var fullPath = PrepareDatabasePath(databasePath);
        _connectionFactory = new EncryptedSqliteConnectionFactory(
            fullPath,
            DefaultBusyTimeoutSeconds);
    }

    internal SqliteActionProgressionRecoveryStore(
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
                    CREATE TABLE pending_action_progression (
                        operation_id TEXT PRIMARY KEY NOT NULL,
                        organization_id TEXT NOT NULL,
                        case_id TEXT NOT NULL,
                        primary_action_id TEXT NOT NULL,
                        intent_kind TEXT NOT NULL
                            CHECK(intent_kind IN (
                                'reschedule_primary_action',
                                'record_verification_and_next_action'
                            )),
                        payload_json TEXT NOT NULL CHECK(length(payload_json) > 0),
                        saved_at_unix_ms INTEGER NOT NULL,
                        disposition TEXT NOT NULL DEFAULT 'pending'
                            CHECK(disposition IN ('pending', 'rejected_cleanup'))
                    ) STRICT;

                    CREATE UNIQUE INDEX ux_pending_action_progression_action
                        ON pending_action_progression (
                            organization_id,
                            case_id,
                            primary_action_id
                        )
                        WHERE disposition = 'pending';

                    PRAGMA user_version = 1;
                    """;
                await schemaCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (version != CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported Action progression recovery schema version: {version}.");
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
        ActionProgressionRecoveryIntent intent,
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
                DELETE FROM pending_action_progression
                WHERE organization_id = $organization_id
                  AND case_id = $case_id
                  AND primary_action_id = $primary_action_id
                  AND disposition = 'rejected_cleanup';
                """;
            cleanupRejected.Parameters.AddWithValue(
                "$organization_id",
                intent.OrganizationId.ToString("D"));
            cleanupRejected.Parameters.AddWithValue(
                "$case_id",
                intent.CaseId.ToString("D"));
            cleanupRejected.Parameters.AddWithValue(
                "$primary_action_id",
                intent.PrimaryActionId.ToString("D"));
            await cleanupRejected.ExecuteNonQueryAsync(cancellationToken);
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO pending_action_progression (
                operation_id,
                organization_id,
                case_id,
                primary_action_id,
                intent_kind,
                payload_json,
                saved_at_unix_ms
            )
            VALUES (
                $operation_id,
                $organization_id,
                $case_id,
                $primary_action_id,
                $intent_kind,
                $payload_json,
                $saved_at_unix_ms
            )
            ON CONFLICT(operation_id) DO NOTHING;
            """;
        insert.Parameters.AddWithValue("$operation_id", intent.OperationId.ToString("D"));
        insert.Parameters.AddWithValue("$organization_id", intent.OrganizationId.ToString("D"));
        insert.Parameters.AddWithValue("$case_id", intent.CaseId.ToString("D"));
        insert.Parameters.AddWithValue("$primary_action_id", intent.PrimaryActionId.ToString("D"));
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
                "A different unresolved Action progression intent already owns this primary Action.",
                exception);
        }

        var stored = await ReadByOperationIdAsync(
            connection,
            transaction,
            intent.OperationId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Action progression recovery insert completed without a readable row.");

        if (inserted == 0 && stored != intent)
        {
            throw new InvalidOperationException(
                $"Operation id '{intent.OperationId:D}' is already bound to a different Action progression intent.");
        }

        transaction.Commit();
    }

    public async Task<ActionProgressionRecoveryIntent?> FindByPrimaryActionAsync(
        Guid organizationId,
        Guid caseId,
        Guid primaryActionId,
        CancellationToken cancellationToken = default)
    {
        RequireGuid(organizationId, nameof(organizationId));
        RequireGuid(caseId, nameof(caseId));
        RequireGuid(primaryActionId, nameof(primaryActionId));
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT intent_kind, payload_json
            FROM pending_action_progression
            WHERE organization_id = $organization_id
              AND case_id = $case_id
              AND primary_action_id = $primary_action_id
              AND disposition = 'pending'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$organization_id", organizationId.ToString("D"));
        command.Parameters.AddWithValue("$case_id", caseId.ToString("D"));
        command.Parameters.AddWithValue("$primary_action_id", primaryActionId.ToString("D"));

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return Deserialize(reader.GetString(0), reader.GetString(1));
    }

    public async Task<IReadOnlyList<ActionProgressionRecoveryIntent>> ListPendingAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        RequireGuid(organizationId, nameof(organizationId));
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT intent_kind, payload_json
            FROM pending_action_progression
            WHERE organization_id = $organization_id
              AND disposition = 'pending'
            ORDER BY saved_at_unix_ms, operation_id;
            """;
        command.Parameters.AddWithValue("$organization_id", organizationId.ToString("D"));

        var pending = new List<ActionProgressionRecoveryIntent>();
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
            UPDATE pending_action_progression
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
            DELETE FROM pending_action_progression
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

    private static async Task<ActionProgressionRecoveryIntent?> ReadByOperationIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT intent_kind, payload_json
            FROM pending_action_progression
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
        ActionProgressionRecoveryIntent intent) =>
        intent switch
        {
            ReschedulePrimaryActionRecoveryIntent reschedule =>
                (RescheduleKind, JsonSerializer.Serialize(reschedule.Request)),
            VerificationAndNextActionRecoveryIntent verification =>
                (VerificationKind, JsonSerializer.Serialize(verification.Request)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(intent),
                $"Unsupported Action progression recovery intent type '{intent.GetType().FullName}'."),
        };

    private static ActionProgressionRecoveryIntent Deserialize(
        string kind,
        string payloadJson) =>
        kind switch
        {
            RescheduleKind => new ReschedulePrimaryActionRecoveryIntent(
                JsonSerializer.Deserialize<ReschedulePrimaryActionRequest>(payloadJson)
                ?? throw new InvalidDataException(
                    "Stored ReschedulePrimaryAction recovery payload deserialized to null.")),
            VerificationKind => new VerificationAndNextActionRecoveryIntent(
                JsonSerializer.Deserialize<RecordVerificationAndNextActionRequest>(payloadJson)
                ?? throw new InvalidDataException(
                    "Stored Verification recovery payload deserialized to null.")),
            _ => throw new InvalidDataException(
                $"Stored Action progression recovery intent kind '{kind}' is unsupported."),
        };

    private static void ValidateIntent(ActionProgressionRecoveryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        switch (intent)
        {
            case ReschedulePrimaryActionRecoveryIntent reschedule:
                ArgumentNullException.ThrowIfNull(reschedule.Request);
                ValidateCommon(
                    reschedule.Request.OperationId,
                    reschedule.Request.OrganizationId,
                    reschedule.Request.StudentId,
                    reschedule.Request.SubjectProfileId,
                    reschedule.Request.OwnerAssignmentId,
                    reschedule.Request.CaseId,
                    reschedule.Request.PrimaryActionId,
                    reschedule.Request.ExpectedCaseVersion,
                    reschedule.Request.ExpectedActionVersion);
                break;

            case VerificationAndNextActionRecoveryIntent verification:
                ArgumentNullException.ThrowIfNull(verification.Request);
                ValidateCommon(
                    verification.Request.OperationId,
                    verification.Request.OrganizationId,
                    verification.Request.StudentId,
                    verification.Request.SubjectProfileId,
                    verification.Request.OwnerAssignmentId,
                    verification.Request.CaseId,
                    verification.Request.CurrentPrimaryActionId,
                    verification.Request.ExpectedCaseVersion,
                    verification.Request.ExpectedActionVersion);

                if (!Enum.IsDefined(typeof(VerificationOutcome), verification.Request.Outcome) ||
                    string.IsNullOrWhiteSpace(verification.Request.VerificationSummary) ||
                    string.IsNullOrWhiteSpace(verification.Request.NextActionText))
                {
                    throw new ArgumentException(
                        "Verification recovery cannot persist an invalid formal intent.",
                        nameof(intent));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(intent),
                    $"Unsupported Action progression recovery intent type '{intent.GetType().FullName}'.");
        }
    }

    private static void ValidateCommon(
        Guid operationId,
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid ownerAssignmentId,
        Guid caseId,
        Guid primaryActionId,
        long expectedCaseVersion,
        long expectedActionVersion)
    {
        RequireGuid(operationId, nameof(operationId));
        RequireGuid(organizationId, nameof(organizationId));
        RequireGuid(studentId, nameof(studentId));
        RequireGuid(subjectProfileId, nameof(subjectProfileId));
        RequireGuid(ownerAssignmentId, nameof(ownerAssignmentId));
        RequireGuid(caseId, nameof(caseId));
        RequireGuid(primaryActionId, nameof(primaryActionId));

        if (expectedCaseVersion <= 0 || expectedActionVersion <= 0)
        {
            throw new ArgumentException(
                "Action progression recovery requires positive expected versions.");
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
