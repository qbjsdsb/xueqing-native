using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.LocalData;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class CreateLearningCaseRecoveryStoreTests
{
    private static readonly byte[] TestMasterKey = SHA256.HashData(
        Encoding.UTF8.GetBytes("fictional-create-learning-case-recovery-key-v1"));

    [TestMethod]
    public async Task Pending_intent_survives_reopen_and_is_found_by_source_scope()
    {
        var path = CreateDatabasePath();
        var request = Request();

        var first = new SqliteCreateLearningCaseRecoveryStore(path, TestMasterKey);
        await first.SaveAsync(request);

        var reopened = new SqliteCreateLearningCaseRecoveryStore(path, TestMasterKey);
        var restored = await reopened.FindBySourceObservationAsync(
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.SourceObservationId!.Value);

        Assert.AreEqual(request, restored);

        await reopened.RemoveAsync(request.OperationId);
        Assert.IsNull(await reopened.FindBySourceObservationAsync(
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.SourceObservationId.Value));
    }

    [TestMethod]
    public async Task Existing_v1_encrypted_store_migrates_without_losing_pending_intent()
    {
        var path = CreateDatabasePath();
        var request = Request();
        await CreateV1DatabaseAsync(path, request);

        var migrated = new SqliteCreateLearningCaseRecoveryStore(path, TestMasterKey);

        var inspectedOrganizations =
            await migrated.InspectStoredOrganizationIdsAsync();
        CollectionAssert.AreEqual(
            new[] { request.OrganizationId },
            inspectedOrganizations.ToArray());

        var preMigrationFactory =
            new EncryptedSqliteConnectionFactory(path, TestMasterKey);
        using (var preMigrationConnection =
               await preMigrationFactory.OpenAsync())
        using (var preMigrationCommand = preMigrationConnection.CreateCommand())
        {
            preMigrationCommand.CommandText = "PRAGMA user_version;";
            Assert.AreEqual(
                1L,
                Convert.ToInt64(
                    await preMigrationCommand.ExecuteScalarAsync()));
        }

        var pending = await migrated.ListPendingAsync(request.OrganizationId);

        Assert.AreEqual(1, pending.Count);
        Assert.AreEqual(request, pending[0]);
        Assert.AreEqual(
            request,
            await migrated.FindBySourceObservationAsync(
                request.OrganizationId,
                request.StudentId,
                request.SubjectProfileId,
                request.SourceObservationId!.Value));

        var factory = new EncryptedSqliteConnectionFactory(path, TestMasterKey);
        using var connection = await factory.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.AreEqual(2L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [TestMethod]
    public void Organization_scope_identity_is_deterministic_and_actor_isolated()
    {
        const string environment = "production-eu";
        const string actorA = "10000000-0000-0000-0000-000000000001";
        const string actorB = "10000000-0000-0000-0000-000000000002";
        const string organization = "20000000-0000-0000-0000-000000000001";

        var first =
            WindowsLocalScopeIdentity.ComputeOrganizationScopeDirectoryName(
                environment,
                actorA,
                organization);
        var repeated =
            WindowsLocalScopeIdentity.ComputeOrganizationScopeDirectoryName(
                environment,
                actorA,
                organization);
        var otherActor =
            WindowsLocalScopeIdentity.ComputeOrganizationScopeDirectoryName(
                environment,
                actorB,
                organization);
        var otherEnvironment =
            WindowsLocalScopeIdentity.ComputeOrganizationScopeDirectoryName(
                "staging",
                actorA,
                organization);

        Assert.AreEqual(first, repeated);
        Assert.AreNotEqual(first, otherActor);
        Assert.AreNotEqual(first, otherEnvironment);
        Assert.AreEqual(32, first.Length);
        Assert.IsTrue(first.All(character =>
            char.IsAsciiHexDigit(character) &&
            !char.IsUpper(character)));
    }

    [TestMethod]
    public async Task Actor_recovery_index_survives_reopen_and_is_encrypted()
    {
        var path = CreateIndexDatabasePath();
        var firstOrganization = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var removedOrganization = Guid.Parse("20000000-0000-0000-0000-000000000099");

        var first = new SqliteCreateLearningCaseRecoveryIndex(path, TestMasterKey);
        await first.RegisterOrganizationAsync(firstOrganization);
        await first.RegisterOrganizationAsync(removedOrganization);
        await first.RegisterOrganizationAsync(firstOrganization);

        var reopened = new SqliteCreateLearningCaseRecoveryIndex(path, TestMasterKey);
        CollectionAssert.AreEqual(
            new[] { firstOrganization, removedOrganization },
            (await reopened.ListOrganizationsAsync()).ToArray());

        var databaseText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));
        Assert.IsFalse(databaseText.Contains(
            firstOrganization.ToString("D"),
            StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(databaseText.Contains(
            removedOrganization.ToString("D"),
            StringComparison.OrdinalIgnoreCase));

        var walPath = path + "-wal";
        if (File.Exists(walPath))
        {
            var walText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(walPath));
            Assert.IsFalse(walText.Contains(
                firstOrganization.ToString("D"),
                StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(walText.Contains(
                removedOrganization.ToString("D"),
                StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public async Task Pending_intents_are_enumerable_without_recent_observation_projection()
    {
        var store = new SqliteCreateLearningCaseRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var first = Request();
        var second = Request() with
        {
            OperationId = Guid.Parse("76000000-0000-0000-0000-000000000002"),
            SourceObservationId = Guid.Parse("60000000-0000-0000-0000-000000000002"),
            Title = "第二个尚未确认的虚构学情问题",
        };

        await store.SaveAsync(first);
        await store.SaveAsync(second);

        var pending = await store.ListPendingAsync(first.OrganizationId);

        Assert.AreEqual(2, pending.Count);
        CollectionAssert.AreEqual(
            new[] { first.OperationId, second.OperationId },
            pending.Select(item => item.OperationId).ToArray());
    }

    [TestMethod]
    public async Task Rejected_intent_is_hidden_and_does_not_block_corrected_operation()
    {
        var store = new SqliteCreateLearningCaseRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var rejected = Request();

        await store.SaveAsync(rejected);
        await store.MarkRejectedAsync(rejected.OperationId);

        Assert.IsNull(await store.FindBySourceObservationAsync(
            rejected.OrganizationId,
            rejected.StudentId,
            rejected.SubjectProfileId,
            rejected.SourceObservationId!.Value));
        Assert.AreEqual(0, (await store.ListPendingAsync(rejected.OrganizationId)).Count);

        var corrected = rejected with
        {
            OperationId = Guid.Parse("76000000-0000-0000-0000-000000000099"),
            Title = "修正后的虚构学情问题",
        };
        await store.SaveAsync(corrected);

        Assert.AreEqual(
            corrected,
            await store.FindBySourceObservationAsync(
                corrected.OrganizationId,
                corrected.StudentId,
                corrected.SubjectProfileId,
                corrected.SourceObservationId!.Value));
    }

    [TestMethod]
    public async Task Same_operation_is_idempotent_but_collision_is_rejected()
    {
        var store = new SqliteCreateLearningCaseRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var request = Request();

        await store.SaveAsync(request);
        await store.SaveAsync(request);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(request with { Title = "不同的正式意图" }));
    }

    [TestMethod]
    public async Task Same_source_observation_cannot_hold_two_unresolved_operations()
    {
        var store = new SqliteCreateLearningCaseRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var request = Request();

        await store.SaveAsync(request);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(
                request with
                {
                    OperationId = Guid.Parse("76000000-0000-0000-0000-000000000099"),
                }));
    }

    [TestMethod]
    public async Task Recovery_database_does_not_expose_teaching_text_as_plaintext()
    {
        var path = CreateDatabasePath();
        var request = Request() with
        {
            Title = "仅用于加密验证的虚构关注问题XYZ",
            PrimaryActionText = "仅用于加密验证的虚构下一步行动XYZ",
        };
        var store = new SqliteCreateLearningCaseRecoveryStore(path, TestMasterKey);

        await store.SaveAsync(request);

        var databaseBytes = await File.ReadAllBytesAsync(path);
        var databaseText = Encoding.UTF8.GetString(databaseBytes);
        Assert.IsFalse(databaseText.Contains(request.Title, StringComparison.Ordinal));
        Assert.IsFalse(databaseText.Contains(request.PrimaryActionText, StringComparison.Ordinal));

        var walPath = path + "-wal";
        if (File.Exists(walPath))
        {
            var walText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(walPath));
            Assert.IsFalse(walText.Contains(request.Title, StringComparison.Ordinal));
            Assert.IsFalse(walText.Contains(request.PrimaryActionText, StringComparison.Ordinal));
        }
    }

    private static async Task CreateV1DatabaseAsync(
        string path,
        CreateLearningCaseRequest request)
    {
        var factory = new EncryptedSqliteConnectionFactory(path, TestMasterKey);
        using var connection = await factory.OpenAsync();
        using var command = connection.CreateCommand();
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

            INSERT INTO pending_create_learning_case (
                operation_id,
                organization_id,
                student_id,
                subject_profile_id,
                source_observation_id,
                payload_json,
                saved_at_unix_ms
            ) VALUES (
                $operation_id,
                $organization_id,
                $student_id,
                $subject_profile_id,
                $source_observation_id,
                $payload_json,
                1
            );

            PRAGMA user_version = 1;
            """;
        command.Parameters.AddWithValue("$operation_id", request.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$organization_id", request.OrganizationId.ToString("D"));
        command.Parameters.AddWithValue("$student_id", request.StudentId.ToString("D"));
        command.Parameters.AddWithValue("$subject_profile_id", request.SubjectProfileId.ToString("D"));
        command.Parameters.AddWithValue("$source_observation_id", request.SourceObservationId!.Value.ToString("D"));
        command.Parameters.AddWithValue("$payload_json", JsonSerializer.Serialize(request));
        await command.ExecuteNonQueryAsync();
    }

    private static CreateLearningCaseRequest Request() =>
        new(
            Guid.Parse("76000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            "概括题压缩仍不稳定",
            "下节课用陌生材料复核三道题",
            new DateOnly(2026, 9, 20),
            Guid.Parse("60000000-0000-0000-0000-000000000001"));

    private static string CreateIndexDatabasePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "xueqing-native-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "online-command-recovery-index.db");
    }

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "xueqing-native-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "online-command-recovery.db");
    }
}
