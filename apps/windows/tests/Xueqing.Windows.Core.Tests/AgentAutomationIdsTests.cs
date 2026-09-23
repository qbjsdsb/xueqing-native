using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Agent;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class AgentAutomationIdsTests
{
    private static readonly Guid EntityId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");

    [TestMethod]
    public void Entity_ids_are_deterministic_and_action_scoped()
    {
        Assert.AreEqual(
            "Xueqing.Today.Reschedule.11111111-1111-4111-8111-111111111111",
            AgentAutomationIds.TodayReschedule(EntityId));
        Assert.AreEqual(
            "Xueqing.Today.Verify.11111111-1111-4111-8111-111111111111",
            AgentAutomationIds.TodayVerify(EntityId));
        Assert.AreNotEqual(
            AgentAutomationIds.FocusReschedule(EntityId),
            AgentAutomationIds.FocusVerify(EntityId));
        Assert.AreNotEqual(
            AgentAutomationIds.CaseLifecycle(EntityId),
            AgentAutomationIds.ObservationCreateLearningCase(EntityId));
    }

    [TestMethod]
    public void Recovery_ids_are_operation_scoped()
    {
        Assert.AreEqual(
            "Xueqing.Recovery.LearningCase.11111111-1111-4111-8111-111111111111",
            AgentAutomationIds.RecoveryLearningCase(EntityId));
        Assert.AreEqual(
            "Xueqing.Recovery.Action.11111111-1111-4111-8111-111111111111",
            AgentAutomationIds.RecoveryAction(EntityId));
        Assert.AreEqual(
            "Xueqing.Recovery.CaseLifecycle.11111111-1111-4111-8111-111111111111",
            AgentAutomationIds.RecoveryCaseLifecycle(EntityId));
    }

    [TestMethod]
    public void Empty_entity_id_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => AgentAutomationIds.TodayVerify(Guid.Empty));
    }
}
