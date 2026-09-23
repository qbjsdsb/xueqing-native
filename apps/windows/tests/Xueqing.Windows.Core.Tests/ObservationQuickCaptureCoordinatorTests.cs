using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class ObservationQuickCaptureCoordinatorTests
{
    private static readonly Guid ActorId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OperationId =
        Guid.Parse("74000000-0000-0000-0000-000000000001");

    [TestMethod]
    public async Task Submit_durably_saves_draft_and_formal_intent_before_network()
    {
        var events = new List<string>();
        var drafts = new FakeDraftStore(events);
        var recovery = new FakeRecoveryStore(events);
        var command = new FakeCommand(events)
        {
            Result = SuccessResult(),
        };
        var coordinator = new ObservationQuickCaptureCoordinator(
            drafts,
            recovery,
            command);

        var opened = await coordinator.OpenAsync(ActorId, Scope());
        var result = await coordinator.SubmitAsync(
            ActorId,
            Scope(),
            opened.Draft.Epoch,
            OperationId,
            "最终课堂观察",
            DateTimeOffset.Parse("2026-09-22T12:00:00Z"),
            new Dictionary<string, string> { ["source"] = "windows_quick_capture" });

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[]
            {
                "draft-open",
                "recovery-find",
                "draft-save",
                "recovery-save",
                "command-execute",
                "draft-discard",
                "recovery-remove",
            },
            events);
        Assert.IsNull(await recovery.FindPendingAsync(ActorId, Scope()));
        Assert.IsNull((await drafts.OpenAsync(ActorId, Scope())).Recovered);
    }

    [TestMethod]
    public async Task Local_draft_failure_prevents_formal_intent_and_network()
    {
        var events = new List<string>();
        var drafts = new FakeDraftStore(events) { RejectSave = true };
        var recovery = new FakeRecoveryStore(events);
        var command = new FakeCommand(events);
        var coordinator = new ObservationQuickCaptureCoordinator(
            drafts,
            recovery,
            command);

        var result = await coordinator.SubmitAsync(
            ActorId,
            Scope(),
            0,
            OperationId,
            "不得丢失的课堂观察");

        Assert.AreEqual(
            CreateObservationFailureKind.LocalDurabilityFailure,
            result.Failure?.Kind);
        CollectionAssert.AreEqual(new[] { "draft-save" }, events);
        Assert.AreEqual(0, command.ExecuteCount);
    }

    [TestMethod]
    public async Task ResultUnknown_retains_same_operation_and_draft_for_retry()
    {
        var events = new List<string>();
        var drafts = new FakeDraftStore(events);
        var recovery = new FakeRecoveryStore(events);
        var command = new FakeCommand(events)
        {
            Result = CreateObservationResult.Failed(
                CreateObservationFailureKind.ResultUnknown,
                "XQ_RESULT_UNKNOWN_NETWORK"),
        };
        var coordinator = new ObservationQuickCaptureCoordinator(
            drafts,
            recovery,
            command);

        var first = await coordinator.SubmitAsync(
            ActorId,
            Scope(),
            0,
            OperationId,
            "网络结果未知但不可重复生成新写入");

        Assert.AreEqual(
            CreateObservationFailureKind.ResultUnknown,
            first.Failure?.Kind);
        var pending = await recovery.FindPendingAsync(ActorId, Scope());
        Assert.AreEqual(OperationId, pending?.OperationId);
        Assert.AreEqual(
            "网络结果未知但不可重复生成新写入",
            (await drafts.OpenAsync(ActorId, Scope())).Recovered?.Text);

        command.Result = SuccessResult();
        var retry = await coordinator.RetryPendingAsync(ActorId, Scope());

        Assert.IsTrue(retry.IsSuccess);
        Assert.AreEqual(2, command.ExecuteCount);
        CollectionAssert.AreEqual(
            new[] { OperationId, OperationId },
            command.OperationIds.ToArray());
        Assert.IsNull(await recovery.FindPendingAsync(ActorId, Scope()));
    }

    [TestMethod]
    public async Task Authentication_required_retains_operation_for_same_operation_retry()
    {
        var recovery = new FakeRecoveryStore(new List<string>());
        var command = new FakeCommand(new List<string>())
        {
            Result = CreateObservationResult.Failed(
                CreateObservationFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED"),
        };
        var coordinator = new ObservationQuickCaptureCoordinator(
            new FakeDraftStore(new List<string>()),
            recovery,
            command);

        var first = await coordinator.SubmitAsync(
            ActorId,
            Scope(),
            0,
            OperationId,
            "重新登录后必须继续同一个 operation");

        Assert.AreEqual(
            CreateObservationFailureKind.AuthenticationRequired,
            first.Failure?.Kind);
        Assert.AreEqual(
            OperationId,
            (await recovery.FindPendingAsync(ActorId, Scope()))?.OperationId);
    }

    [TestMethod]
    public async Task Deterministic_rejection_keeps_editable_draft_but_releases_formal_slot()
    {
        var drafts = new FakeDraftStore(new List<string>());
        var recovery = new FakeRecoveryStore(new List<string>());
        var command = new FakeCommand(new List<string>())
        {
            Result = CreateObservationResult.Failed(
                CreateObservationFailureKind.Validation,
                "XQ_INVALID_OBSERVATION_TEXT"),
        };
        var coordinator = new ObservationQuickCaptureCoordinator(
            drafts,
            recovery,
            command);

        var rejected = await coordinator.SubmitAsync(
            ActorId,
            Scope(),
            0,
            OperationId,
            "需要教师修正的草稿");

        Assert.AreEqual(
            CreateObservationFailureKind.Validation,
            rejected.Failure?.Kind);
        Assert.IsNull(await recovery.FindPendingAsync(ActorId, Scope()));
        Assert.AreEqual(
            "需要教师修正的草稿",
            (await drafts.OpenAsync(ActorId, Scope())).Recovered?.Text);
    }

    [TestMethod]
    public async Task Server_success_with_local_cleanup_failure_keeps_idempotent_recovery()
    {
        var drafts = new FakeDraftStore(new List<string>())
        {
            RejectDiscard = true,
        };
        var recovery = new FakeRecoveryStore(new List<string>());
        var command = new FakeCommand(new List<string>())
        {
            Result = SuccessResult(),
        };
        var coordinator = new ObservationQuickCaptureCoordinator(
            drafts,
            recovery,
            command);

        var result = await coordinator.SubmitAsync(
            ActorId,
            Scope(),
            0,
            OperationId,
            "服务器已提交但本地清理失败");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            OperationId,
            (await recovery.FindPendingAsync(ActorId, Scope()))?.OperationId);
    }

    private static ObservationDraftScope Scope() =>
        new(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"));

    private static CreateObservationResult SuccessResult() =>
        CreateObservationResult.Success(
            new CreateObservationReceipt(
                OperationId,
                Guid.Parse("60000000-0000-0000-0000-000000000001"),
                ActorId,
                Scope().OrganizationId,
                Scope().StudentId,
                Scope().SubjectProfileId,
                "chinese",
                DateTimeOffset.Parse("2026-09-22T12:01:00Z")));

    private sealed class FakeDraftStore(List<string> events) : IObservationDraftStore
    {
        private long _epoch;
        private string? _text;

        public bool RejectSave { get; init; }
        public bool RejectDiscard { get; init; }

        public Task<ObservationDraftOpenResult> OpenAsync(
            Guid actorAppUserId,
            ObservationDraftScope scope,
            CancellationToken cancellationToken = default)
        {
            events.Add("draft-open");
            return Task.FromResult(
                new ObservationDraftOpenResult(
                    _epoch,
                    _text is null
                        ? null
                        : new ObservationDraftSnapshot(
                            _epoch,
                            _text,
                            DateTimeOffset.Parse("2026-09-22T12:00:00Z"))));
        }

        public Task<bool> SaveAsync(
            Guid actorAppUserId,
            ObservationDraftScope scope,
            long expectedEpoch,
            string text,
            CancellationToken cancellationToken = default)
        {
            events.Add("draft-save");
            if (RejectSave || expectedEpoch != _epoch)
            {
                return Task.FromResult(false);
            }

            _text = text;
            return Task.FromResult(true);
        }

        public Task<long?> DiscardAsync(
            Guid actorAppUserId,
            ObservationDraftScope scope,
            long expectedEpoch,
            CancellationToken cancellationToken = default)
        {
            events.Add("draft-discard");
            if (RejectDiscard || expectedEpoch != _epoch)
            {
                return Task.FromResult<long?>(null);
            }

            _text = null;
            _epoch++;
            return Task.FromResult<long?>(_epoch);
        }
    }

    private sealed class FakeRecoveryStore(List<string> events) :
        ICreateObservationRecoveryStore
    {
        private CreateObservationRequest? _pending;
        private bool _rejected;

        public Task SaveAsync(
            Guid actorAppUserId,
            CreateObservationRequest request,
            CancellationToken cancellationToken = default)
        {
            events.Add("recovery-save");
            if (_pending is not null &&
                _pending.OperationId != request.OperationId)
            {
                throw new InvalidOperationException("pending collision");
            }

            _pending = request;
            _rejected = false;
            return Task.CompletedTask;
        }

        public Task<CreateObservationRequest?> FindPendingAsync(
            Guid actorAppUserId,
            ObservationDraftScope scope,
            CancellationToken cancellationToken = default)
        {
            events.Add("recovery-find");
            return Task.FromResult(_rejected ? null : _pending);
        }

        public Task MarkRejectedAsync(
            Guid actorAppUserId,
            Guid organizationId,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            events.Add("recovery-reject");
            if (_pending?.OperationId == operationId)
            {
                _rejected = true;
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            Guid actorAppUserId,
            Guid organizationId,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            events.Add("recovery-remove");
            if (_pending?.OperationId == operationId)
            {
                _pending = null;
                _rejected = false;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeCommand(List<string> events) : ICreateObservationCommand
    {
        public CreateObservationResult Result { get; set; } =
            CreateObservationResult.Failed(
                CreateObservationFailureKind.Transient,
                "not-configured");

        public int ExecuteCount { get; private set; }
        public List<Guid> OperationIds { get; } = new();

        public Task<CreateObservationResult> ExecuteAsync(
            CreateObservationRequest request,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default)
        {
            events.Add("command-execute");
            ExecuteCount++;
            OperationIds.Add(request.OperationId);
            return Task.FromResult(Result);
        }
    }
}
