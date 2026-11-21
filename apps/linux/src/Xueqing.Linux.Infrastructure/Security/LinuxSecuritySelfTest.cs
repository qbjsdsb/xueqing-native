using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Linux.Infrastructure.Security;

public sealed record LinuxSecurityCheckReport(
    bool Passed,
    string Sqlite3McVersion,
    string BootSessionId,
    long MonotonicMs,
    IReadOnlyList<string> Checks);

[SupportedOSPlatform("linux")]
public static class LinuxSecuritySelfTest
{
    private static readonly Lazy<bool> SqliteInit = new(
        static () =>
        {
            SQLitePCL.Batteries_V2.Init();
            return true;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static async Task<LinuxSecurityCheckReport> RunAsync(CancellationToken cancellationToken = default)
    {
        _ = SqliteInit.Value;

        var checks = new List<string>();
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"xueqing-linux-sec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var dbPath = Path.Combine(tempRoot, "durable-intent-check.db");
        var keyStore = new LinuxSecretServiceDatabaseKeyStore(dbPath);
        const string marker = "FICTIONAL-XUEQING-LINUX-EVIDENCE-MUST-NOT-BE-PLAINTEXT";

        try
        {
            // 1. Boot session + monotonic clock check
            var clockBefore = LinuxOfflineAccessClock.Sample();
            await Task.Delay(15, cancellationToken);
            var clockAfter = LinuxOfflineAccessClock.Sample();
            if (!string.Equals(clockBefore.BootSessionId, clockAfter.BootSessionId, StringComparison.Ordinal) ||
                clockAfter.MonotonicElapsedMilliseconds < clockBefore.MonotonicElapsedMilliseconds)
            {
                throw new InvalidOperationException("Linux boot_id or monotonic clock failed consistency check.");
            }

            checks.Add($"[PASS] boot_id + monotonic clock ({clockAfter.BootSessionId[..8]}..., {clockAfter.MonotonicElapsedMilliseconds} ms)");

            // 2. Create 256-bit key in libsecret + create encrypted SQLite3MC database
            string sqlite3mcVersion;
            var createdKey = await keyStore.LoadOrCreateAsync(cancellationToken);
            try
            {
                await using var conn = await OpenEncryptedAsync(dbPath, createdKey, cancellationToken);
                await using (var verCmd = conn.CreateCommand())
                {
                    verCmd.CommandText = "SELECT sqlite3mc_version();";
                    sqlite3mcVersion = Convert.ToString(await verCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture)
                        ?? string.Empty;
                    if (!sqlite3mcVersion.Contains("2.4.0", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Unexpected sqlite3mc_version(): {sqlite3mcVersion}");
                    }
                }

                await using (var setupCmd = conn.CreateCommand())
                {
                    setupCmd.CommandText = """
                        PRAGMA journal_mode = WAL;
                        PRAGMA synchronous = FULL;
                        CREATE TABLE durable_intent_probe(id INTEGER PRIMARY KEY, payload TEXT NOT NULL);
                        INSERT INTO durable_intent_probe(id, payload) VALUES (1, $payload);
                        """;
                    setupCmd.Parameters.AddWithValue("$payload", marker);
                    await setupCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await AssertNoPlaintextMarkerAsync(dbPath, marker, cancellationToken);
                await AssertNoPlaintextMarkerAsync(dbPath + "-wal", marker, cancellationToken, allowMissing: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(createdKey);
            }

            checks.Add($"[PASS] libsecret 256-bit key + SQLite3MC {sqlite3mcVersion} WAL write (zero plaintext markers on disk)");

            // 3. Reopen database using key fetched back from libsecret
            var reloadedKey = await keyStore.LoadOrCreateAsync(cancellationToken);
            try
            {
                await using var conn = await OpenEncryptedAsync(dbPath, reloadedKey, cancellationToken);
                await using var readCmd = conn.CreateCommand();
                readCmd.CommandText = "SELECT payload FROM durable_intent_probe WHERE id = 1;";
                var readBack = Convert.ToString(await readCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                if (!string.Equals(readBack, marker, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Encrypted database reopen returned unexpected payload.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(reloadedKey);
            }

            checks.Add("[PASS] Encrypted DB reopen with key retrieved from Linux Secret Service");

            // 4. Wrong key must fail to read encrypted database
            var wrongKey = SHA256.HashData(Encoding.UTF8.GetBytes("fictional-wrong-linux-key-v1"));
            try
            {
                var rejected = false;
                try
                {
                    await using var wrongConn = await OpenEncryptedAsync(dbPath, wrongKey, cancellationToken);
                    await using var badRead = wrongConn.CreateCommand();
                    badRead.CommandText = "SELECT COUNT(*) FROM durable_intent_probe;";
                    _ = await badRead.ExecuteScalarAsync(cancellationToken);
                }
                catch (SqliteException)
                {
                    rejected = true;
                }

                if (!rejected)
                {
                    throw new InvalidOperationException("Wrong key unexpectedly succeeded on encrypted SQLite3MC database.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(wrongKey);
            }

            checks.Add("[PASS] Wrong 256-bit key rejected by SQLite3MC");

            // 5. Purge key while encrypted DB still exists -> LoadOrCreate must fail closed
            var cleared = keyStore.Purge();
            if (!cleared)
            {
                throw new InvalidOperationException("libsecret failed to clear check key.");
            }

            var failedClosed = false;
            try
            {
                var impossibleKey = await keyStore.LoadOrCreateAsync(cancellationToken);
                CryptographicOperations.ZeroMemory(impossibleKey);
            }
            catch (LocalDatabaseKeyUnavailableException)
            {
                failedClosed = true;
            }

            if (!failedClosed)
            {
                throw new InvalidOperationException("Missing keyring key for existing DB did not fail closed.");
            }

            checks.Add("[PASS] Existing encrypted DB with missing libsecret key fails closed (no replacement key / no plaintext fallback)");

            return new LinuxSecurityCheckReport(
                true,
                sqlite3mcVersion,
                clockAfter.BootSessionId,
                clockAfter.MonotonicElapsedMilliseconds,
                checks);
        }
        finally
        {
            try
            {
                keyStore.Purge();
            }
            catch
            {
                // Best-effort cleanup of check keyring entry.
            }

            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup of temp directory.
            }
        }
    }

    private static async Task<SqliteConnection> OpenEncryptedAsync(
        string databasePath,
        byte[] masterKey,
        CancellationToken cancellationToken)
    {
        var password = Convert.ToBase64String(masterKey);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            Password = password,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task AssertNoPlaintextMarkerAsync(
        string filePath,
        string marker,
        CancellationToken cancellationToken,
        bool allowMissing = false)
    {
        if (!File.Exists(filePath))
        {
            if (allowMissing)
            {
                return;
            }

            throw new FileNotFoundException("Expected encrypted SQLite file to exist.", filePath);
        }

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var needle = Encoding.UTF8.GetBytes(marker);
        if (bytes.AsSpan().IndexOf(needle) >= 0)
        {
            throw new InvalidOperationException($"Plaintext marker leaked into {filePath}!");
        }
    }
}
