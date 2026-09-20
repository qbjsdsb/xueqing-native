using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class CaseLifecycleTargetTests
{
    private static readonly Guid Actor = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Org = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Student = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid Profile = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid Assignment = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid Case = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid Action = Guid.Parse("70000000-0000-0000-0000-000000000001");

    [TestMethod]
    public void Current_responsibility_history_case_becomes_frozen_lifecycle_target()
    {
        var item = OpenCase(LearningCaseState.Stable);
        var target = CaseLifecycleTargetResolver.FromHistory(Snapshot(item), item);

        Assert.AreEqual(Org, target.OrganizationId);
        Assert.AreEqual(Assignment, target.OwnerAssignmentId);
        Assert.AreEqual(Actor, target.ResponsibleTeacherAppUserId);
        Assert.AreEqual(Case, target.CaseId);
        Assert.AreEqual(5L, target.CaseVersion);
        Assert.AreEqual(Action, target.PrimaryAction?.ActionId);
        Assert.AreEqual(9L, target.PrimaryAction?.Version);

        var close = target.CreateCloseRequest(
            Guid.Parse("80000000-0000-0000-0000-000000000001"));
        Assert.AreEqual(5L, close.ExpectedCaseVersion);
        Assert.AreEqual(9L, close.ExpectedActionVersion);
        Assert.AreEqual(Action, close.PrimaryActionId);
    }

    [TestMethod]
    public void Readable_historical_case_without_current_responsibility_is_not_writable()
    {
        var item = OpenCase(LearningCaseState.Intervening) with
        {
            IsCurrentActorResponsibility = false,
            OwnerAssignmentId = Guid.Parse("50000000-0000-0000-0000-000000000099"),
            ResponsibleTeacherAppUserId = Guid.Parse("10000000-0000-0000-0000-000000000099"),
        };

        Assert.ThrowsExactly<InvalidDataException>(
            () => CaseLifecycleTargetResolver.FromHistory(Snapshot(item), item));
    }

    [TestMethod]
    public void Closed_case_requires_no_pending_action_and_builds_reopen_from_frozen_version()
    {
        var item = OpenCase(LearningCaseState.Closed) with { PrimaryAction = null };
        var target = CaseLifecycleTargetResolver.FromHistory(Snapshot(item), item);
        var operationId = Guid.Parse("80000000-0000-0000-0000-000000000002");

        var reopen = target.CreateReopenRequest(
            operationId,
            "重新检查陌生材料中的限制条件",
            new DateOnly(2026, 9, 29));

        Assert.AreEqual(operationId, reopen.OperationId);
        Assert.AreEqual(Case, reopen.CaseId);
        Assert.AreEqual(5L, reopen.ExpectedCaseVersion);
        Assert.AreEqual("重新检查陌生材料中的限制条件", reopen.NewPrimaryActionText);
    }

    [TestMethod]
    public void Projection_invariant_mismatch_is_rejected_before_command_target_exists()
    {
        var closedWithAction = OpenCase(LearningCaseState.Closed);
        Assert.ThrowsExactly<InvalidDataException>(
            () => CaseLifecycleTargetResolver.FromHistory(
                Snapshot(closedWithAction),
                closedWithAction));

        var openWithoutAction = OpenCase(LearningCaseState.Stable) with { PrimaryAction = null };
        Assert.ThrowsExactly<InvalidDataException>(
            () => CaseLifecycleTargetResolver.FromHistory(
                Snapshot(openWithoutAction),
                openWithoutAction));
    }

    private static StudentLearningCaseSummary OpenCase(LearningCaseState state) =>
        new(
            Case,
            "概括题压缩仍不稳定",
            state,
            5,
            Actor,
            Assignment,
            true,
            DateTimeOffset.Parse("2026-09-18T08:00:00Z"),
            DateTimeOffset.Parse("2026-09-20T08:00:00Z"),
            new LearningPrimaryAction(
                Action,
                "复核陌生材料",
                new DateOnly(2026, 9, 21),
                ActionDueBucket.Today,
                9));

    private static StudentLearningCasesSnapshot Snapshot(StudentLearningCaseSummary item) =>
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
            new[] { item },
            false);
}

