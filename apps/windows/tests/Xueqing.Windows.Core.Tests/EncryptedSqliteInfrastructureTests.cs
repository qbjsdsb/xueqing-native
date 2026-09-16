using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class EncryptedSqliteInfrastructureTests
{
    [TestMethod]
    public async Task Fixed_key_factory_encrypts_payload_and_wrong_key_cannot_read_schema()
    {
        var databasePath = CreateDatabasePath();
        var masterKey = SHA256.HashData(Encoding.UTF8.GetBytes("fictional-xueqing-test-master-key-v1"));
        var marker = "FICTIONAL-STUDENT-EVIDENCE-DO-NOT-STORE-IN-PLAINTEXT";

        try
        {
            var factory = new EncryptedSqliteConnectionFactory(databasePath, masterKey);
            await using (var connection = await factory.OpenAsync())
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE secure_probe(value TEXT NOT NULL);
                    INSERT INTO secure_probe(value) VALUES ($value);
                    """;
                command.Parameters.AddWithValue("$value", marker);
                await command.ExecuteNonQueryAsync();
            }

            var databaseBytes = await File.ReadAllBytesAsync(databasePath);
            Assert.IsFalse(
                ContainsSequence(databaseBytes, Encoding.UTF8.GetBytes(marker)),
                "Sensitive marker was visible in the raw SQLite database bytes.");

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
    public async Task Windows_dpapi_factory_reopens_database_and_missing_key_fails_closed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var databasePath = CreateDatabasePath();
        var factory = new EncryptedSqliteConnectionFactory(databasePath);

        await using (var connection = await factory.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE secure_probe(id INTEGER PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        Assert.IsTrue(File.Exists(databasePath + ".key"));

        var reopenedFactory = new EncryptedSqliteConnectionFactory(databasePath);
        await using (var reopened = await reopenedFactory.OpenAsync())
        {
            await using var read = reopened.CreateCommand();
            read.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'secure_probe';";
            Assert.AreEqual(1L, Convert.ToInt64(await read.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
        }

        File.Delete(databasePath + ".key");
        var missingKeyFactory = new EncryptedSqliteConnectionFactory(databasePath);
        await Assert.ThrowsExactlyAsync<LocalDatabaseKeyUnavailableException>(
            () => missingKeyFactory.OpenAsync());
    }

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "xueqing-native-encrypted-infrastructure-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "local.db");
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
