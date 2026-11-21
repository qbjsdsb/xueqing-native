using System.Runtime.Versioning;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Linux.Infrastructure.LocalData;

[SupportedOSPlatform("linux")]
public static class LinuxLocalStatePaths
{
    public static string GetAppDataRoot()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var baseDir = !string.IsNullOrWhiteSpace(xdg)
            ? xdg
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share");
        var root = Path.Combine(Path.GetFullPath(baseDir), "xueqing");
        Directory.CreateDirectory(root);
        return root;
    }

    public static Guid LoadOrCreateInstallationId(string? appDataRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(appDataRoot)
            ? GetAppDataRoot()
            : Path.GetFullPath(appDataRoot);
        Directory.CreateDirectory(root);

        var idPath = Path.Combine(root, "installation-id.v1");
        if (File.Exists(idPath))
        {
            var raw = File.ReadAllText(idPath).Trim();
            if (Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty)
            {
                return parsed;
            }
        }

        var created = Guid.NewGuid();
        var tmp = idPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, created.ToString("D") + "\n");
        File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, idPath, overwrite: true);
        return created;
    }

    public static string GetObservationDraftDatabasePath(
        string environmentId,
        string appUserId,
        string organizationId,
        Guid installationId,
        string? appDataRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(appDataRoot)
            ? GetAppDataRoot()
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

        Directory.CreateDirectory(scopeDir);
        return Path.Combine(scopeDir, "observation-draft.db");
    }
}
