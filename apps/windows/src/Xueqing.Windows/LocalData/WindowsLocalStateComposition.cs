using System.Security.Cryptography;
using System.Text;
using Windows.Storage;

namespace Xueqing.Windows.LocalData;

internal sealed record WindowsLocalDataScope(
    string EnvironmentId,
    string AppUserId,
    string OrganizationId,
    Guid InstallationId)
{
    private const string InstallationIdSetting = "xueqing.local-state.installation-id.v1";

    public static WindowsLocalDataScope Create(
        ApplicationData applicationData,
        string environmentId,
        string appUserId,
        string organizationId)
    {
        ArgumentNullException.ThrowIfNull(applicationData);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);

        return new WindowsLocalDataScope(
            environmentId,
            appUserId,
            organizationId,
            LoadOrCreateInstallationId(applicationData.LocalSettings));
    }

    public static WindowsLocalDataScope CreateFictionalIntegrationScope(
        ApplicationData applicationData) =>
        Create(
            applicationData,
            "integration",
            "user-fictional-001",
            "org-fictional-001");

    private static Guid LoadOrCreateInstallationId(ApplicationDataContainer localSettings)
    {
        if (localSettings.Values.TryGetValue(InstallationIdSetting, out var existing)
            && existing is string existingText
            && Guid.TryParse(existingText, out var parsed)
            && parsed != Guid.Empty)
        {
            return parsed;
        }

        var created = Guid.NewGuid();
        localSettings.Values[InstallationIdSetting] = created.ToString("D");
        return created;
    }
}

internal static class WindowsLocalStatePaths
{
    public static string GetDurableIntentDatabasePath(
        StorageFolder localFolder,
        WindowsLocalDataScope scope) =>
        GetScopedDatabasePath(localFolder, scope, "durable-intent.db");

    public static string GetOnlineCommandRecoveryDatabasePath(
        StorageFolder localFolder,
        WindowsLocalDataScope scope) =>
        GetScopedDatabasePath(localFolder, scope, "online-command-recovery.db");

    private static string GetScopedDatabasePath(
        StorageFolder localFolder,
        WindowsLocalDataScope scope,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(localFolder);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ValidateScope(scope);

        var scopeDirectory = Path.Combine(
            localFolder.Path,
            "scoped-local-data",
            "v1",
            HashScope(scope),
            scope.InstallationId.ToString("N"));

        Directory.CreateDirectory(scopeDirectory);
        return Path.Combine(scopeDirectory, fileName);
    }

    private static void ValidateScope(WindowsLocalDataScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.EnvironmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.AppUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.OrganizationId);
        if (scope.InstallationId == Guid.Empty)
        {
            throw new ArgumentException("Installation id must be non-empty.", nameof(scope));
        }
    }

    private static string HashScope(WindowsLocalDataScope scope)
    {
        var environmentHash = HashSegment(scope.EnvironmentId);
        var appUserHash = HashSegment(scope.AppUserId);
        var organizationHash = HashSegment(scope.OrganizationId);

        return HashSegment(string.Concat(environmentHash, appUserHash, organizationHash));
    }

    private static string HashSegment(string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        try
        {
            var hash = SHA256.HashData(utf8);
            try
            {
                return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }
}
