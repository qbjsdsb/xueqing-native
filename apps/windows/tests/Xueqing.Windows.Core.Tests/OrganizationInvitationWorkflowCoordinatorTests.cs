using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class OrganizationInvitationWorkflowCoordinatorTests
{
    [TestMethod]
    public async Task Successful_flow_persists_before_create_then_removes_after_sent()
    {
        var recovery = new MemoryRecoveryStore();
        var create = new StubCreateCommand
        {
            Result = CreateSuccess(),
            OnExecute = () => Assert.AreEqual(1, recovery.Items.Count),
        };
        var deliver = new StubDeliveryCommand
        {
            Result = DeliverySuccess(),
            OnExecute = request =>
            {
                var persisted = recovery.Items.Single();
                Assert.AreEqual(
                    OrganizationInvitationRecoveryStage.DeliveryPending,
                    persisted.Stage);
                Assert.AreEqual(InvitationId, persisted.InvitationId);
                Assert.AreEqual(persisted.DeliveryOperationId, request.OperationId);
            },
        };
        var coordinator = new OrganizationInvitationWorkflowCoordinator(
            create,
            deliver,
            recovery);

        var result = await coordinator.StartAsync(
            CreateRequest(),
            DeliveryOperationId,
            ActorId);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, recovery.Items.Count);
        Assert.AreEqual(1, create.CallCount);
        Assert.AreEqual(1, deliver.CallCount);
    }

    [TestMethod]
    public async Task Unknown_create_result_is_replayed_with_same_operation()
    {
        var recovery = new MemoryRecoveryStore();
        var create = new StubCreateCommand
        {
            Result = CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.ResultUnknown,
                "XQ_RESULT_UNKNOWN_NETWORK"),
        };
        var deliver = new StubDeliveryCommand();
        var coordinator = new OrganizationInvitationWorkflowCoordinator(
            create,
            deliver,
            recovery);

        var first = await coordinator.StartAsync(
            CreateRequest(),
            DeliveryOperationId,
            ActorId);

        Assert.AreEqual(
            OrganizationInvitationWorkflowOutcome.CreatePendingConfirmation,
            first.Outcome);
        var pending = recovery.Items.Single();
        Assert.AreEqual(CreateRequest().OperationId, pending.CreateOperationId);
        Assert.AreEqual(0, deliver.CallCount);

        create.Result = CreateSuccess();
        deliver.Result = DeliverySuccess();
        var resumed = await coordinator.ResumeAsync(pending, ActorId);

        Assert.IsTrue(resumed.IsSuccess);
        Assert.AreEqual(2, create.CallCount);
        CollectionAssert.AreEqual(
            new[] { CreateRequest().OperationId, CreateRequest().OperationId },
            create.OperationIds.ToArray());
    }

    [TestMethod]
    public async Task Unknown_delivery_result_never_invents_another_delivery_operation()
    {
        var recovery = new MemoryRecoveryStore();
        var create = new StubCreateCommand { Result = CreateSuccess() };
        var deliver = new StubDeliveryCommand
        {
            Result = DeliverOrganizationInvitationResult.Failed(
                DeliverOrganizationInvitationFailureKind.ResultUnknown,
                "XQ_INVITATION_DELIVERY_RESULT_UNKNOWN"),
        };
        var coordinator = new OrganizationInvitationWorkflowCoordinator(
            create,
            deliver,
            recovery);

        var first = await coordinator.StartAsync(
            CreateRequest(),
            DeliveryOperationId,
            ActorId);

        Assert.AreEqual(
            OrganizationInvitationWorkflowOutcome.DeliveryPendingConfirmation,
            first.Outcome);
        var pending = recovery.Items.Single();
        Assert.AreEqual(DeliveryOperationId, pending.DeliveryOperationId);

        deliver.Result = DeliverySuccess();
        var resumed = await coordinator.ResumeAsync(pending, ActorId);

        Assert.IsTrue(resumed.IsSuccess);
        Assert.AreEqual(2, deliver.CallCount);
        CollectionAssert.AreEqual(
            new[] { DeliveryOperationId, DeliveryOperationId },
            deliver.OperationIds.ToArray());
    }

    [TestMethod]
    public async Task Known_provider_rejection_becomes_terminal_without_auto_resend()
    {
        var recovery = new MemoryRecoveryStore();
        var create = new StubCreateCommand { Result = CreateSuccess() };
        var deliver = new StubDeliveryCommand
        {
            Result = DeliverOrganizationInvitationResult.Failed(
                DeliverOrganizationInvitationFailureKind.ProviderRejected,
                "XQ_PROVIDER_DELIVERY_REJECTED"),
        };
        var coordinator = new OrganizationInvitationWorkflowCoordinator(
            create,
            deliver,
            recovery);

        var first = await coordinator.StartAsync(
            CreateRequest(),
            DeliveryOperationId,
            ActorId);

        Assert.AreEqual(
            OrganizationInvitationWorkflowOutcome.DeliveryFailed,
            first.Outcome);
        var terminal = recovery.Items.Single();
        Assert.AreEqual(
            OrganizationInvitationRecoveryStage.DeliveryFailed,
            terminal.Stage);

        var resumed = await coordinator.ResumeAsync(terminal, ActorId);

        Assert.AreEqual(
            OrganizationInvitationWorkflowOutcome.DeliveryFailed,
            resumed.Outcome);
        Assert.AreEqual(1, deliver.CallCount);
    }

    [TestMethod]
    public async Task Local_persistence_failure_prevents_network_side_effect()
    {
        var recovery = new MemoryRecoveryStore { FailNextSave = true };
        var create = new StubCreateCommand { Result = CreateSuccess() };
        var deliver = new StubDeliveryCommand { Result = DeliverySuccess() };
        var coordinator = new OrganizationInvitationWorkflowCoordinator(
            create,
            deliver,
            recovery);

        var result = await coordinator.StartAsync(
            CreateRequest(),
            DeliveryOperationId,
            ActorId);

        Assert.AreEqual(
            OrganizationInvitationWorkflowOutcome.LocalDurabilityFailure,
            result.Outcome);
        Assert.AreEqual(0, create.CallCount);
        Assert.AreEqual(0, deliver.CallCount);
    }

    private static CreateOrganizationInvitationRequest CreateRequest() =>
        new(
            Guid.Parse("91000000-0000-4000-8000-00000000f001"),
            OrganizationId,
            "invite.teacher@example.com",
            OrganizationInvitationTargetRole.Teacher,
            true);

    private static CreateOrganizationInvitationResult CreateSuccess() =>
        CreateOrganizationInvitationResult.Success(
            new CreateOrganizationInvitationReceipt(
                CreateRequest().OperationId,
                InvitationId,
                ActorId,
                OrganizationId,
                CreateRequest().InvitedEmail,
                CreateRequest().TargetRole,
                CreateRequest().TargetCanTeach,
                DateTimeOffset.Parse("2026-09-28T00:00:00Z"),
                DateTimeOffset.Parse("2026-09-21T00:00:00Z")));

    private static DeliverOrganizationInvitationResult DeliverySuccess() =>
        DeliverOrganizationInvitationResult.Success(
            new DeliverOrganizationInvitationReceipt(
                DeliveryOperationId,
                Guid.Parse("95000000-0000-4000-8000-00000000f001"),
                InvitationId));

    private static readonly Guid ActorId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static readonly Guid OrganizationId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static readonly Guid InvitationId =
        Guid.Parse("92000000-0000-4000-8000-00000000f001");

    private static readonly Guid DeliveryOperationId =
        Guid.Parse("94000000-0000-4000-8000-00000000f001");

    private sealed class StubCreateCommand : IOrganizationInvitationCommand
    {
        public CreateOrganizationInvitationResult Result { get; set; } =
            CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.InvalidResponse,
                "NOT_CONFIGURED");

        public int CallCount { get; private set; }
        public List<Guid> OperationIds { get; } = new();
        public Action? OnExecute { get; init; }

        public Task<CreateOrganizationInvitationResult> ExecuteAsync(
            CreateOrganizationInvitationRequest request,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default)
        {
            Assert.IsTrue(expectedActorAppUserId == ActorId);
            CallCount++;
            OperationIds.Add(request.OperationId);
            OnExecute?.Invoke();
            return Task.FromResult(Result);
        }
    }

    private sealed class StubDeliveryCommand : IOrganizationInvitationDeliveryCommand
    {
        public DeliverOrganizationInvitationResult Result { get; set; } =
            DeliverOrganizationInvitationResult.Failed(
                DeliverOrganizationInvitationFailureKind.InvalidResponse,
                "NOT_CONFIGURED");

        public int CallCount { get; private set; }
        public List<Guid> OperationIds { get; } = new();
        public Action<DeliverOrganizationInvitationRequest>? OnExecute { get; init; }

        public Task<DeliverOrganizationInvitationResult> ExecuteAsync(
            DeliverOrganizationInvitationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            OperationIds.Add(request.OperationId);
            OnExecute?.Invoke(request);
            return Task.FromResult(Result);
        }
    }

    private sealed class MemoryRecoveryStore : IOrganizationInvitationRecoveryStore
    {
        public List<OrganizationInvitationRecoveryIntent> Items { get; } = new();
        public bool FailNextSave { get; set; }

        public Task SaveAsync(
            Guid actorAppUserId,
            OrganizationInvitationRecoveryIntent intent,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(ActorId, actorAppUserId);
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("Fictional local durability failure.");
            }

            Items.RemoveAll(item => item.CreateOperationId == intent.CreateOperationId);
            Items.Add(intent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OrganizationInvitationRecoveryIntent>> ListAsync(
            Guid actorAppUserId,
            Guid organizationId,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(ActorId, actorAppUserId);
            return Task.FromResult<IReadOnlyList<OrganizationInvitationRecoveryIntent>>(
                Items.Where(item => item.OrganizationId == organizationId).ToArray());
        }

        public Task RemoveAsync(
            Guid actorAppUserId,
            Guid organizationId,
            Guid createOperationId,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(ActorId, actorAppUserId);
            Items.RemoveAll(item =>
                item.OrganizationId == organizationId &&
                item.CreateOperationId == createOperationId);
            return Task.CompletedTask;
        }
    }
}
