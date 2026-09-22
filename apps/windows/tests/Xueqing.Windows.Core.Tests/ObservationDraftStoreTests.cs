using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class ObservationDraftStoreTests
{
    private static readonly byte[] TestMasterKey = SHA256.HashData(
        Encoding.UTF8.GetBytes("fictional-observation-draft-key-v1"));

    [TestMethod]
    public async Task Draft_survives_reopen_and_epoch_prevents_stale_writer()
    {
        var path = CreateDatabasePath();
        var scope = Scope();
        var first = new SqliteObservationDraftStore(path, TestMasterKey);

        var opened = await first.OpenAsync(scope);
        Assert.AreEqual(0L, opened.Epoch);
        Assert.IsNull(opened.Recovered);

        Assert.IsTrue(await first.SaveAsync(
            scope,
            opened.Epoch,
            "虚构课堂观察草稿一",
            DateTimeOffset.Parse("2026-09-22T10:00:00Z")));

        var reopened = new SqliteObservationDraftStore(path, TestMasterKey);
        var recovered = await reopened.OpenAsync(scope);
        Assert.AreEqual(opened.Epoch, recovered.Epoch);
        Assert.AreEqual("虚构课堂观察草稿一", recovered.Recovered?.Text);

        var nextEpoch = await reopened.DiscardAsync(scope, recovered.Epoch);
        Assert.AreEqual(1L, nextEpoch);
        Assert.IsFalse(await first.SaveAsync(
            scope,
            opened.Epoch,
            "旧窗口不应覆盖新 epoch",
            DateTimeOffset.UtcNow));

        var afterDiscard = await reopened.OpenAsync(scope);
        Assert.AreEqual(1L, afterDiscard.Epoch);
        Assert.IsNull(afterDiscard.Recovered);
    }

    [TestMethod]
    public async Task Draft_database_does_not_expose_text_as_plaintext()
    {
        var path = CreateDatabasePath();
        var scope = Scope();
        const string text = "仅用于 Windows 加密草稿验证的虚构文本XYZ";
        var store = new SqliteObservationDraftStore(path, TestMasterKey);

        var opened = await store.OpenAsync(scope);
        Assert.IsTrue(await store.SaveAsync(
            scope,
            opened.Epoch,
            text,
            DateTimeOffset.UtcNow));

        AssertEncrypted(path, text);
    }

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
        return Path.Combine(directory, "observation-draft.db");
    }
}
