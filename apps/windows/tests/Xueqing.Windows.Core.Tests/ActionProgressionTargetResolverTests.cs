using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class ActionProgressionTargetResolverTests
{
    private static readonly Guid Actor = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Org = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Student = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid Profile = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid Assignment = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid Case = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid Action = Guid.Parse("70000000-0000-0000-0000-000000000001");

    [TestMethod]
    public void Today_target_preserves_authoritative_versions_assignment_and_action_identity()
    {
        var target = ActionProgressionTargetResolver.FromToday(TodayAction());

        Assert.AreEqual(Org, target.OrganizationId);
        Assert.AreEqual(Assignment, target.OwnerAssignmentId);
        Assert.AreEqual(Case, target.CaseId);
        Assert.AreEqual(7L, target.CaseVersion);
        Assert.AreEqual(Action, target.PrimaryActionId);
        Assert.AreEqual(11L, target.ActionVersion);

        var request = target.CreateRescheduleRequest(
            Guid.Parse("80000000-0000-0000-0000-000000000001"),
            new DateOnly(2026, 9, 28));
        Assert.AreEqual(7L, request.ExpectedCaseVersion);
        Assert.AreEqual(11L, request.ExpectedActionVersion);
        Assert.AreEqual(Assignment, request.OwnerAssignmentId);
    }

    [TestMethod]
    public void Focus_target_uses_snapshot_scope_and_rejects_assignment_drift()
    {
        var focus = FocusCase();
        var snapshot = FocusSnapshot(focus);

        var target = ActionProgressionTargetResolver.FromFocus(snapshot, focus);
        Assert.AreEqual(snapshot.OrganizationId, target.OrganizationId);
        Assert.AreEqual(snapshot.SubjectProfileId, target.SubjectProfileId);
        Assert.AreEqual(snapshot.AssignmentId, target.OwnerAssignmentId);
        Assert.AreEqual(focus.Version, target.CaseVersion);
        Assert.AreEqual(focus.PrimaryAction.Version, target.ActionVersion);

        var drifted = focus with { OwnerAssignmentId = Guid.NewGuid() };
        Assert.ThrowsExactly<InvalidDataException>(
            () => ActionProgressionTargetResolver.FromFocus(
                FocusSnapshot(drifted) with { AssignmentId = Assignment },
                drifted));
    }

    [TestMethod]
    public void Verification_request_is_built_only_from_frozen_authoritative_target()
    {
        var target = ActionProgressionTargetResolver.FromToday(TodayAction());
        var operationId = Guid.Parse("80000000-0000-0000-0000-000000000002");

        var request = target.CreateVerificationRequest(
            operationId,
            VerificationOutcome.PartiallyMet,
            "仍有遗漏",
            "再练两道陌生材料",
            new DateOnly(2026, 9, 29));

        Assert.AreEqual(operationId, request.OperationId);
        Assert.AreEqual(Case, request.CaseId);
        Assert.AreEqual(Action, request.CurrentPrimaryActionId);
        Assert.AreEqual(7L, request.ExpectedCaseVersion);
        Assert.AreEqual(11L, request.ExpectedActionVersion);
        Assert.AreEqual("仍有遗漏", request.VerificationSummary);
        Assert.AreEqual("再练两道陌生材料", request.NextActionText);
    }

    private static PersonalTodayAction TodayAction() =>
        new(
            Org,
            "虚构机构",
            "Asia/Shanghai",
            new DateOnly(2026, 9, 20),
            Student,
            "虚构学生",
            Profile,
            "chinese",
            Assignment,
            Case,
            "概括题压缩仍不稳定",
            LearningCaseState.Intervening,
            7,
            Action,
            "复核陌生材料",
            new DateOnly(2026, 9, 21),
            ActionDueBucket.Today,
            11,
            DateTimeOffset.Parse("2026-09-20T08:00:00Z"));

    private static StudentLearningCaseFocus FocusCase() =>
        new(
            Case,
            "概括题压缩仍不稳定",
            LearningCaseState.Intervening,
            7,
            Actor,
            Assignment,
            DateTimeOffset.Parse("2026-09-18T08:00:00Z"),
            DateTimeOffset.Parse("2026-09-20T08:00:00Z"),
            new LearningPrimaryAction(
                Action,
                "复核陌生材料",
                new DateOnly(2026, 9, 21),
                ActionDueBucket.Today,
                11));

    private static StudentLearningFocusSnapshot FocusSnapshot(
        StudentLearningCaseFocus learningCase) =>
        new(
            DateTimeOffset.Parse("2026-09-20T08:00:00Z"),
            Actor,
            Org,
            "虚构机构",
            "Asia/Shanghai",
            new DateOnly(2026, 9, 20),
            Student,
            "虚构学生",
            Profile,
            "chinese",
            Assignment,
            new[] { learningCase },
            false);
}
