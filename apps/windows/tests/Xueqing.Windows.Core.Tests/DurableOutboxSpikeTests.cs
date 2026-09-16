using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Sync;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class DurableOutboxSpikeTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] TestMasterKey = SHA256.HashData(
        Encoding.UTF8.GetBytes("fictional-xueqing-outbox-master-key-v1"));

    [TestMethod]
    public async Task Same_operation_id_is_idempotent_but_collision_is_rejected()
    {
        var store = CreateStore();
        var operationId = Guid.NewGuid();
        var intent = CreateIntent(operationId, "{\"value\":1}");

        var first = await store.EnqueueAsync(intent);
        var second = await store.EnqueueAsync(intent with { CreatedAtClientUtc = BaseTime.AddMinutes(1) });

        Assert.AreEqual(OutboxEnqueueDisposition.Inserted, first.Disposition);
        Assert.AreEqual(OutboxEnqueueDisposition.AlreadyPresent, second.Disposition);
        Assert.AreEqual(1L, await store.CountAsync());

        await Assert.ThrowsExactlyAsync<OutboxOperationIdCollisionException>(
            () => store.EnqueueAsync(intent with { PayloadJson = "{\"value\":2}" }));
    }

    [TestMethod]
    public async Task Durable_intent_survives_store_reopen()
    {
        var databasePath = CreateDatabasePath();
        var operationId = Guid.NewGuid();

        var firstStore = CreateStore(databasePath);
        await firstStore.EnqueueAsync(CreateIntent(operationId));

        var reopenedStore = CreateStore(databasePath);
        var restored = await reopenedStore.GetAsync(operationId);

        Assert.IsNotNull(restored);
        Assert.AreEqual(operationId, restored.Intent.OperationId);
        Assert.AreEqual(OutboxQueueStatus.Pending, restored.QueueStatus);
        Assert.AreEqual(0, restored.AttemptCount);
    }

    [TestMethod]
    public async Task Claim_is_exclusive_and_expired_lease_is_recoverable_after_reopen()
    {
        var databasePath = CreateDatabasePath();
        var operationId = Guid.NewGuid();
        var firstStore = CreateStore(databasePath);
        await firstStore.EnqueueAsync(CreateIntent(operationId));

        var firstClaim = await firstStore.TryClaimNextReadyAsync(BaseTime, TimeSpan.FromMinutes(1));
        Assert.IsNotNull(firstClaim);
        Assert.AreEqual(1, firstClaim.AttemptCount);
        Assert.AreEqual(OutboxQueueStatus.InFlight, firstClaim.QueueStatus);
        Assert.IsNotNull(firstClaim.LeaseId);

        var unavailable = await firstStore.TryClaimNextReadyAsync(BaseTime.AddSeconds(30), TimeSpan.FromMinutes(1));
        Assert.IsNull(unavailable);

        var reopenedStore = CreateStore(databasePath);
        var recoveredClaim = await reopenedStore.TryClaimNextReadyAsync(BaseTime.AddMinutes(2), TimeSpan.FromMinutes(1));
        Assert.IsNotNull(recoveredClaim);
        Assert.AreEqual(operationId, recoveredClaim.Intent.OperationId);
        Assert.AreEqual(2, recoveredClaim.AttemptCount);
        Assert.AreNotEqual(firstClaim.LeaseId, recoveredClaim.LeaseId);

        await Assert.ThrowsExactlyAsync<OutboxLeaseLostException>(
            () => reopenedStore.MarkAcknowledgedAsync(
                operationId,
                firstClaim.LeaseId!.Value,
                BaseTime.AddMinutes(2),
                "{\"receipt\":\"stale\"}"));

        var acknowledged = await reopenedStore.MarkAcknowledgedAsync(
            operationId,
            recoveredClaim.LeaseId!.Value,
            BaseTime.AddMinutes(2),
            "{\"receipt\":\"ok\"}");

        Assert.AreEqual(OutboxQueueStatus.Acknowledged, acknowledged.QueueStatus);
    }

    [TestMethod]
    public async Task Retry_is_not_claimable_before_due_time()
    {
        var store = CreateStore();
        var operationId = Guid.NewGuid();
        await store.EnqueueAsync(CreateIntent(operationId));

        var firstClaim = await store.TryClaimNextReadyAsync(BaseTime, TimeSpan.FromMinutes(1));
        Assert.IsNotNull(firstClaim);

        var retryAt = BaseTime.AddMinutes(5);
        var retry = await store.ScheduleRetryAsync(
            operationId,
            firstClaim.LeaseId!.Value,
            "network_timeout",
            retryAt);

        Assert.AreEqual(OutboxQueueStatus.Retry, retry.QueueStatus);
        Assert.AreEqual("network_timeout", retry.LastErrorClass);
        Assert.AreEqual(retryAt, retry.NextAttemptAtUtc);

        Assert.IsNull(await store.TryClaimNextReadyAsync(BaseTime.AddMinutes(4), TimeSpan.FromMinutes(1)));

        var secondClaim = await store.TryClaimNextReadyAsync(retryAt, TimeSpan.FromMinutes(1));
        Assert.IsNotNull(secondClaim);
        Assert.AreEqual(2, secondClaim.AttemptCount);
    }

    [TestMethod]
    public async Task Concurrent_claimers_only_obtain_one_active_lease()
    {
        var store = CreateStore();
        await store.EnqueueAsync(CreateIntent(Guid.NewGuid()));

        var claims = await Task.WhenAll(
            Enumerable.Range(0, 16)
                .Select(_ => store.TryClaimNextReadyAsync(BaseTime, TimeSpan.FromMinutes(1))));

        Assert.AreEqual(1, claims.Count(claim => claim is not null));
        Assert.AreEqual(1L, await store.CountAsync());
    }

    [TestMethod]
    public async Task Acknowledged_operation_is_terminal_and_preserves_server_receipt()
    {
        var store = CreateStore();
        var operationId = Guid.NewGuid();
        await store.EnqueueAsync(CreateIntent(operationId));

        var claim = await store.TryClaimNextReadyAsync(BaseTime, TimeSpan.FromMinutes(1));
        Assert.IsNotNull(claim);

        var acknowledged = await store.MarkAcknowledgedAsync(
            operationId,
            claim.LeaseId!.Value,
            BaseTime.AddSeconds(2),
            "{\"caseVersion\":42}");

        Assert.AreEqual(OutboxQueueStatus.Acknowledged, acknowledged.QueueStatus);
        Assert.AreEqual("{\"caseVersion\":42}", acknowledged.ServerReceiptJson);
        Assert.IsNull(acknowledged.LeaseId);
        Assert.IsNull(await store.TryClaimNextReadyAsync(BaseTime.AddDays(1), TimeSpan.FromMinutes(1)));
    }

    [TestMethod]
    public async Task Dead_letter_operation_is_terminal()
    {
        var store = CreateStore();
        var operationId = Guid.NewGuid();
        await store.EnqueueAsync(CreateIntent(operationId));

        var claim = await store.TryClaimNextReadyAsync(BaseTime, TimeSpan.FromMinutes(1));
        Assert.IsNotNull(claim);

        var deadLetter = await store.MoveToDeadLetterAsync(
            operationId,
            claim.LeaseId!.Value,
            "validation_terminal");

        Assert.AreEqual(OutboxQueueStatus.DeadLetter, deadLetter.QueueStatus);
        Assert.AreEqual("validation_terminal", deadLetter.LastErrorClass);
        Assert.IsNull(await store.TryClaimNextReadyAsync(BaseTime.AddDays(1), TimeSpan.FromMinutes(1)));
    }

    private static SqliteDurableOutboxStore CreateStore()
        => CreateStore(CreateDatabasePath());

    private static SqliteDurableOutboxStore CreateStore(string databasePath)
        => new(databasePath, TestMasterKey);

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "xueqing-native-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "xueqing-spike.db");
    }

    private static OutboxCommandIntent CreateIntent(Guid operationId, string payloadJson = "{\"value\":1}")
        => new(
            operationId,
            "append_evidence",
            "case-fictional-001",
            "org-fictional-001/student-fictional-001/subject-chinese",
            7,
            "assignment-version-3",
            payloadJson,
            BaseTime);
}
