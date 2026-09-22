using Windows.Storage;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Remote;
using Xueqing.Windows.LocalData;

namespace Xueqing.Windows.Integration;

internal static class ProviderTeachingWorkspaceBuilder
{
    public static PersonalTeachingWorkspaceServices Create(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider,
        string environmentId,
        ApplicationData applicationData,
        string? functionRegion = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(projectUri);
        ArgumentNullException.ThrowIfNull(accessTokenProvider);
        ArgumentNullException.ThrowIfNull(applicationData);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);

        var bootstrapReader = new PostgrestPersonalBootstrapReader(httpClient, projectUri, apiKey, accessTokenProvider);
        var organizationManagementReader = new PostgrestOrganizationManagementReader(httpClient, projectUri, apiKey, accessTokenProvider);
        var organizationInvitationCommand = new PostgrestOrganizationInvitationCommand(httpClient, projectUri, apiKey, accessTokenProvider);
        var organizationInvitationAcceptance = new PostgrestAcceptOrganizationInvitationCommand(httpClient, projectUri, apiKey, accessTokenProvider);
        var organizationInvitationDelivery = new SupabaseOrganizationInvitationDeliveryCommand(
            httpClient, projectUri, apiKey, accessTokenProvider, functionRegion);
        var organizationInvitationRecovery = new WindowsOrganizationInvitationRecoveryStore(environmentId, applicationData);
        var recentReader = new PostgrestStudentRecentObservationsReader(httpClient, projectUri, apiKey, accessTokenProvider);
        var focusReader = new PostgrestStudentLearningFocusReader(httpClient, projectUri, apiKey, accessTokenProvider);
        var caseHistoryReader = new PostgrestStudentLearningCasesReader(httpClient, projectUri, apiKey, accessTokenProvider);
        var todayReader = new PostgrestPersonalTodayActionsReader(httpClient, projectUri, apiKey, accessTokenProvider);
        var createLearningCase = new PostgrestCreateLearningCaseCommand(httpClient, projectUri, apiKey, accessTokenProvider);
        var createLearningCaseRecovery = new WindowsCreateLearningCaseRecoveryStore(environmentId, applicationData);

        var actionProgressionRecovery = new WindowsActionProgressionRecoveryStore(environmentId, applicationData);
        var actionProgression = new ActionProgressionCommandCoordinator(
            new PostgrestReschedulePrimaryActionCommand(httpClient, projectUri, apiKey, accessTokenProvider),
            new PostgrestRecordVerificationAndNextActionCommand(httpClient, projectUri, apiKey, accessTokenProvider),
            actionProgressionRecovery);

        var caseLifecycleRecovery = new WindowsCaseLifecycleRecoveryStore(environmentId, applicationData);
        var caseLifecycle = new CaseLifecycleCommandCoordinator(
            new PostgrestTransitionLearningCaseStateCommand(httpClient, projectUri, apiKey, accessTokenProvider),
            new PostgrestCloseLearningCaseCommand(httpClient, projectUri, apiKey, accessTokenProvider),
            new PostgrestReopenLearningCaseCommand(httpClient, projectUri, apiKey, accessTokenProvider),
            caseLifecycleRecovery);

        return new PersonalTeachingWorkspaceServices(
            new PersonalStudentWorkspaceCoordinator(
                bootstrapReader,
                new StudentRecentObservationsCoordinator(recentReader)),
            new StudentLearningFocusCoordinator(focusReader),
            new StudentLearningCasesCoordinator(caseHistoryReader),
            new PersonalTodayActionsCoordinator(todayReader),
            createLearningCase,
            createLearningCaseRecovery,
            actionProgression,
            actionProgressionRecovery,
            caseLifecycle,
            caseLifecycleRecovery,
            organizationManagementReader,
            organizationInvitationCommand,
            organizationInvitationAcceptance,
            organizationInvitationDelivery,
            organizationInvitationRecovery);
    }
}
