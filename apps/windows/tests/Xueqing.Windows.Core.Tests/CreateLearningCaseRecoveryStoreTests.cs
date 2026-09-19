using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
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
