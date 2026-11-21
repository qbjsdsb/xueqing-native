using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xueqing.Linux.Infrastructure.LocalData;
using Xueqing.Linux.Infrastructure.Security;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Auth;
using Xueqing.Windows.Infrastructure.Deployment;
using Xueqing.Windows.Infrastructure.LocalData;
using Xueqing.Windows.Infrastructure.Remote;
using Xueqing.Windows.Infrastructure.Support;

namespace Xueqing.Linux.Infrastructure.Integration;

[SupportedOSPlatform("linux")]
public sealed class LinuxProductionClientRuntime : IDisposable
{
    public const string ProfileFileName = "deployment_profile.json";
    public const string LinuxPlatformName = "linux";
    public const string PackageIdentity = "com.xueqing.app";
    public const string DefaultAppVersion = "1.0.0-rc.1";
    public const int ClientContractVersion = 1;
    public const int LocalSchemaVersion = 1;

    private const string LocalRefUrlVariable = "XUEQING_LOCAL_REFERENCE_PROVIDER_URL";
    private const string LocalRefApiKeyVariable = "XUEQING_LOCAL_REFERENCE_API_KEY";
    private const string LocalRefAccessTokenVariable = "XUEQING_LOCAL_REFERENCE_ACCESS_TOKEN";

    private readonly HttpClient _httpClient;
    private readonly string _appDataRoot;
    private readonly Func<string, ILocalDatabaseKeyStore>? _keyStoreFactory;

    private LinuxProductionClientRuntime(
        DeploymentProfile profile,
        HttpClient httpClient,
        string appDataRoot,
        ProviderSessionTokenSource tokenSource,
        ProviderAuthCoordinator authCoordinator,
        Func<string, ILocalDatabaseKeyStore>? keyStoreFactory)
    {
        Profile = profile;
        _httpClient = httpClient;
        _appDataRoot = appDataRoot;
        TokenSource = tokenSource;
        AuthCoordinator = authCoordinator;
        _keyStoreFactory = keyStoreFactory;
    }

    public DeploymentProfile Profile { get; }
    public ProviderSessionTokenSource TokenSource { get; }
    public ProviderAuthCoordinator AuthCoordinator { get; }

