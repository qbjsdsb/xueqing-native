using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.LocalData;

namespace Xueqing.Linux.Infrastructure.LocalData;

/// <summary>
/// Deterministic UUID and scope derivation shared between Xueqing.Linux (--prototype)
/// and Xueqing.Mcp (--prototype) so local AI draft handoff works end-to-end over
/// the real encrypted SQLite3MC + libsecret store.
/// </summary>
public static class PrototypeDraftIdentity
{
    public static readonly Guid OrganizationId = DeterministicGuid("prototype-org");
    public static readonly Guid SubjectProfileId = DeterministicGuid("prototype-subject-profile");
    public static readonly Guid AssignmentId = DeterministicGuid("prototype-assignment");

    public static Guid DeterministicGuid(string seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("xueqing-mcp-v1/" + seed));
        var bytes = new byte[16];
        Array.Copy(hash, 0, bytes, 0, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    public static Guid ResolveStudentUuid(string studentIdOrKey) =>
        Guid.TryParse(studentIdOrKey, out var parsed) && parsed != Guid.Empty
            ? parsed
            : DeterministicGuid("prototype-student/" + studentIdOrKey);

    public static ObservationDraftScope CreateScopeForStudent(string studentIdOrKey) =>
        new(
            OrganizationId,
            ResolveStudentUuid(studentIdOrKey),
            SubjectProfileId,
            AssignmentId);
}

[SupportedOSPlatform("linux")]
public static class LinuxLocalStatePaths
{
    private const UnixFileMode OwnerDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

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
        EnsureOwnerDirectory(root);
        return root;
    }

    public static Guid LoadOrCreateInstallationId(string? appDataRoot = null)
    {
        var root = ResolveRoot(appDataRoot);
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

    public static string GetPrototypeMcpDraftDatabasePath(string? appDataRoot = null)
    {
        var root = ResolveRoot(appDataRoot);
        var dir = Path.Combine(root, "mcp-prototype");
        EnsureOwnerDirectory(dir);
        return Path.Combine(dir, "observation-draft.db");
    }

    public static string GetDurableIntentDatabasePath(
        string environmentId,
        string appUserId,
        string organizationId,
        Guid installationId,
        string? appDataRoot = null) =>
        GetScopedDatabasePath(environmentId, appUserId, organizationId, installationId, "durable-intent.db", appDataRoot);

    public static string GetObservationDraftDatabasePath(
        string environmentId,
        string appUserId,
        string organizationId,
        Guid installationId,
        string? appDataRoot = null) =>
        GetScopedDatabasePath(environmentId, appUserId, organizationId, installationId, "observation-draft.db", appDataRoot);

    public static string GetCreateObservationRecoveryDatabasePath(
        string environmentId,
        string appUserId,
        string organizationId,
        Guid installationId,
        string? appDataRoot = null) =>
        GetScopedDatabasePath(environmentId, appUserId, organizationId, installationId, "create-observation-recovery.db", appDataRoot);

    public static string GetOnlineCommandRecoveryDatabasePath(
        string environmentId,
        string appUserId,
        string organizationId,
        Guid installationId,
        string? appDataRoot = null) =>
        GetScopedDatabasePath(environmentId, appUserId, organizationId, installationId, "online-command-recovery.db", appDataRoot);

    public static string GetActionProgressionRecoveryDatabasePath(
        string environmentId,
        string appUserId,
        string organizationId,
        Guid installationId,
        string? appDataRoot = null) =>
        GetScopedDatabasePath(environmentId, appUserId, organizationId, installationId, "action-progression-recovery.db", appDataRoot);

    public static string GetCaseLifecycleRecoveryDatabasePath(
        string environmentId,
        string appUserId,
        string organizationId,
        Guid installationId,
        string? appDataRoot = null) =>
        GetScopedDatabasePath(environmentId, appUserId, organizationId, installationId, "case-lifecycle-recovery.db", appDataRoot);

    public static string GetOrganizationInvitationRecoveryDatabasePath(
        string environmentId,
        string appUserId,
        string organizationId,
        Guid installationId,
        string? appDataRoot = null) =>
        GetScopedDatabasePath(environmentId, appUserId, organizationId, installationId, "organization-invitation-recovery.db", appDataRoot);

    public static string GetOnlineCommandRecoveryIndexDatabasePath(
        string environmentId,
        string appUserId,
        Guid installationId,
        string? appDataRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserId);
        if (installationId == Guid.Empty)
        {
            throw new ArgumentException("Installation id must be non-empty.", nameof(installationId));
        }

        var root = ResolveRoot(appDataRoot);
        var scopeDir = Path.Combine(
            root,
            "scoped-local-data",
            "v1",
            "actor",
            WindowsLocalScopeIdentity.ComputeActorScopeDirectoryName(environmentId, appUserId),
            installationId.ToString("N"));
        EnsureOwnerDirectory(scopeDir);
        return Path.Combine(scopeDir, "online-command-recovery-index.db");
    }

    public static string GetScopedDatabasePath(
        string environmentId,
        string appUserId,
        string organizationId,
        Guid installationId,
        string fileName,
        string? appDataRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (installationId == Guid.Empty)
        {
            throw new ArgumentException("Installation id must be non-empty.", nameof(installationId));
        }

        var root = ResolveRoot(appDataRoot);
        var scopeDir = Path.Combine(
            root,
            "scoped-local-data",
            "v1",
            WindowsLocalScopeIdentity.ComputeOrganizationScopeDirectoryName(
                environmentId,
                appUserId,
                organizationId),
            installationId.ToString("N"));
        EnsureOwnerDirectory(scopeDir);
        return Path.Combine(scopeDir, fileName);
    }

    private static string ResolveRoot(string? appDataRoot)
    {
        var root = string.IsNullOrWhiteSpace(appDataRoot)
            ? GetAppDataRoot()
            : Path.GetFullPath(appDataRoot);
        EnsureOwnerDirectory(root);
        return root;
    }

    private static void EnsureOwnerDirectory(string path)
    {
        var existed = Directory.Exists(path);
        Directory.CreateDirectory(path);
        if (!existed)
        {
            try
            {
                File.SetUnixFileMode(path, OwnerDirectoryMode);
            }
            catch
            {
                // Best-effort permission tightening on filesystems that support POSIX modes.
            }
        }
    }
}
