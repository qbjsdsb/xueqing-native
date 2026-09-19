using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class ActionProgressionRecoveryStoreTests
{
    private static readonly byte[] TestMasterKey =
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    [TestMethod]
    public async Task Reopen_restores_exact_typed_intents_and_same_operation_ids()
    {
        var path = CreateDatabasePath();
        var reschedule = new ReschedulePrimaryActionRecoveryIntent(Reschedule());
        var verification = new VerificationAndNextActionRecoveryIntent(
            Verification() with
            {
                OperationId = Guid.Parse("80000000-0000-0000-0000-000000000002"),
                CurrentPrimaryActionId = Guid.Parse("70000000-0000-0000-0000-000000000002"),
            });

        var first = new SqliteActionProgressionRecoveryStore(path, TestMasterKey);
        await first.SaveAsync(reschedule);
        await first.SaveAsync(verification);

        var reopened = new SqliteActionProgressionRecoveryStore(path, TestMasterKey);
        var pending = await reopened.ListPendingAsync(reschedule.OrganizationId);

        Assert.AreEqual(2, pending.Count);
        CollectionAssert.AreEquivalent(
            new ActionProgressionRecoveryIntent[] { reschedule, verification },
            pending.ToArray());
        Assert.AreEqual(
            reschedule,
            await reopened.FindByPrimaryActionAsync(
                reschedule.OrganizationId,
                reschedule.CaseId,
                reschedule.PrimaryActionId));
    }

    [TestMethod]
    public async Task Same_operation_is_idempotent_but_payload_collision_is_rejected()
    {
        var store = new SqliteActionProgressionRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var intent = new ReschedulePrimaryActionRecoveryIntent(Reschedule());

        await store.SaveAsync(intent);
        await store.SaveAsync(intent);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(
                new ReschedulePrimaryActionRecoveryIntent(
                    intent.Request with { NewDueOn = new DateOnly(2026, 9, 30) })));
    }

    [TestMethod]
    public async Task One_primary_action_has_one_pending_intent_and_rejection_frees_it()
    {
        var store = new SqliteActionProgressionRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var reschedule = new ReschedulePrimaryActionRecoveryIntent(Reschedule());

        await store.SaveAsync(reschedule);

        var competing = new VerificationAndNextActionRecoveryIntent(
            Verification() with
            {
                OperationId = Guid.Parse("80000000-0000-0000-0000-000000000099"),
            });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(competing));

        await store.MarkRejectedAsync(reschedule.OperationId);
        Assert.IsNull(await store.FindByPrimaryActionAsync(
            reschedule.OrganizationId,
            reschedule.CaseId,
            reschedule.PrimaryActionId));

        await store.SaveAsync(competing);
        Assert.AreEqual(
            competing,
            await store.FindByPrimaryActionAsync(
                competing.OrganizationId,
                competing.CaseId,
                competing.PrimaryActionId));
    }

    [TestMethod]
    public async Task Deterministic_rejection_is_hidden_and_remove_clears_recovery()
    {
        var store = new SqliteActionProgressionRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var intent = new VerificationAndNextActionRecoveryIntent(Verification());

        await store.SaveAsync(intent);
        await store.MarkRejectedAsync(intent.OperationId);

        Assert.AreEqual(
            0,
            (await store.ListPendingAsync(intent.OrganizationId)).Count);

        await store.RemoveAsync(intent.OperationId);
        Assert.AreEqual(
            0,
            (await store.ListPendingAsync(intent.OrganizationId)).Count);
    }

    [TestMethod]
    public async Task Encrypted_recovery_does_not_expose_teacher_text_as_plaintext()
    {
        var path = CreateDatabasePath();
        var request = Verification() with
        {
            VerificationSummary = "仅用于加密验证的虚构复核结论XYZ",
            NextActionText = "仅用于加密验证的虚构下一步行动XYZ",
        };
        var store = new SqliteActionProgressionRecoveryStore(path, TestMasterKey);

        await store.SaveAsync(new VerificationAndNextActionRecoveryIntent(request));

        AssertFileDoesNotContain(path, request.VerificationSummary);
        AssertFileDoesNotContain(path, request.NextActionText);

        var walPath = path + "-wal";
        if (File.Exists(walPath))
        {
            AssertFileDoesNotContain(walPath, request.VerificationSummary);
            AssertFileDoesNotContain(walPath, request.NextActionText);
        }
    }

    [TestMethod]
    public async Task Invalid_formal_intent_is_rejected_before_persistence()
    {
        var store = new SqliteActionProgressionRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.SaveAsync(
                new ReschedulePrimaryActionRecoveryIntent(
                    Reschedule() with { ExpectedActionVersion = 0 })));

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.SaveAsync(
                new VerificationAndNextActionRecoveryIntent(
                    Verification() with { VerificationSummary = " " })));
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
            Guid.Parse("80000000-0000-0000-0000-000000000001"),
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

    private static void AssertFileDoesNotContain(string path, string value)
    {
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.IsFalse(text.Contains(value, StringComparison.Ordinal));
    }

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "xueqing-native-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "action-progression-recovery.db");
    }
}
