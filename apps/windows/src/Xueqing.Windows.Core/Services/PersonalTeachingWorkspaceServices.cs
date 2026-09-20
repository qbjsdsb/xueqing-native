namespace Xueqing.Windows.Core.Services;

public sealed record PersonalTeachingWorkspaceServices(
    PersonalStudentWorkspaceCoordinator Students,
    StudentLearningFocusCoordinator LearningFocus,
    StudentLearningCasesCoordinator CaseHistory,
    PersonalTodayActionsCoordinator Today,
    ICreateLearningCaseCommand CreateLearningCase,
    ICreateLearningCaseRecoveryStore CreateLearningCaseRecovery,
    ActionProgressionCommandCoordinator? ActionProgression = null,
    IActionProgressionRecoveryStore? ActionProgressionRecovery = null,
    CaseLifecycleCommandCoordinator? CaseLifecycle = null,
    ICaseLifecycleRecoveryStore? CaseLifecycleRecovery = null,
    IOrganizationManagementReader? OrganizationManagement = null,
    IOrganizationInvitationCommand? OrganizationInvitations = null,
    IAcceptOrganizationInvitationCommand? OrganizationInvitationAcceptance = null,
    IOrganizationInvitationDeliveryCommand? OrganizationInvitationDelivery = null,
    IOrganizationInvitationRecoveryStore? OrganizationInvitationRecovery = null);
