using System.Runtime.Versioning;
using Xueqing.Linux.Infrastructure.Security;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Linux.Infrastructure.LocalData;

public sealed record LinuxLocalDataPurgeReport(
    int DatabasesPurged,
    int FilesRemoved,
    int KeySlotsPurged);

/// <summary>
/// Explicit, scope-bounded purge utility for Linux encrypted SQLite databases,
/// SQLite WAL/SHM/journal sidecars, and their associated Linux Secret Service
/// master key slots.
///
/// Per docs/architecture/LOCAL_DATA_SECURITY.md, this must only be invoked for
/// explicit user-confirmed local data deletion or disposable cache resets,
/// never silently during ordinary session expiry.
/// </summary>
[SupportedOSPlatform("linux")]
public static class LinuxLocalDataPurger
{
    private static readonly string[] SidecarSuffixes = ["-wal", "-shm", "-journal"];

    public static LinuxLocalDataPurgeReport PurgeOrganizationScope(
        string environmentId,
        string appUserId,
        string organizationId,
        Guid installationId,
        string? appDataRoot = null,
        Func<string, bool>? purgeKeyForDatabasePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        if (installationId == Guid.Empty)
        {
            throw new ArgumentException("Installation id must be non-empty.", nameof(installationId));
        }

        var root = string.IsNullOrWhiteSpace(appDataRoot)
            ? LinuxLocalStatePaths.GetAppDataRoot()
            : Path.GetFullPath(appDataRoot);

        var scopeDir = Path.Combine(
            root,
            "scoped-local-data",
            "v1",
            WindowsLocalScopeIdentity.ComputeOrganizationScopeDirectoryName(
                environmentId,
                appUserId,
                organizationId),
            installationId.ToString("N"));

        return PurgeDatabaseDirectory(scopeDir, purgeKeyForDatabasePath);
    }

    public static LinuxLocalDataPurgeReport PurgeDatabaseFile(
        string databasePath,
        Func<string, bool>? purgeKeyForDatabasePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        var keyPurger = purgeKeyForDatabasePath ?? DefaultPurgeSecretServiceKey;
        var filesRemoved = 0;
        var dbExisted = File.Exists(fullPath);

        if (dbExisted)
        {
            File.Delete(fullPath);
            filesRemoved++;
        }

        foreach (var suffix in SidecarSuffixes)
        {
            var sidecar = fullPath + suffix;
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
                filesRemoved++;
            }
        }

        var keyPurged = keyPurger(fullPath) ? 1 : 0;
        return new LinuxLocalDataPurgeReport(
            DatabasesPurged: dbExisted ? 1 : 0,
            FilesRemoved: filesRemoved,
            KeySlotsPurged: keyPurged);
    }

    private static LinuxLocalDataPurgeReport PurgeDatabaseDirectory(
        string scopeDirectory,
        Func<string, bool>? purgeKeyForDatabasePath)
    {
        if (!Directory.Exists(scopeDirectory))
        {
            return new LinuxLocalDataPurgeReport(0, 0, 0);
        }

        var keyPurger = purgeKeyForDatabasePath ?? DefaultPurgeSecretServiceKey;
        var dbCount = 0;
        var fileCount = 0;
        var keyCount = 0;

        foreach (var dbFile in Directory.EnumerateFiles(scopeDirectory, "*.db", SearchOption.TopDirectoryOnly))
        {
            var report = PurgeDatabaseFile(dbFile, keyPurger);
            dbCount += report.DatabasesPurged;
            fileCount += report.FilesRemoved;
            keyCount += report.KeySlotsPurged;
        }

        // Remove empty directory if nothing remains inside
        if (!Directory.EnumerateFileSystemEntries(scopeDirectory).Any())
        {
            Directory.Delete(scopeDirectory, recursive: false);
        }

        return new LinuxLocalDataPurgeReport(dbCount, fileCount, keyCount);
    }

    private static bool DefaultPurgeSecretServiceKey(string fullDatabasePath) =>
        new LinuxSecretServiceDatabaseKeyStore(fullDatabasePath).Purge();
}
