using Xueqing.Windows.Core.Services;
using Windows.Storage;
using Xueqing.Windows.Infrastructure.Remote;
using Xueqing.Windows.LocalData;

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

        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };

        ValueTask<string?> AccessTokenProvider(CancellationToken _) =>
            ValueTask.FromResult<string?>(accessToken);

        var bootstrapReader = new PostgrestPersonalBootstrapReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var recentReader = new PostgrestStudentRecentObservationsReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var focusReader = new PostgrestStudentLearningFocusReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var todayReader = new PostgrestPersonalTodayActionsReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var createLearningCase = new PostgrestCreateLearningCaseCommand(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var createLearningCaseRecovery = new WindowsCreateLearningCaseRecoveryStore(
            projectUri.GetLeftPart(UriPartial.Authority),
            ApplicationData.Current);

        var actionProgressionRecovery = new WindowsActionProgressionRecoveryStore(
            projectUri.GetLeftPart(UriPartial.Authority),
            ApplicationData.Current);
        var actionProgression = new ActionProgressionCommandCoordinator(
            new PostgrestReschedulePrimaryActionCommand(
                httpClient,
                projectUri,
                apiKey,
                AccessTokenProvider),
            new PostgrestRecordVerificationAndNextActionCommand(
                httpClient,
                projectUri,
                apiKey,
                AccessTokenProvider),
            actionProgressionRecovery);

        return new PersonalTeachingWorkspaceServices(
            new PersonalStudentWorkspaceCoordinator(
                bootstrapReader,
                new StudentRecentObservationsCoordinator(recentReader)),
            new StudentLearningFocusCoordinator(focusReader),
            new PersonalTodayActionsCoordinator(todayReader),
            createLearningCase,
            createLearningCaseRecovery,
            actionProgression,
            actionProgressionRecovery);
    }
}
