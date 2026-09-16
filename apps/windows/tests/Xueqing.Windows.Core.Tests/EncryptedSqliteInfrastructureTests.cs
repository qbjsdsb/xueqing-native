using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Sync;
using Xueqing.Windows.Infrastructure.LocalData;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class EncryptedSqliteInfrastructureTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Fixed_key_factory_uses_sqlite3mc_encrypts_database_and_wal_and_rejects_wrong_key()
    {
        var databasePath = CreateDatabasePath();
        var masterKey = SHA256.HashData(Encoding.UTF8.GetBytes("fictional-xueqing-test-master-key-v1"));
        var marker = "FICTIONAL-STUDENT-EVIDENCE-DO-NOT-STORE-IN-PLAINTEXT";

        try
        {
            var factory = new EncryptedSqliteConnectionFactory(databasePath, masterKey);
            await using (var connection = await factory.OpenAsync())
            {
                await using (var runtime = connection.CreateCommand())
                {
                    runtime.CommandText = "SELECT sqlite3mc_version();";
                    var runtimeVersion = Convert.ToString(
                        await runtime.ExecuteScalarAsync(),
                        CultureInfo.InvariantCulture);
                    Assert.IsNotNull(runtimeVersion);
                    StringAssert.Contains(runtimeVersion, "2.4.0");
                }

                await using (var wal = connection.CreateCommand())
                {
                    wal.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL;";
                    await wal.ExecuteNonQueryAsync();
                }

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE secure_probe(value TEXT NOT NULL);
                    INSERT INTO secure_probe(value) VALUES ($value);
                    """;
                command.Parameters.AddWithValue("$value", marker);
                await command.ExecuteNonQueryAsync();

                await AssertFileDoesNotContainAsync(databasePath, marker);
                await AssertFileDoesNotContainAsync(databasePath + "-wal", marker, allowMissing: true);
            }

            await AssertFileDoesNotContainAsync(databasePath, marker);
            await AssertFileDoesNotContainAsync(databasePath + "-wal", marker, allowMissing: true);

            var wrongKey = SHA256.HashData(Encoding.UTF8.GetBytes("fictional-wrong-master-key-v1"));
            try
            {
                var wrongFactory = new EncryptedSqliteConnectionFactory(databasePath, wrongKey);
                await Assert.ThrowsExactlyAsync<SqliteException>(async () =>
                {
                    await using var wrongConnection = await wrongFactory.OpenAsync();
                    await using var read = wrongConnection.CreateCommand();
                    read.CommandText = "SELECT COUNT(*) FROM secure_probe;";
                    _ = await read.ExecuteScalarAsync();
                });
            }
            finally
            {
                CryptographicOperations.ZeroMemory(wrongKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    [TestMethod]
    public async Task Durable_outbox_payload_is_encrypted_and_survives_reopen_with_same_key()
    {
        var databasePath = CreateDatabasePath();
        var masterKey = SHA256.HashData(Encoding.UTF8.GetBytes("fictional-xueqing-outbox-encryption-key-v1"));
        var operationId = Guid.NewGuid();
        var marker = "FICTIONAL-OUTBOX-PAYLOAD-MUST-NOT-BE-PLAINTEXT";

        try
        {
            var firstStore = new SqliteDurableOutboxStore(databasePath, masterKey);
            await firstStore.EnqueueAsync(CreateIntent(operationId, $"{{\"marker\":\"{marker}\"}}"));

            await AssertFileDoesNotContainAsync(databasePath, marker);
            await AssertFileDoesNotContainAsync(databasePath + "-wal", marker, allowMissing: true);

            var reopenedStore = new SqliteDurableOutboxStore(databasePath, masterKey);
            var restored = await reopenedStore.GetAsync(operationId);

            Assert.IsNotNull(restored);
            Assert.AreEqual(operationId, restored.Intent.OperationId);
            StringAssert.Contains(restored.Intent.PayloadJson, marker);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task Windows_dpapi_key_store_concurrent_first_creation_converges_on_one_key()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var databasePath = CreateDatabasePath();
        var keys = await Task.WhenAll(
            Enumerable.Range(0, 16)
                .Select(_ => new WindowsDpapiDatabaseKeyStore(databasePath).LoadOrCreateAsync()));

        try
        {
            Assert.IsTrue(File.Exists(databasePath + ".key"));
            foreach (var key in keys.Skip(1))
            {
                CollectionAssert.AreEqual(keys[0], key);
            }
        }
        finally
        {
            foreach (var key in keys)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task Windows_sqlite3mc_encrypted_vfs_documents_legacy_path_limit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "xueqing-native-longpath-vfs-tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = CreateBeyondMaxPathDatabasePath(rootDirectory);

        try
        {
            Assert.IsTrue(
                databasePath.Length > 260,
                $"Regression path must exceed MAX_PATH; actual length was {databasePath.Length}.");

            var store = new SqliteDurableOutboxStore(databasePath);
            var exception = await Assert.ThrowsExactlyAsync<SqliteException>(
                () => store.CountAsync());

            Assert.AreEqual(14, exception.SqliteErrorCode);
            Assert.IsTrue(
                File.Exists(databasePath + ".key"),
                "DPAPI key creation should succeed before SQLite reports its VFS path-length limit.");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task Windows_dpapi_outbox_reopens_and_corrupt_or_missing_key_fails_closed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var databasePath = CreateDatabasePath();
        var operationId = Guid.NewGuid();
        var firstStore = new SqliteDurableOutboxStore(databasePath);
        await firstStore.EnqueueAsync(CreateIntent(operationId, "{\"fictional\":true}"));

        var keyPath = databasePath + ".key";
        Assert.IsTrue(File.Exists(keyPath));

        var reopenedStore = new SqliteDurableOutboxStore(databasePath);
        var restored = await reopenedStore.GetAsync(operationId);
        Assert.IsNotNull(restored);
        Assert.AreEqual(operationId, restored.Intent.OperationId);

        await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(48));
        var corruptKeyStore = new SqliteDurableOutboxStore(databasePath);
        await Assert.ThrowsExactlyAsync<CryptographicException>(
            () => corruptKeyStore.CountAsync());

        File.Delete(keyPath);
        var missingKeyStore = new SqliteDurableOutboxStore(databasePath);
        await Assert.ThrowsExactlyAsync<LocalDatabaseKeyUnavailableException>(
            () => missingKeyStore.CountAsync());
    }

    private static OutboxCommandIntent CreateIntent(Guid operationId, string payloadJson)
        => new(
            operationId,
            "append_evidence",
            "case-fictional-security-001",
            "org-fictional-001/student-fictional-001/subject-chinese",
            7,
            "assignment-version-3",
            payloadJson,
            BaseTime);

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "xueqing-native-encrypted-infrastructure-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "local.db");
    }

    private static string CreateBeyondMaxPathDatabasePath(string rootDirectory)
    {
        const string fileName = "durable-intent.db";
        var directory = Path.GetFullPath(rootDirectory);

        while (Path.Combine(directory, fileName).Length <= 260)
        {
            directory = Path.Combine(directory, "path");
        }

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    private static async Task AssertFileDoesNotContainAsync(
        string path,
        string marker,
        bool allowMissing = false)
    {
        if (!File.Exists(path))
        {
            if (allowMissing)
            {
                return;
            }

            Assert.Fail($"Expected encrypted SQLite file '{path}' to exist.");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        var bytes = copy.ToArray();

        Assert.IsFalse(
            ContainsSequence(bytes, Encoding.UTF8.GetBytes(marker)),
            $"Sensitive marker was visible in raw SQLite bytes at '{path}'.");
    }

    private static bool ContainsSequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty)
        {
            return true;
        }

        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.Slice(index, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }
}
