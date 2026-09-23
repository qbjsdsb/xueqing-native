using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class CompatibilityGatedCommandsTests
{
    [TestMethod]
    public async Task Blocked_compatibility_does_not_invoke_observation_command()
    {
        var inner = new RecordingObservationCommand();
        var gate = new StubGate(new ConsequentialWriteCompatibilityResult(
            ConsequentialWriteCompatibilityKind.Blocked,
            "XQ_CLIENT_VERSION_UNSUPPORTED"));
        var command = new CompatibilityGatedCreateObservationCommand(inner, gate);
        var request = ObservationRequest();

        var result = await command.ExecuteAsync(
            request,
            Guid.Parse("10000000-0000-0000-0000-000000000001"));

        Assert.AreEqual(0, inner.CallCount);
        Assert.AreEqual(CreateObservationFailureKind.CompatibilityBlocked, result.Failure?.Kind);
        Assert.AreEqual("XQ_CLIENT_VERSION_UNSUPPORTED", result.Failure?.Code);
    }

    [TestMethod]
    public async Task Allowed_compatibility_invokes_inner_command_exactly_once()
    {
        var inner = new RecordingObservationCommand();
        var gate = new StubGate(ConsequentialWriteCompatibilityResult.Allowed());
        var command = new CompatibilityGatedCreateObservationCommand(inner, gate);
        var request = ObservationRequest();

        var result = await command.ExecuteAsync(
            request,
            Guid.Parse("10000000-0000-0000-0000-000000000001"));

        Assert.AreEqual(1, inner.CallCount);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(request.OperationId, result.Receipt?.OperationId);
    }

    [TestMethod]
    public async Task Server_gate_maps_policy_unavailable_to_write_unavailable()
    {
        var reader = new StubCompatibilityReader(
            ClientCompatibilityReadResult.Failed(
                ClientCompatibilityFailureKind.PolicyUnavailable,
                "XQ_COMPATIBILITY_POLICY_UNAVAILABLE"));
        var gate = new ServerClientCompatibilityWriteGate(
            reader,
            new ClientCompatibilityRequest("windows", "1.0.0.0", 1));

        var result = await gate.CheckAsync();

        Assert.AreEqual(ConsequentialWriteCompatibilityKind.TemporarilyUnavailable, result.Kind);
        Assert.AreEqual("XQ_COMPATIBILITY_POLICY_UNAVAILABLE", result.Code);
    }

    [TestMethod]
    public async Task Server_gate_allows_update_recommended_but_blocks_security_blocked()
    {
        var reader = new MutableCompatibilityReader();
        var gate = new ServerClientCompatibilityWriteGate(
            reader,
            new ClientCompatibilityRequest("windows", "1.0.0.0", 1));

        reader.Result = Success(ClientCompatibilityState.UpdateRecommended, "XQ_UPDATE_RECOMMENDED");
        Assert.AreEqual(
            ConsequentialWriteCompatibilityKind.Allowed,
            (await gate.CheckAsync()).Kind);

        reader.Result = Success(ClientCompatibilityState.SecurityBlocked, "XQ_CLIENT_SECURITY_BLOCKED");
        var blocked = await gate.CheckAsync();
        Assert.AreEqual(ConsequentialWriteCompatibilityKind.Blocked, blocked.Kind);
        Assert.AreEqual("XQ_CLIENT_SECURITY_BLOCKED", blocked.Code);
    }

    private static ClientCompatibilityReadResult Success(
        ClientCompatibilityState state,
        string reason) =>
        ClientCompatibilityReadResult.Success(
            new ClientCompatibilityDecision(
                DateTimeOffset.Parse("2026-09-23T04:00:00Z"),
                "v1-initial",
                "windows",
                "1.0.0.0",
                1,
                state,
                reason,
                "0.9.0.0",
                "1.0.0.0",
                1,
                1,
                new Uri("https://github.com/qbjsdsb/xueqing-native/releases")));

    private static CreateObservationRequest ObservationRequest() =>
        new(
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            "兼容 Gate 阻断时这段草稿不能被远端提交");

    private sealed class StubGate(ConsequentialWriteCompatibilityResult result) :
        IConsequentialWriteCompatibilityGate
    {
        public Task<ConsequentialWriteCompatibilityResult> CheckAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class RecordingObservationCommand : ICreateObservationCommand
    {
        public int CallCount { get; private set; }

        public Task<CreateObservationResult> ExecuteAsync(
            CreateObservationRequest request,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(
                CreateObservationResult.Success(
                    new CreateObservationReceipt(
                        request.OperationId,
                        Guid.Parse("71000000-0000-0000-0000-000000000001"),
                        expectedActorAppUserId,
                        request.OrganizationId,
                        request.StudentId,
                        request.SubjectProfileId,
                        "chinese",
                        DateTimeOffset.Parse("2026-09-23T04:00:00Z"))));
        }
    }

    private sealed class StubCompatibilityReader(ClientCompatibilityReadResult result) :
        IClientCompatibilityReader
    {
        public Task<ClientCompatibilityReadResult> ReadAsync(
            ClientCompatibilityRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class MutableCompatibilityReader : IClientCompatibilityReader
    {
        public ClientCompatibilityReadResult Result { get; set; } =
            ClientCompatibilityReadResult.Failed(
                ClientCompatibilityFailureKind.InvalidResponse,
                "XQ_TEST_NOT_CONFIGURED");

        public Task<ClientCompatibilityReadResult> ReadAsync(
            ClientCompatibilityRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }
}
