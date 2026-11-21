using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.LocalData;
using Xueqing.Windows.Infrastructure.Support;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class PlatformNeutralLocalDataTests
{
    [TestMethod]
    public async Task Sqlite_observation_draft_store_accepts_platform_neutral_key_store()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xueqing-key-store-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "observation-draft.db");
        var key = SHA256.HashData(Encoding.UTF8.GetBytes("fictional-platform-neutral-key-v1"));

        try
        {
            var keyStore = new FixedTestKeyStore(key);
            var store = new SqliteObservationDraftStore(dbPath, keyStore);
            var scope = new ObservationDraftScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

            var opened = await store.OpenAsync(scope);
            Assert.IsNull(opened.Recovered);

            var saved = await store.SaveAsync(scope, opened.Epoch, "虚构学生：口算速度明显提升", DateTimeOffset.UtcNow);
            Assert.IsTrue(saved);

            var reopened = await store.OpenAsync(scope);
            Assert.AreEqual("虚构学生：口算速度明显提升", reopened.Recovered?.Text);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Diagnostics_writer_and_compatibility_request_accept_linux_and_reject_unknown_platform()
    {
        var linuxRequest = new ClientCompatibilityRequest("linux", "1.0.0-rc.1", 1).Validate();
        Assert.AreEqual("linux", linuxRequest.Platform);
        Assert.Throws<ArgumentException>(() => new ClientCompatibilityRequest("unknown-os", "1.0.0", 1).Validate());

        var linuxSnapshot = new DiagnosticsSnapshot(
            DateTimeOffset.Parse("2026-11-22T01:00:00Z"),
            "Ubuntu 26.04.1 LTS",
            "x64",
            "com.xueqing.app",
            "1.0.0-rc.1",
            "62cad1dfce913775776e4cbd60add6ecab1aefc2",
            1,
            1,
            new DiagnosticDeployment("prod-sg-v1", "production", "xueqing-prod-sg", "supabase"),
            DiagnosticSessionState.Authenticated,
            DiagnosticSyncCategory.Recent,
            new DiagnosticCompatibility(DiagnosticCompatibilityState.Supported, "XQ_CLIENT_SUPPORTED", "v1-initial"),
            new DiagnosticQueueCounts(0, 0, 0),
            Array.Empty<string>(),
            PlatformKind: "linux");

        var json = DiagnosticsArchiveWriter.Serialize(linuxSnapshot);
        StringAssert.Contains(json, "\"kind\": \"linux\"");

        var invalidSnapshot = linuxSnapshot with { PlatformKind = "unknown-os" };
        Assert.Throws<ArgumentException>(() => DiagnosticsArchiveWriter.Serialize(invalidSnapshot));
    }

    private sealed class FixedTestKeyStore(byte[] masterKey) : ILocalDatabaseKeyStore
    {
        public Task<byte[]> LoadOrCreateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(masterKey.ToArray());
    }
}
