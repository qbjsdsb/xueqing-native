using Windows.Storage;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Integration;

internal static class LocalReferenceProviderTeachingWorkspaceFactory
{
    private const string UrlVariable = "XUEQING_LOCAL_REFERENCE_PROVIDER_URL";
    private const string ApiKeyVariable = "XUEQING_LOCAL_REFERENCE_API_KEY";
    private const string AccessTokenVariable = "XUEQING_LOCAL_REFERENCE_ACCESS_TOKEN";

    public static PersonalTeachingWorkspaceServices? CreateFromEnvironment()
    {
        var providerUrl = Environment.GetEnvironmentVariable(UrlVariable);
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        var accessToken = Environment.GetEnvironmentVariable(AccessTokenVariable);

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

        return ProviderTeachingWorkspaceBuilder.Create(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider,
            projectUri.GetLeftPart(UriPartial.Authority),
            ApplicationData.Current);
    }
}
