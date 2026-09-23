using System.Runtime.InteropServices;
using Windows.Storage;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Auth;
using Xueqing.Windows.Infrastructure.Deployment;
using Xueqing.Windows.Infrastructure.Remote;
using Xueqing.Windows.Infrastructure.Support;

namespace Xueqing.Windows.Integration;

internal sealed class ProductionProviderClientRuntime
{
    private const string ProfileFileName = "deployment_profile.json";
    private const int ClientContractVersion = 1;
    private readonly HttpClient _httpClient;
    private readonly ApplicationData _applicationData;

    private ProductionProviderClientRuntime(
        DeploymentProfile profile,
        HttpClient httpClient,
        ApplicationData applicationData,
        ProviderSessionTokenSource tokenSource,
        ProviderAuthCoordinator authCoordinator)
    {
        Profile = profile;
        _httpClient = httpClient;
        _applicationData = applicationData;
        TokenSource = tokenSource;
        AuthCoordinator = authCoordinator;
    }

    public DeploymentProfile Profile { get; }
    public ProviderSessionTokenSource TokenSource { get; }
    public ProviderAuthCoordinator AuthCoordinator { get; }

    public static ProductionProviderClientRuntime Create()
    {
        var profilePath = Path.Combine(AppContext.BaseDirectory, ProfileFileName);
        if (!File.Exists(profilePath))
        {
            throw new FileNotFoundException(
                "Production deployment profile is not present in the installed application.",
                profilePath);
        }

        var profile = DeploymentProfileParser.Parse(File.ReadAllText(profilePath));
        if (!string.Equals(profile.ProviderId, "supabase", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The installed production provider is not supported by this composition root.");
        }

        var applicationData = ApplicationData.Current;
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var tokenSource = new ProviderSessionTokenSource(profile.EnvironmentId, profile.TrustDomainId);
        var refreshTokenVault = new WindowsDpapiRefreshTokenVault(
            Path.Combine(applicationData.LocalFolder.Path, "auth"),
            profile.EnvironmentId,
            profile.TrustDomainId);
        var authTransport = new SupabaseAuthTransport(
            httpClient,
            profile.ProjectOrigin,
            profile.PublishableKey);
        var authCoordinator = new ProviderAuthCoordinator(tokenSource, refreshTokenVault, authTransport);

        return new ProductionProviderClientRuntime(
            profile, httpClient, applicationData, tokenSource, authCoordinator);
    }

    public byte[] CreateDiagnosticsArchive()
    {
        var package = global::Windows.ApplicationModel.Package.Current;
        var sessionState = TokenSource.Snapshot.Status switch
        {
            ProviderSessionStatus.SignedOut => DiagnosticSessionState.SignedOut,
            ProviderSessionStatus.Usable => DiagnosticSessionState.Authenticated,
            ProviderSessionStatus.RefreshRequired => DiagnosticSessionState.RefreshRequired,
            ProviderSessionStatus.Invalid => DiagnosticSessionState.RevokedOrInvalid,
            _ => DiagnosticSessionState.ConfigurationUnavailable,
        };

        var snapshot = new DiagnosticsSnapshot(
            DateTimeOffset.UtcNow,
            Environment.OSVersion.VersionString,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            package.Id.Name,
            InstalledPackageVersion(),
            Profile.Source.Commit,
            ClientContractVersion,
            1,
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
            Array.Empty<string>());

        using var output = new MemoryStream();
        DiagnosticsArchiveWriter.Write(snapshot, output);
        return output.ToArray();
    }

    private static string InstalledPackageVersion()
    {
        var version = global::Windows.ApplicationModel.Package.Current.Id.Version;
        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }

    public PersonalTeachingWorkspaceServices CreateTeachingWorkspace()
    {
        if (TokenSource.Snapshot.Status != ProviderSessionStatus.Usable)
        {
            throw new InvalidOperationException(
                "Production teaching workspace requires a usable provider session.");
        }

        var appVersion = InstalledPackageVersion();
        var compatibilityGate = new ServerClientCompatibilityWriteGate(
            new PostgrestClientCompatibilityReader(
                _httpClient,
                Profile.ProjectOrigin,
                Profile.PublishableKey,
                TokenSource.GetCurrentAccessTokenAsync),
            new ClientCompatibilityRequest(
                "windows",
                appVersion,
                ClientContractVersion));

        return ProviderTeachingWorkspaceBuilder.Create(
            _httpClient,
            Profile.ProjectOrigin,
            Profile.PublishableKey,
            TokenSource.GetCurrentAccessTokenAsync,
            compatibilityGate,
            Profile.EnvironmentId,
            _applicationData,
            Profile.RequiredEdgeRegion);
    }
}
