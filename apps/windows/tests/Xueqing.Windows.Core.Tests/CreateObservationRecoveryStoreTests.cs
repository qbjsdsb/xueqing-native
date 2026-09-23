using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class CreateObservationRecoveryStoreTests
{
    private static readonly byte[] TestMasterKey = SHA256.HashData(
        Encoding.UTF8.GetBytes("fictional-create-observation-recovery-key-v1"));

    [TestMethod]
    public async Task Pending_intent_survives_reopen_and_same_operation_is_idempotent()
    {
        var path = CreateDatabasePath();
        var request = Request();
        var first = new SqliteCreateObservationRecoveryStore(path, TestMasterKey);

        await first.SaveAsync(request);
        await first.SaveAsync(request);

        var reopened = new SqliteCreateObservationRecoveryStore(path, TestMasterKey);
        var restored = await reopened.FindPendingAsync(Scope());

        Assert.IsNotNull(restored);
        Assert.AreEqual(request.OperationId, restored.OperationId);
        Assert.AreEqual(request.RawText, restored.RawText);
        Assert.AreEqual(
            "windows_quick_capture",
            restored.ClientCaptureMetadata?["source"]);
    }

    [TestMethod]
    public async Task One_teaching_context_cannot_hold_two_unresolved_operations()
    {
        var store = new SqliteCreateObservationRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var request = Request();

        await store.SaveAsync(request);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(request with
            {
                OperationId = Guid.Parse("74000000-0000-0000-0000-000000000099"),
            }));
    }

    [TestMethod]
    public async Task Same_operation_with_different_payload_is_rejected()
    {
        var store = new SqliteCreateObservationRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var request = Request();

        await store.SaveAsync(request);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(request with
            {
                RawText = "不同的正式 Observation 意图",
            }));
    }

    [TestMethod]
    public async Task Rejected_intent_is_hidden_and_allows_corrected_operation()
    {
        var store = new SqliteCreateObservationRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var rejected = Request();

        await store.SaveAsync(rejected);
        await store.MarkRejectedAsync(rejected.OperationId);
        Assert.IsNull(await store.FindPendingAsync(Scope()));

        var corrected = rejected with
        {
            OperationId = Guid.Parse("74000000-0000-0000-0000-000000000099"),
            RawText = "修正后的虚构课堂观察",
        };
        await store.SaveAsync(corrected);

        var restored = await store.FindPendingAsync(Scope());
        Assert.AreEqual(corrected.OperationId, restored?.OperationId);
        Assert.AreEqual(corrected.RawText, restored?.RawText);
    }

    [TestMethod]
    public async Task Recovery_database_does_not_expose_teaching_text_as_plaintext()
    {
        var path = CreateDatabasePath();
        var request = Request() with
        {
            RawText = "仅用于 Windows recovery 加密验证的虚构教学文本XYZ",
        };
        var store = new SqliteCreateObservationRecoveryStore(path, TestMasterKey);

        await store.SaveAsync(request);

        AssertEncrypted(path, request.RawText);
    }

    private static CreateObservationRequest Request() =>
        new(
            Guid.Parse("74000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            "虚构课堂观察正式意图",
            DateTimeOffset.Parse("2026-09-22T10:00:00Z"),
            new Dictionary<string, string>
            {
                ["source"] = "windows_quick_capture",
            });

    private static ObservationDraftScope Scope() =>
        new(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"));

    private static void AssertEncrypted(string path, string secret)
    {
        var databaseText = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.IsFalse(databaseText.Contains(secret, StringComparison.Ordinal));

        var walPath = path + "-wal";
        if (File.Exists(walPath))
        {
            var walText = Encoding.UTF8.GetString(File.ReadAllBytes(walPath));
            Assert.IsFalse(walText.Contains(secret, StringComparison.Ordinal));
        }
    }

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "xueqing-native-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "create-observation-recovery.db");
    }
}
