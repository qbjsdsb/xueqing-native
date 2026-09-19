namespace Xueqing.Windows.Core.Services;

public sealed record PersonalTeachingWorkspaceServices(
    PersonalStudentWorkspaceCoordinator Students,
    StudentLearningFocusCoordinator LearningFocus,
    PersonalTodayActionsCoordinator Today,
    ICreateLearningCaseCommand CreateLearningCase,
    ICreateLearningCaseRecoveryStore CreateLearningCaseRecovery,
    ActionProgressionCommandCoordinator? ActionProgression = null,
    IActionProgressionRecoveryStore? ActionProgressionRecovery = null);
