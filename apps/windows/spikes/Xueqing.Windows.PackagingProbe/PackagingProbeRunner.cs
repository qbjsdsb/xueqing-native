using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Windows.ApplicationModel;
using Windows.Storage;
using Xueqing.Windows.LocalDataSecurity;

namespace Xueqing.Windows.PackagingProbe;

internal static class PackagingProbeRunner
{
    private const string KeyPurpose = "msix-packaging-probe/sqlite-master-key";

    public static async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var localFolder = ApplicationData.Current.LocalFolder.Path;
        Directory.CreateDirectory(localFolder);

        var wrappedKeyPath = Path.Combine(localFolder, "packaging-probe-key.bin");
        var databasePath = Path.Combine(localFolder, "packaging-probe.db");
        var reportPath = Path.Combine(localFolder, "packaging-probe.json");

        var protector = new WindowsDpapiCurrentUserProtector();
        var masterKey = await LoadOrCreateMasterKeyAsync(
            protector,
            wrappedKeyPath,
            cancellationToken);

        try
        {
            var password = Convert.ToBase64String(masterKey);
            await using var connection = Sqlite3McProbe.CreateConnection(databasePath, password);
            await connection.OpenAsync(cancellationToken);

            var sqlite3McVersion = await Sqlite3McProbe.GetVersionAsync(connection, cancellationToken);
            if (!string.Equals(
                    sqlite3McVersion,
                    "SQLite3 Multiple Ciphers 2.4.0",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected SQLite3MC runtime '{sqlite3McVersion}'.");
            }

            await ConfigureDatabaseAsync(connection, cancellationToken);
            var stableToken = await EnsureStableTokenAsync(connection, cancellationToken);

            var package = Package.Current;
            var version = package.Id.Version;
            var versionText = string.Create(
                CultureInfo.InvariantCulture,
                $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");

            var report = new PackagingProbeReport(
                package.Id.Name,
                package.Id.FamilyName,
                versionText,
                sqlite3McVersion,
                stableToken);

            var json = JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                });

            var temporaryPath = reportPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, reportPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    private static async Task<byte[]> LoadOrCreateMasterKeyAsync(
        WindowsDpapiCurrentUserProtector protector,
        string wrappedKeyPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(wrappedKeyPath))
        {
            var wrapped = await File.ReadAllBytesAsync(wrappedKeyPath, cancellationToken);
            try
            {
                return protector.Unprotect(wrapped, KeyPurpose);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(wrapped);
            }
        }

        var masterKey = RandomNumberGenerator.GetBytes(32);
        var protectedKey = protector.Protect(masterKey, KeyPurpose);
        try
        {
            await File.WriteAllBytesAsync(wrappedKeyPath, protectedKey, cancellationToken);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(masterKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
        }

        return masterKey;
    }

    private static async Task ConfigureDatabaseAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var journal = connection.CreateCommand())
        {
            journal.CommandText = "PRAGMA journal_mode = WAL;";
            var mode = Convert.ToString(
                await journal.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);

            if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SQLite3MC packaging probe refused WAL mode; actual mode is '{mode ?? "<null>"}'.");
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA synchronous = FULL;

            CREATE TABLE IF NOT EXISTS probe_state (
                id INTEGER PRIMARY KEY CHECK(id = 1),
                stable_token TEXT NOT NULL
            ) STRICT;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> EnsureStableTokenAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT OR IGNORE INTO probe_state(id, stable_token)
                VALUES (1, $stable_token);
                """;
            insert.Parameters.AddWithValue("$stable_token", Guid.NewGuid().ToString("D"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT stable_token FROM probe_state WHERE id = 1;";
        return Convert.ToString(
            await read.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("Packaging probe stable token was not persisted.");
    }

    private sealed record PackagingProbeReport(
        string PackageName,
        string PackageFamilyName,
        string PackageVersion,
        string Sqlite3McVersion,
        string StableToken);
}
