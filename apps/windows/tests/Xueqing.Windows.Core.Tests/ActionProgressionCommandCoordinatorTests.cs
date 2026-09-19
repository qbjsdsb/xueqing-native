using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class ActionProgressionCommandCoordinatorTests
{
    private static readonly Guid Actor =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    [TestMethod]
    public async Task Reschedule_is_durable_before_network_send_and_success_cleans_up()
    {
        var calls = new List<string>();
        var recovery = new RecordingRecoveryStore(calls);
        var command = new StubRescheduleCommand(calls, request =>
            ActionProgressionResult<ReschedulePrimaryActionReceipt>.Success(
                RescheduleReceipt(request)));
        var coordinator = new ActionProgressionCommandCoordinator(
            command,
            new StubVerificationCommand(calls, _ => throw new AssertFailedException()),
            recovery);

        var request = Reschedule();
        var result = await coordinator.RescheduleAsync(request, Actor);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { "save", "reschedule-send", "remove" },
            calls.ToArray());
        Assert.AreEqual(request.OperationId, recovery.LastSaved!.OperationId);
    }

    [TestMethod]
    public async Task Unknown_result_keeps_exact_intent_for_same_operation_retry()
    {
        var calls = new List<string>();
        var recovery = new RecordingRecoveryStore(calls);
        var command = new StubVerificationCommand(calls, _ =>
            ActionProgressionResult<RecordVerificationAndNextActionReceipt>.Failed(
                ActionProgressionFailureKind.ResultUnknown,
                "XQ_RESULT_UNKNOWN_TIMEOUT"));
        var coordinator = new ActionProgressionCommandCoordinator(
            new StubRescheduleCommand(calls, _ => throw new AssertFailedException()),
            command,
            recovery);

        var request = Verification();
        var result = await coordinator.VerifyAsync(request, Actor);

        Assert.IsTrue(result.MustRetrySameOperation);
        CollectionAssert.AreEqual(
            new[] { "save", "verification-send" },
            calls.ToArray());
        Assert.AreEqual(request.OperationId, recovery.LastSaved!.OperationId);
        Assert.AreEqual(0, recovery.MarkRejectedCount);
        Assert.AreEqual(0, recovery.RemoveCount);
    }

    [TestMethod]
    public async Task Deterministic_rejection_is_quarantined_before_cleanup()
    {
        var calls = new List<string>();
        var recovery = new RecordingRecoveryStore(calls);
        var command = new StubRescheduleCommand(calls, _ =>
            ActionProgressionResult<ReschedulePrimaryActionReceipt>.Failed(
                ActionProgressionFailureKind.VersionConflict,
                "XQ_ACTION_VERSION_CONFLICT"));
        var coordinator = new ActionProgressionCommandCoordinator(
            command,
            new StubVerificationCommand(calls, _ => throw new AssertFailedException()),
            recovery);

        var result = await coordinator.RescheduleAsync(Reschedule(), Actor);

        Assert.AreEqual(ActionProgressionFailureKind.VersionConflict, result.Failure?.Kind);
        CollectionAssert.AreEqual(
            new[] { "save", "reschedule-send", "reject", "remove" },
            calls.ToArray());
    }

    [TestMethod]
    public async Task Failed_local_save_prevents_any_authoritative_send()
    {
        var calls = new List<string>();
        var recovery = new RecordingRecoveryStore(calls) { FailSave = true };
        var coordinator = new ActionProgressionCommandCoordinator(
            new StubRescheduleCommand(
                calls,
                _ => throw new AssertFailedException("Network send must not happen.")),
            new StubVerificationCommand(
                calls,
                _ => throw new AssertFailedException("Network send must not happen.")),
            recovery);

        var result = await coordinator.RescheduleAsync(Reschedule(), Actor);

        Assert.AreEqual(
            ActionProgressionFailureKind.LocalDurabilityFailure,
            result.Failure?.Kind);
        CollectionAssert.AreEqual(new[] { "save" }, calls.ToArray());
    }

    [TestMethod]
    public async Task Quarantine_failure_replaces_deterministic_result_with_local_safety_failure()
    {
        var calls = new List<string>();
        var recovery = new RecordingRecoveryStore(calls) { FailReject = true };
        var coordinator = new ActionProgressionCommandCoordinator(
            new StubRescheduleCommand(calls, _ =>
                ActionProgressionResult<ReschedulePrimaryActionReceipt>.Failed(
                    ActionProgressionFailureKind.AuthorityChanged,
                    "XQ_MEMBERSHIP_DISABLED")),
            new StubVerificationCommand(calls, _ => throw new AssertFailedException()),
            recovery);

        var result = await coordinator.RescheduleAsync(Reschedule(), Actor);

        Assert.AreEqual(
            ActionProgressionFailureKind.LocalDurabilityFailure,
            result.Failure?.Kind);
        Assert.AreEqual(
            "XQ_LOCAL_ACTION_REJECTION_QUARANTINE_FAILED",
            result.Failure?.Code);
        CollectionAssert.AreEqual(
            new[] { "save", "reschedule-send", "reject" },
            calls.ToArray());
    }

    private static ReschedulePrimaryActionRequest Reschedule() =>
        new(
            Guid.Parse("80000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            2,
            4,
            new DateOnly(2026, 9, 24));

    private static RecordVerificationAndNextActionRequest Verification() =>
        new(
            Guid.Parse("80000000-0000-0000-0000-000000000002"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            2,
            4,
            VerificationOutcome.PartiallyMet,
            "还有遗漏",
            "再练跨段概括",
            new DateOnly(2026, 9, 24));

    private static ReschedulePrimaryActionReceipt RescheduleReceipt(
        ReschedulePrimaryActionRequest request) =>
        new(
            request.OperationId,
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.OwnerAssignmentId,
            Actor,
            request.CaseId,
            LearningCaseState.Intervening,
            request.ExpectedCaseVersion + 1,
            request.PrimaryActionId,
            request.ExpectedActionVersion + 1,
            new DateOnly(2026, 9, 21),
            request.NewDueOn,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

    private sealed class StubRescheduleCommand(
        List<string> calls,
        Func<ReschedulePrimaryActionRequest,
            ActionProgressionResult<ReschedulePrimaryActionReceipt>> execute)
        : IReschedulePrimaryActionCommand
    {
        public Task<ActionProgressionResult<ReschedulePrimaryActionReceipt>> ExecuteAsync(
            ReschedulePrimaryActionRequest request,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("reschedule-send");
            return Task.FromResult(execute(request));
        }
    }

    private sealed class StubVerificationCommand(
        List<string> calls,
        Func<RecordVerificationAndNextActionRequest,
            ActionProgressionResult<RecordVerificationAndNextActionReceipt>> execute)
        : IRecordVerificationAndNextActionCommand
    {
        public Task<ActionProgressionResult<RecordVerificationAndNextActionReceipt>> ExecuteAsync(
            RecordVerificationAndNextActionRequest request,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("verification-send");
            return Task.FromResult(execute(request));
        }
    }

    private sealed class RecordingRecoveryStore(List<string> calls)
        : IActionProgressionRecoveryStore
    {
        public bool FailSave { get; init; }
        public bool FailReject { get; init; }
        public int MarkRejectedCount { get; private set; }
        public int RemoveCount { get; private set; }
        public ActionProgressionRecoveryIntent? LastSaved { get; private set; }

        public Task SaveAsync(
            Guid actorAppUserId,
            ActionProgressionRecoveryIntent intent,
            CancellationToken cancellationToken = default)
        {
            calls.Add("save");
            if (FailSave)
            {
                throw new IOException("fictional local durability failure");
            }

            LastSaved = intent;
            return Task.CompletedTask;
        }

        public Task<ActionProgressionRecoveryIntent?> FindByPrimaryActionAsync(
            Guid actorAppUserId,
            Guid organizationId,
            Guid caseId,
            Guid primaryActionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ActionProgressionRecoveryIntent?>(LastSaved);

        public Task<IReadOnlyList<ActionProgressionRecoveryIntent>> ListPendingAsync(
            Guid actorAppUserId,
            IReadOnlyCollection<Guid> currentOrganizationIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActionProgressionRecoveryIntent>>(
                LastSaved is null
                    ? Array.Empty<ActionProgressionRecoveryIntent>()
                    : new[] { LastSaved });

        public Task MarkRejectedAsync(
            Guid actorAppUserId,
            Guid organizationId,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("reject");
            MarkRejectedCount++;
            if (FailReject)
            {
                throw new IOException("fictional quarantine failure");
            }

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
