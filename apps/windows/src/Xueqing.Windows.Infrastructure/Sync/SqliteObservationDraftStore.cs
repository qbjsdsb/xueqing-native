using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Windows.Infrastructure.Sync;

public sealed class SqliteObservationDraftStore
{
    private const long CurrentSchemaVersion = 1;
    private const int DefaultBusyTimeoutSeconds = 5;

    private readonly EncryptedSqliteConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private volatile bool _initialized;

    [SupportedOSPlatform("windows")]
    public SqliteObservationDraftStore(string databasePath)
    {
        _connectionFactory = new EncryptedSqliteConnectionFactory(
            PrepareDatabasePath(databasePath),
            DefaultBusyTimeoutSeconds);
    }

    internal SqliteObservationDraftStore(
        string databasePath,
        ReadOnlySpan<byte> testMasterKey)
    {
        _connectionFactory = new EncryptedSqliteConnectionFactory(
            PrepareDatabasePath(databasePath),
            testMasterKey,
            DefaultBusyTimeoutSeconds);
    }

    public async Task<ObservationDraftOpenResult> OpenAsync(
        ObservationDraftScope scope,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        await InitializeAsync(cancellationToken);

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        await EnsureScopeStateAsync(connection, transaction, scope, cancellationToken);
        var previousEpoch = await ReadEpochAsync(
            connection,
            transaction,
            scope,
            cancellationToken);

        ObservationDraftSnapshot? recovered = null;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT epoch, text, updated_at_unix_ms
                FROM observation_drafts
                WHERE organization_id = $organization_id
                  AND student_id = $student_id
                  AND subject_profile_id = $subject_profile_id
                  AND assignment_id = $assignment_id
                  AND epoch = $epoch
                LIMIT 1;
                """;
            AddScopeParameters(command, scope);
            command.Parameters.AddWithValue("$epoch", previousEpoch);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                recovered = new ObservationDraftSnapshot(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)));
            }
        }

        // Windows can legitimately have more than one app process. Opening a
        // draft therefore claims a new epoch lease atomically. Any older
        // window keeps its in-memory text but can no longer overwrite the
        // newly opened session.
        var claimedEpoch = checked(previousEpoch + 1);
        using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = """
                UPDATE observation_draft_scope_state
                SET epoch = $claimed_epoch
                WHERE organization_id = $organization_id
                  AND student_id = $student_id
                  AND subject_profile_id = $subject_profile_id
                  AND assignment_id = $assignment_id
                  AND epoch = $previous_epoch;
                """;
            AddScopeParameters(claim, scope);
            claim.Parameters.AddWithValue("$claimed_epoch", claimedEpoch);
            claim.Parameters.AddWithValue("$previous_epoch", previousEpoch);
            if (await claim.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "Observation draft lease changed while opening.");
            }
        }

        if (recovered is not null)
        {
            using var migrate = connection.CreateCommand();
            migrate.Transaction = transaction;
            migrate.CommandText = """
                UPDATE observation_drafts
                SET epoch = $claimed_epoch
                WHERE organization_id = $organization_id
                  AND student_id = $student_id
                  AND subject_profile_id = $subject_profile_id
                  AND assignment_id = $assignment_id
                  AND epoch = $previous_epoch;
                """;
            AddScopeParameters(migrate, scope);
            migrate.Parameters.AddWithValue("$claimed_epoch", claimedEpoch);
            migrate.Parameters.AddWithValue("$previous_epoch", previousEpoch);
            if (await migrate.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "Recovered Observation draft could not migrate to the claimed lease.");
            }

            recovered = recovered with { Epoch = claimedEpoch };
        }

        transaction.Commit();
        return new ObservationDraftOpenResult(claimedEpoch, recovered);
    }

    public async Task<bool> SaveAsync(
        ObservationDraftScope scope,
        long expectedEpoch,
        string text,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        ArgumentNullException.ThrowIfNull(text);
        if (expectedEpoch < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedEpoch));
        }

        await InitializeAsync(cancellationToken);
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        await EnsureScopeStateAsync(connection, transaction, scope, cancellationToken);
        var currentEpoch = await ReadEpochAsync(connection, transaction, scope, cancellationToken);
        if (currentEpoch != expectedEpoch)
        {
            return false;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO observation_drafts (
                organization_id,
                student_id,
                subject_profile_id,
                assignment_id,
                epoch,
                text,
                updated_at_unix_ms
            )
            VALUES (
                $organization_id,
                $student_id,
                $subject_profile_id,
                $assignment_id,
                $epoch,
                $text,
                $updated_at_unix_ms
            )
            ON CONFLICT (
                organization_id,
                student_id,
                subject_profile_id,
                assignment_id
            )
            DO UPDATE SET
                epoch = excluded.epoch,
                text = excluded.text,
                updated_at_unix_ms = excluded.updated_at_unix_ms;
            """;
        AddScopeParameters(command, scope);
        command.Parameters.AddWithValue("$epoch", expectedEpoch);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue(
            "$updated_at_unix_ms",
            updatedAt.ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
        return true;
    }

    public async Task<long?> DiscardAsync(
        ObservationDraftScope scope,
        long expectedEpoch,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        if (expectedEpoch < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedEpoch));
        }

