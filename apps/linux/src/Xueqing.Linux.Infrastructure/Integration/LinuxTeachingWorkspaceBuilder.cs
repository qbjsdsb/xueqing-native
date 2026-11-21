using System.Runtime.Versioning;
using Xueqing.Linux.Infrastructure.LocalData;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.LocalData;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Linux.Infrastructure.Integration;

[SupportedOSPlatform("linux")]
public static class LinuxTeachingWorkspaceBuilder
{
    public static PersonalTeachingWorkspaceServices Create(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider,
        IConsequentialWriteCompatibilityGate compatibilityGate,
        string environmentId,
        string? appDataRoot = null,
        string? functionRegion = null,
        Func<string, ILocalDatabaseKeyStore>? keyStoreFactory = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(projectUri);
        ArgumentNullException.ThrowIfNull(accessTokenProvider);
        ArgumentNullException.ThrowIfNull(compatibilityGate);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);

        var storeContext = new LinuxLocalStoreContext(environmentId, appDataRoot, keyStoreFactory);

        var bootstrapReader = new PostgrestPersonalBootstrapReader(httpClient, projectUri, apiKey, accessTokenProvider);
        var organizationManagementReader = new PostgrestOrganizationManagementReader(httpClient, projectUri, apiKey, accessTokenProvider);
        var organizationInvitationCommand = new CompatibilityGatedOrganizationInvitationCommand(
            new PostgrestOrganizationInvitationCommand(httpClient, projectUri, apiKey, accessTokenProvider),
            compatibilityGate);
        var organizationInvitationAcceptance = new CompatibilityGatedAcceptOrganizationInvitationCommand(
            new PostgrestAcceptOrganizationInvitationCommand(httpClient, projectUri, apiKey, accessTokenProvider),
            compatibilityGate);
        var organizationInvitationDelivery = new CompatibilityGatedOrganizationInvitationDeliveryCommand(
            new SupabaseOrganizationInvitationDeliveryCommand(
                httpClient, projectUri, apiKey, accessTokenProvider, functionRegion),
            compatibilityGate);
        var organizationInvitationRecovery = new LinuxOrganizationInvitationRecoveryStore(storeContext);

        var recentReader = new PostgrestStudentRecentObservationsReader(httpClient, projectUri, apiKey, accessTokenProvider);
        var focusReader = new PostgrestStudentLearningFocusReader(httpClient, projectUri, apiKey, accessTokenProvider);
        var caseHistoryReader = new PostgrestStudentLearningCasesReader(httpClient, projectUri, apiKey, accessTokenProvider);
        var todayReader = new PostgrestPersonalTodayActionsReader(httpClient, projectUri, apiKey, accessTokenProvider);

        var createLearningCase = new CompatibilityGatedCreateLearningCaseCommand(
            new PostgrestCreateLearningCaseCommand(httpClient, projectUri, apiKey, accessTokenProvider),
            compatibilityGate);
        var createLearningCaseRecovery = new LinuxCreateLearningCaseRecoveryStore(storeContext);

        var observationCapture = new ObservationQuickCaptureCoordinator(
            new LinuxObservationDraftStore(storeContext),
            new LinuxCreateObservationRecoveryStore(storeContext),
            new CompatibilityGatedCreateObservationCommand(
                new PostgrestCreateObservationCommand(httpClient, projectUri, apiKey, accessTokenProvider),
                compatibilityGate));

        var actionProgressionRecovery = new LinuxActionProgressionRecoveryStore(storeContext);
        var actionProgression = new ActionProgressionCommandCoordinator(
            new CompatibilityGatedReschedulePrimaryActionCommand(
                new PostgrestReschedulePrimaryActionCommand(httpClient, projectUri, apiKey, accessTokenProvider),
                compatibilityGate),
            new CompatibilityGatedRecordVerificationAndNextActionCommand(
                new PostgrestRecordVerificationAndNextActionCommand(httpClient, projectUri, apiKey, accessTokenProvider),
                compatibilityGate),
            actionProgressionRecovery);

        var caseLifecycleRecovery = new LinuxCaseLifecycleRecoveryStore(storeContext);
        var caseLifecycle = new CaseLifecycleCommandCoordinator(
            new CompatibilityGatedTransitionLearningCaseStateCommand(
                new PostgrestTransitionLearningCaseStateCommand(httpClient, projectUri, apiKey, accessTokenProvider),
                compatibilityGate),
            new CompatibilityGatedCloseLearningCaseCommand(
                new PostgrestCloseLearningCaseCommand(httpClient, projectUri, apiKey, accessTokenProvider),
                compatibilityGate),
            new CompatibilityGatedReopenLearningCaseCommand(
                new PostgrestReopenLearningCaseCommand(httpClient, projectUri, apiKey, accessTokenProvider),
                compatibilityGate),
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
            organizationInvitationRecovery,
            observationCapture);
    }
}