[TestClass]
public sealed class CaseLifecycleCommandCoordinatorTests
{
    private static readonly Guid Actor =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    [TestMethod]
    public async Task Transition_is_durable_before_send_and_success_cleans_up()
    {
        var calls = new List<string>();
        var recovery = new RecordingRecoveryStore(calls);
        var transition = new StubTransitionCommand(calls, request =>
            CaseLifecycleResult<TransitionLearningCaseStateReceipt>.Success(
                TransitionReceipt(request)));

        var coordinator = new CaseLifecycleCommandCoordinator(
            transition,
            new StubCloseCommand(calls, _ => throw new AssertFailedException()),
            new StubReopenCommand(calls, _ => throw new AssertFailedException()),
            recovery);

        var request = Transition();
        var result = await coordinator.TransitionAsync(request, Actor);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { "save", "transition-send", "remove" },
            calls.ToArray());
        Assert.AreEqual(request.OperationId, recovery.LastPersisted?.OperationId);
        Assert.IsNull(recovery.LastSaved);
    }

    [TestMethod]
    public async Task Unknown_reopen_result_keeps_exact_intent_for_same_operation_retry()
    {
        var calls = new List<string>();
        var recovery = new RecordingRecoveryStore(calls);
        var reopen = new StubReopenCommand(calls, _ =>
            CaseLifecycleResult<ReopenLearningCaseReceipt>.Failed(
                CaseLifecycleFailureKind.ResultUnknown,
                "XQ_RESULT_UNKNOWN_TIMEOUT"));

        var coordinator = new CaseLifecycleCommandCoordinator(
            new StubTransitionCommand(calls, _ => throw new AssertFailedException()),
            new StubCloseCommand(calls, _ => throw new AssertFailedException()),
            reopen,
            recovery);

        var request = Reopen();
        var result = await coordinator.ReopenAsync(request, Actor);

        Assert.IsTrue(result.MustRetrySameOperation);
        CollectionAssert.AreEqual(new[] { "save", "reopen-send" }, calls.ToArray());
        Assert.AreEqual(request.OperationId, recovery.LastSaved?.OperationId);
        Assert.AreEqual(0, recovery.MarkRejectedCount);
        Assert.AreEqual(0, recovery.RemoveCount);
    }

    [TestMethod]
    public async Task Deterministic_close_conflict_is_quarantined_then_removed()
    {
        var calls = new List<string>();
        var recovery = new RecordingRecoveryStore(calls);
        var close = new StubCloseCommand(calls, _ =>
            CaseLifecycleResult<CloseLearningCaseReceipt>.Failed(
                CaseLifecycleFailureKind.VersionConflict,
                "XQ_ACTION_VERSION_CONFLICT"));

        var coordinator = new CaseLifecycleCommandCoordinator(
            new StubTransitionCommand(calls, _ => throw new AssertFailedException()),
            close,
            new StubReopenCommand(calls, _ => throw new AssertFailedException()),
            recovery);

        var result = await coordinator.CloseAsync(Close(), Actor);

        Assert.AreEqual(CaseLifecycleFailureKind.VersionConflict, result.Failure?.Kind);
        CollectionAssert.AreEqual(
            new[] { "save", "close-send", "reject", "remove" },
            calls.ToArray());
    }

    [TestMethod]
    public async Task Local_durability_failure_prevents_authoritative_send()
    {
        var calls = new List<string>();
        var recovery = new RecordingRecoveryStore(calls) { FailSave = true };
        var coordinator = new CaseLifecycleCommandCoordinator(
            new StubTransitionCommand(
                calls,
                _ => throw new AssertFailedException("Network send must not happen.")),
            new StubCloseCommand(
                calls,
                _ => throw new AssertFailedException("Network send must not happen.")),
            new StubReopenCommand(
                calls,
                _ => throw new AssertFailedException("Network send must not happen.")),
            recovery);

        var result = await coordinator.TransitionAsync(Transition(), Actor);

        Assert.AreEqual(
            CaseLifecycleFailureKind.LocalDurabilityFailure,
            result.Failure?.Kind);
        CollectionAssert.AreEqual(new[] { "save" }, calls.ToArray());
    }

    private static TransitionLearningCaseStateRequest Transition() =>
        new(
            Guid.Parse("81000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            4,
            LearningCaseState.Stable);

    private static CloseLearningCaseRequest Close() =>
        new(
            Guid.Parse("81000000-0000-0000-0000-000000000002"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            5,
            9);

    private static ReopenLearningCaseRequest Reopen() =>
        new(
            Guid.Parse("81000000-0000-0000-0000-000000000003"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            6,
            "重新检查陌生材料中的限制条件",
            new DateOnly(2026, 9, 29));

    private static TransitionLearningCaseStateReceipt TransitionReceipt(
        TransitionLearningCaseStateRequest request) =>
        new(
            request.OperationId,
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.OwnerAssignmentId,
            Actor,
            request.CaseId,
            LearningCaseState.PendingVerification,
            request.TargetState,
            request.ExpectedCaseVersion + 1,
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

    private sealed class StubTransitionCommand(
        List<string> calls,
        Func<TransitionLearningCaseStateRequest,
            CaseLifecycleResult<TransitionLearningCaseStateReceipt>> execute)
        : ITransitionLearningCaseStateCommand
    {
        public Task<CaseLifecycleResult<TransitionLearningCaseStateReceipt>> ExecuteAsync(
            TransitionLearningCaseStateRequest request,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("transition-send");
            return Task.FromResult(execute(request));
        }
    }

    private sealed class StubCloseCommand(
        List<string> calls,
        Func<CloseLearningCaseRequest,
            CaseLifecycleResult<CloseLearningCaseReceipt>> execute)
        : ICloseLearningCaseCommand
    {
        public Task<CaseLifecycleResult<CloseLearningCaseReceipt>> ExecuteAsync(
            CloseLearningCaseRequest request,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("close-send");
            return Task.FromResult(execute(request));
        }
    }

    private sealed class StubReopenCommand(
        List<string> calls,
        Func<ReopenLearningCaseRequest,
            CaseLifecycleResult<ReopenLearningCaseReceipt>> execute)
        : IReopenLearningCaseCommand
    {
        public Task<CaseLifecycleResult<ReopenLearningCaseReceipt>> ExecuteAsync(
            ReopenLearningCaseRequest request,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("reopen-send");
            return Task.FromResult(execute(request));
        }
    }

    private sealed class RecordingRecoveryStore(List<string> calls)
        : ICaseLifecycleRecoveryStore
    {
        public bool FailSave { get; init; }
        public int MarkRejectedCount { get; private set; }
        public int RemoveCount { get; private set; }
        public CaseLifecycleRecoveryIntent? LastSaved { get; private set; }
        public CaseLifecycleRecoveryIntent? LastPersisted { get; private set; }

        public Task SaveAsync(
            Guid actorAppUserId,
            CaseLifecycleRecoveryIntent intent,
            CancellationToken cancellationToken = default)
        {
            calls.Add("save");
            if (FailSave)
            {
                throw new IOException("fictional local durability failure");
            }

            LastSaved = intent;
            LastPersisted = intent;
            return Task.CompletedTask;
        }

        public Task<CaseLifecycleRecoveryIntent?> FindByCaseAsync(
            Guid actorAppUserId,
            Guid organizationId,
            Guid caseId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LastSaved);

        public Task<IReadOnlyList<CaseLifecycleRecoveryIntent>> ListPendingAsync(
            Guid actorAppUserId,
            IReadOnlyCollection<Guid> currentOrganizationIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CaseLifecycleRecoveryIntent>>(
                LastSaved is null
                    ? Array.Empty<CaseLifecycleRecoveryIntent>()
                    : new[] { LastSaved });

        public Task MarkRejectedAsync(
            Guid actorAppUserId,
            Guid organizationId,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("reject");
            MarkRejectedCount++;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            Guid actorAppUserId,
            Guid organizationId,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("remove");
            RemoveCount++;
            LastSaved = null;
            return Task.CompletedTask;
        }
    }
}
