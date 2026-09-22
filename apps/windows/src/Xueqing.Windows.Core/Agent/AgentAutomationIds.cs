namespace Xueqing.Windows.Core.Agent;

/// <summary>
/// Stable UI Automation ids for agent/accessibility compatibility.
///
/// Entity-scoped ids are derived only from application-owned immutable UUIDs.
/// Display text, list position, provider identity and organization role never
/// participate in the compatibility identifier.
/// </summary>
public static class AgentAutomationIds
{
    private const string Prefix = "Xueqing";

    public static string TodayReschedule(Guid actionId) =>
        Entity("Today.Reschedule", actionId);

    public static string TodayVerify(Guid actionId) =>
        Entity("Today.Verify", actionId);

    public static string FocusReschedule(Guid caseId) =>
        Entity("Focus.Reschedule", caseId);

    public static string FocusVerify(Guid caseId) =>
        Entity("Focus.Verify", caseId);

    public static string CaseLifecycle(Guid caseId) =>
        Entity("Case.Lifecycle", caseId);

    public static string ObservationCreateLearningCase(Guid observationId) =>
        Entity("Observation.CreateLearningCase", observationId);

    public static string RecoveryLearningCase(Guid operationId) =>
        Entity("Recovery.LearningCase", operationId);

    public static string RecoveryAction(Guid operationId) =>
        Entity("Recovery.Action", operationId);

    public static string RecoveryCaseLifecycle(Guid operationId) =>
        Entity("Recovery.CaseLifecycle", operationId);

    private static string Entity(string action, Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Automation entity id must be a non-empty application-owned UUID.",
                nameof(id));
        }

        return $"{Prefix}.{action}.{id:D}";
    }
}
