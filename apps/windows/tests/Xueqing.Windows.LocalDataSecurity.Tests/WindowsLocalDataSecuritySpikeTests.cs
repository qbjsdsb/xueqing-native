using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.LocalDataSecurity;

namespace Xueqing.Windows.LocalDataSecurity.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsLocalDataSecuritySpikeTests
{
    [TestMethod]
    public void Aes_gcm_envelope_is_randomized_and_bound_to_context()
    {
        var protector = new AesGcmEnvelopeProtector();
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("虚构学生：课堂证据，只用于安全 Spike");

        try
        {
            var first = protector.Protect(plaintext, key, "evidence-text", "evidence-fictional-001");
            var second = protector.Protect(plaintext, key, "evidence-text", "evidence-fictional-001");

            Assert.IsFalse(first.SequenceEqual(second), "AES-GCM envelopes must use a fresh nonce.");
            CollectionAssert.AreEqual(
                plaintext,
                protector.Unprotect(first, key, "evidence-text", "evidence-fictional-001"));

            AssertCryptographicFailure(
                () => protector.Unprotect(first, key, "evidence-text", "evidence-fictional-002"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    [TestMethod]
    public void Aes_gcm_envelope_rejects_tampering()
    {
        var protector = new AesGcmEnvelopeProtector();
        var key = RandomNumberGenerator.GetBytes(32);
        var envelope = protector.Protect("fictional-sensitive-value"u8, key, "attachment-key", "attachment-fictional-001");

        try
        {
            envelope[^1] ^= 0x01;
            AssertCryptographicFailure(
                () => protector.Unprotect(envelope, key, "attachment-key", "attachment-fictional-001"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(envelope);
        }
    }

    [TestMethod]
    public void Dpapi_current_user_roundtrips_master_key_and_binds_purpose()
    {
        var protector = new WindowsDpapiCurrentUserProtector();
        var masterKey = RandomNumberGenerator.GetBytes(32);

        try
        {
            var wrapped = protector.Protect(masterKey, "local-data-master-key");
            Assert.IsFalse(masterKey.SequenceEqual(wrapped));

            var restored = protector.Unprotect(wrapped, "local-data-master-key");
            try
            {
                CollectionAssert.AreEqual(masterKey, restored);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(restored);
            }

            AssertCryptographicFailure(
                () => protector.Unprotect(wrapped, "different-key-purpose"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    [TestMethod]
    public async Task Sqlite3mc_encrypts_database_and_rejects_missing_or_wrong_password()
    {
        var directory = CreateTempDirectory();
        var databasePath = Path.Combine(directory, "encrypted.db");
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        const string Marker = "XUEQING_FICTIONAL_SENSITIVE_MARKER_20260916";

        await using (var connection = Sqlite3McProbe.CreateConnection(databasePath, password))
        {
            await connection.OpenAsync();
            var version = await Sqlite3McProbe.GetVersionAsync(connection);
            StringAssert.StartsWith(version, "2.4.");

            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;");
            await ExecuteAsync(connection, "CREATE TABLE secure_probe(id INTEGER PRIMARY KEY, secret TEXT NOT NULL);");
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO secure_probe(secret) VALUES ($secret);";
            insert.Parameters.AddWithValue("$secret", Marker);
            await insert.ExecuteNonQueryAsync();
        }

        AssertEncryptedFilesDoNotContain(directory, "encrypted.db", Marker);
        Assert.IsFalse(await CanReadMarkerAsync(databasePath, "wrong-password", Marker));
        Assert.IsTrue(await CanReadMarkerAsync(databasePath, password, Marker));
        Assert.IsFalse(await CanReadMarkerWithoutPasswordAsync(databasePath, Marker));
    }

    [TestMethod]
    public async Task Sqlite3mc_rekeys_encrypted_wal_database()
    {
        var directory = CreateTempDirectory();
        var databasePath = Path.Combine(directory, "rekey.db");
        var oldPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var newPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        const string Marker = "XUEQING_FICTIONAL_REKEY_MARKER";

        await using (var connection = Sqlite3McProbe.CreateConnection(databasePath, oldPassword))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;");
            await ExecuteAsync(connection, "CREATE TABLE secure_probe(secret TEXT NOT NULL);");
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO secure_probe(secret) VALUES ($secret);";
            insert.Parameters.AddWithValue("$secret", Marker);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var connection = Sqlite3McProbe.CreateConnection(databasePath, oldPassword))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, $"PRAGMA rekey = '{EscapeSqlLiteral(newPassword)}';");
        }

        Assert.IsFalse(await CanReadMarkerAsync(databasePath, oldPassword, Marker));
        Assert.IsTrue(await CanReadMarkerAsync(databasePath, newPassword, Marker));
        AssertEncryptedFilesDoNotContain(directory, "rekey.db", Marker);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> CanReadMarkerAsync(string databasePath, string password, string expected)
    {
        try
        {
            await using var connection = Sqlite3McProbe.CreateConnection(databasePath, password, SqliteOpenMode.ReadOnly);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT secret FROM secure_probe LIMIT 1;";
            return string.Equals((string?)await command.ExecuteScalarAsync(), expected, StringComparison.Ordinal);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static async Task<bool> CanReadMarkerWithoutPasswordAsync(string databasePath, string expected)
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT secret FROM secure_probe LIMIT 1;";
            return string.Equals((string?)await command.ExecuteScalarAsync(), expected, StringComparison.Ordinal);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static void AssertEncryptedFilesDoNotContain(string directory, string databaseFileName, string marker)
    {
        var needle = Encoding.UTF8.GetBytes(marker);
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, databaseFileName + "*"))
            {
                var bytes = File.ReadAllBytes(path);
                Assert.IsFalse(ContainsSequence(bytes, needle), $"Plaintext marker leaked into {Path.GetFileName(path)}.");
            }

            var databaseBytes = File.ReadAllBytes(Path.Combine(directory, databaseFileName));
            Assert.IsFalse(databaseBytes.AsSpan().StartsWith("SQLite format 3\0"u8));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(needle);
        }
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

    private static string EscapeSqlLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "xueqing-native-security-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertCryptographicFailure(Action action)
    {
        try
        {
            action();
            Assert.Fail("Expected cryptographic verification to fail.");
        }
        catch (CryptographicException)
        {
        }
    }
}