        await InitializeAsync(cancellationToken);
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        await EnsureScopeStateAsync(connection, transaction, scope, cancellationToken);
        var currentEpoch = await ReadEpochAsync(connection, transaction, scope, cancellationToken);
        if (currentEpoch != expectedEpoch)
        {
            return null;
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM observation_drafts
                WHERE organization_id = $organization_id
                  AND student_id = $student_id
                  AND subject_profile_id = $subject_profile_id
                  AND assignment_id = $assignment_id;
                """;
            AddScopeParameters(delete, scope);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        var nextEpoch = checked(expectedEpoch + 1);
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE observation_draft_scope_state
                SET epoch = $next_epoch
                WHERE organization_id = $organization_id
                  AND student_id = $student_id
                  AND subject_profile_id = $subject_profile_id
                  AND assignment_id = $assignment_id
                  AND epoch = $expected_epoch;
                """;
            AddScopeParameters(update, scope);
            update.Parameters.AddWithValue("$next_epoch", nextEpoch);
            update.Parameters.AddWithValue("$expected_epoch", expectedEpoch);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "Observation draft epoch changed during discard.");
            }
        }

        transaction.Commit();
        return nextEpoch;
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
                    CREATE TABLE observation_draft_scope_state (
                        organization_id TEXT NOT NULL,
                        student_id TEXT NOT NULL,
                        subject_profile_id TEXT NOT NULL,
                        assignment_id TEXT NOT NULL,
                        epoch INTEGER NOT NULL CHECK(epoch >= 0),
                        PRIMARY KEY (
                            organization_id,
                            student_id,
                            subject_profile_id,
                            assignment_id
                        )
                    ) STRICT;

                    CREATE TABLE observation_drafts (
                        organization_id TEXT NOT NULL,
                        student_id TEXT NOT NULL,
                        subject_profile_id TEXT NOT NULL,
                        assignment_id TEXT NOT NULL,
                        epoch INTEGER NOT NULL CHECK(epoch >= 0),
                        text TEXT NOT NULL,
                        updated_at_unix_ms INTEGER NOT NULL,
                        PRIMARY KEY (
                            organization_id,
                            student_id,
                            subject_profile_id,
                            assignment_id
                        )
                    ) STRICT;

                    PRAGMA user_version = 1;
                    """;
                await schema.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (current != CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported Observation draft schema version: {current}.");
            }

            transaction.Commit();
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static async Task EnsureScopeStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ObservationDraftScope scope,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO observation_draft_scope_state (
                organization_id,
                student_id,
                subject_profile_id,
                assignment_id,
                epoch
            )
            VALUES (
                $organization_id,
                $student_id,
                $subject_profile_id,
                $assignment_id,
                0
            )
            ON CONFLICT DO NOTHING;
            """;
        AddScopeParameters(command, scope);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ReadEpochAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ObservationDraftScope scope,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT epoch
            FROM observation_draft_scope_state
            WHERE organization_id = $organization_id
              AND student_id = $student_id
              AND subject_profile_id = $subject_profile_id
              AND assignment_id = $assignment_id;
            """;
        AddScopeParameters(command, scope);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidDataException("Observation draft epoch is missing."),
            CultureInfo.InvariantCulture);
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

    private static void AddScopeParameters(
        SqliteCommand command,
        ObservationDraftScope scope)
    {
        command.Parameters.AddWithValue("$organization_id", scope.OrganizationId.ToString("D"));
        command.Parameters.AddWithValue("$student_id", scope.StudentId.ToString("D"));
        command.Parameters.AddWithValue("$subject_profile_id", scope.SubjectProfileId.ToString("D"));
        command.Parameters.AddWithValue("$assignment_id", scope.AssignmentId.ToString("D"));
    }

    private static void ValidateScope(ObservationDraftScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.OrganizationId == Guid.Empty ||
            scope.StudentId == Guid.Empty ||
            scope.SubjectProfileId == Guid.Empty ||
            scope.AssignmentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Observation draft scope requires non-empty application-owned UUIDs.",
                nameof(scope));
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
