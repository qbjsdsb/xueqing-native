using Windows.Storage;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Windows.LocalData;

internal static class WindowsInstallationIdentity
{
    private const string InstallationIdSetting = "xueqing.local-state.installation-id.v1";

    public static Guid LoadOrCreate(ApplicationDataContainer localSettings)
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

internal sealed record WindowsActorLocalDataScope(
    string EnvironmentId,
    string AppUserId,
    Guid InstallationId)
{
    public static WindowsActorLocalDataScope Create(
        ApplicationData applicationData,
        string environmentId,
        string appUserId)
    {
        ArgumentNullException.ThrowIfNull(applicationData);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserId);

        return new WindowsActorLocalDataScope(
            environmentId,
            appUserId,
            WindowsInstallationIdentity.LoadOrCreate(applicationData.LocalSettings));
    }
}

internal sealed record WindowsLocalDataScope(
    string EnvironmentId,
    string AppUserId,
    string OrganizationId,
    Guid InstallationId)
{
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
            WindowsInstallationIdentity.LoadOrCreate(applicationData.LocalSettings));
    }

    public static WindowsLocalDataScope CreateFictionalIntegrationScope(
        ApplicationData applicationData) =>
        Create(
            applicationData,
            "integration",
            "user-fictional-001",
            "org-fictional-001");

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

    public static string GetOnlineCommandRecoveryIndexDatabasePath(
        StorageFolder localFolder,
        WindowsActorLocalDataScope scope)
    {
        ArgumentNullException.ThrowIfNull(localFolder);
        ArgumentNullException.ThrowIfNull(scope);
        ValidateActorScope(scope);

        var scopeDirectory = Path.Combine(
            localFolder.Path,
            "scoped-local-data",
            "v1",
            "actor",
            WindowsLocalScopeIdentity.ComputeActorScopeDirectoryName(
                scope.EnvironmentId,
                scope.AppUserId),
            scope.InstallationId.ToString("N"));

        Directory.CreateDirectory(scopeDirectory);
        return Path.Combine(scopeDirectory, "online-command-recovery-index.db");
    }

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
            WindowsLocalScopeIdentity.ComputeOrganizationScopeDirectoryName(
                scope.EnvironmentId,
                scope.AppUserId,
                scope.OrganizationId),
            scope.InstallationId.ToString("N"));

        Directory.CreateDirectory(scopeDirectory);
        return Path.Combine(scopeDirectory, fileName);
    }

    public static IReadOnlyList<string> EnumerateLegacyOnlineCommandRecoveryDatabasePaths(
        StorageFolder localFolder,
        Guid installationId)
    {
        ArgumentNullException.ThrowIfNull(localFolder);
        if (installationId == Guid.Empty)
        {
            throw new ArgumentException("Installation id must be non-empty.", nameof(installationId));
        }

        var root = Path.Combine(localFolder.Path, "scoped-local-data", "v1");
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        var installationDirectoryName = installationId.ToString("N");
        var paths = new List<string>();
        foreach (var scopeDirectory in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(
                    Path.GetFileName(scopeDirectory),
                    "actor",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var installationDirectory = Path.Combine(
                scopeDirectory,
                installationDirectoryName);
            var databasePath = Path.Combine(
                installationDirectory,
                "online-command-recovery.db");

            if (File.Exists(databasePath))
            {
                paths.Add(Path.GetFullPath(databasePath));
            }
        }

        paths.Sort(StringComparer.OrdinalIgnoreCase);
        return paths;
    }

    public static bool MatchesOrganizationRecoveryScope(
        string databasePath,
        WindowsLocalDataScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(scope);
        ValidateScope(scope);

        var installationDirectory = Directory.GetParent(Path.GetFullPath(databasePath));
        var scopeDirectory = installationDirectory?.Parent;
        if (installationDirectory is null || scopeDirectory is null)
        {
            return false;
        }

        if (!string.Equals(
                installationDirectory.Name,
                scope.InstallationId.ToString("N"),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedScopeDirectory =
            WindowsLocalScopeIdentity.ComputeOrganizationScopeDirectoryName(
                scope.EnvironmentId,
                scope.AppUserId,
                scope.OrganizationId);

        return string.Equals(
            scopeDirectory.Name,
            expectedScopeDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateActorScope(WindowsActorLocalDataScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.EnvironmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.AppUserId);
        if (scope.InstallationId == Guid.Empty)
        {
            throw new ArgumentException("Installation id must be non-empty.", nameof(scope));
        }
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


}