    public static LinuxProductionClientRuntime Create(
        string? profilePath = null,
        string? appDataRoot = null,
        IRefreshTokenVault? refreshTokenVaultOverride = null,
        Func<string, ILocalDatabaseKeyStore>? keyStoreFactory = null)
    {
        var resolvedProfilePath = string.IsNullOrWhiteSpace(profilePath)
            ? Path.Combine(AppContext.BaseDirectory, ProfileFileName)
            : Path.GetFullPath(profilePath);

        if (!File.Exists(resolvedProfilePath))
        {
            throw new FileNotFoundException(
                "Production deployment profile is not present in the installed Linux application.",
                resolvedProfilePath);
        }

        var profile = DeploymentProfileParser.Parse(File.ReadAllText(resolvedProfilePath));
        if (!string.Equals(profile.ProviderId, "supabase", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The installed production provider is not supported by this Linux composition root.");
        }

        var resolvedDataRoot = string.IsNullOrWhiteSpace(appDataRoot)
            ? LinuxLocalStatePaths.GetAppDataRoot()
            : Path.GetFullPath(appDataRoot);

        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var tokenSource = new ProviderSessionTokenSource(profile.EnvironmentId, profile.TrustDomainId);
        var refreshTokenVault = refreshTokenVaultOverride
            ?? new LinuxSecretServiceRefreshTokenVault(profile.EnvironmentId, profile.TrustDomainId);
        var authTransport = new SupabaseAuthTransport(
            httpClient,
            profile.ProjectOrigin,
            profile.PublishableKey);
        var authCoordinator = new ProviderAuthCoordinator(tokenSource, refreshTokenVault, authTransport);

        return new LinuxProductionClientRuntime(
            profile,
            httpClient,
            resolvedDataRoot,
            tokenSource,
            authCoordinator,
            keyStoreFactory);
    }

    public static PersonalTeachingWorkspaceServices? TryCreateLocalReferenceWorkspaceFromEnvironment(
        string? appDataRoot = null)
    {
        var providerUrl = Environment.GetEnvironmentVariable(LocalRefUrlVariable);
        var apiKey = Environment.GetEnvironmentVariable(LocalRefApiKeyVariable);
        var accessToken = Environment.GetEnvironmentVariable(LocalRefAccessTokenVariable);

        if (string.IsNullOrWhiteSpace(providerUrl) &&
            string.IsNullOrWhiteSpace(apiKey) &&
            string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(providerUrl) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "Local reference-provider mode requires URL, API key and access token together.");
        }

        if (!Uri.TryCreate(providerUrl, UriKind.Absolute, out var projectUri) ||
            !projectUri.IsLoopback ||
            (projectUri.Scheme != Uri.UriSchemeHttp && projectUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Local reference-provider mode is restricted to an explicit loopback URL and cannot connect to remote environments.");
        }

        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ValueTask<string?> AccessTokenProvider(CancellationToken _) =>
            ValueTask.FromResult<string?>(accessToken);

        return LinuxTeachingWorkspaceBuilder.Create(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider,
            DevelopmentAllowAllConsequentialWriteCompatibilityGate.Instance,
            projectUri.GetLeftPart(UriPartial.Authority),
            appDataRoot);
    }

    public PersonalTeachingWorkspaceServices CreateTeachingWorkspace()
    {
        if (TokenSource.Snapshot.Status != ProviderSessionStatus.Usable)
        {
            throw new InvalidOperationException(
                "Production teaching workspace requires a usable provider session.");
        }

        var compatibilityGate = new ServerClientCompatibilityWriteGate(
            new PostgrestClientCompatibilityReader(
                _httpClient,
                Profile.ProjectOrigin,
                Profile.PublishableKey,
                TokenSource.GetCurrentAccessTokenAsync),
            new ClientCompatibilityRequest(
                LinuxPlatformName,
                DefaultAppVersion,
                ClientContractVersion));

        return LinuxTeachingWorkspaceBuilder.Create(
            _httpClient,
            Profile.ProjectOrigin,
            Profile.PublishableKey,
            TokenSource.GetCurrentAccessTokenAsync,
            compatibilityGate,
            Profile.EnvironmentId,
            _appDataRoot,
            Profile.RequiredEdgeRegion,
            _keyStoreFactory);
    }

    public DiagnosticsSnapshot BuildDiagnosticsSnapshot()
    {
        var sessionState = TokenSource.Snapshot.Status switch
        {
            ProviderSessionStatus.SignedOut => DiagnosticSessionState.SignedOut,
            ProviderSessionStatus.Usable => DiagnosticSessionState.Authenticated,
            ProviderSessionStatus.RefreshRequired => DiagnosticSessionState.RefreshRequired,
            ProviderSessionStatus.Invalid => DiagnosticSessionState.RevokedOrInvalid,
            _ => DiagnosticSessionState.ConfigurationUnavailable,
        };

        return new DiagnosticsSnapshot(
            DateTimeOffset.UtcNow,
            ReadLinuxOsDisplayVersion(),
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            PackageIdentity,
            DefaultAppVersion,
            Profile.Source.Commit,
            ClientContractVersion,
            LocalSchemaVersion,
            new DiagnosticDeployment(
                Profile.ProfileId,
                Profile.EnvironmentId,
                Profile.TrustDomainId,
                Profile.ProviderId),
            sessionState,
            DiagnosticSyncCategory.Unknown,
            new DiagnosticCompatibility(
                DiagnosticCompatibilityState.Unknown,
                null,
                null),
            new DiagnosticQueueCounts(null, null, null),
            Array.Empty<string>(),
            PlatformKind: LinuxPlatformName);
    }

    public byte[] CreateDiagnosticsArchive()
    {
        using var output = new MemoryStream();
        DiagnosticsArchiveWriter.Write(BuildDiagnosticsSnapshot(), output);
        return output.ToArray();
    }

    public static string ReadLinuxOsDisplayVersion()
    {
        try
        {
            if (File.Exists("/etc/os-release"))
            {
                foreach (var line in File.ReadLines("/etc/os-release"))
                {
                    if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                    {
                        var value = line["PRETTY_NAME=".Length..].Trim().Trim('"');
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall back to runtime OS version string.
        }

        return Environment.OSVersion.VersionString;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
