using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class CaseLifecycleRecoveryStoreTests
{
    private static readonly byte[] TestMasterKey =
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    [TestMethod]
    public async Task Reopen_restores_exact_typed_intent_and_operation_id()
    {
        var path = CreateDatabasePath();
        var intent = new ReopenCaseRecoveryIntent(Reopen());

        var first = new SqliteCaseLifecycleRecoveryStore(path, TestMasterKey);
        await first.SaveAsync(intent);

        var reopened = new SqliteCaseLifecycleRecoveryStore(path, TestMasterKey);
        Assert.AreEqual(
            intent,
            await reopened.FindByCaseAsync(intent.OrganizationId, intent.CaseId));
        CollectionAssert.AreEqual(
            new CaseLifecycleRecoveryIntent[] { intent },
            (await reopened.ListPendingAsync(intent.OrganizationId)).ToArray());
    }

    [TestMethod]
    public async Task Same_operation_is_idempotent_but_payload_collision_is_rejected()
    {
        var store = new SqliteCaseLifecycleRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var intent = new TransitionCaseRecoveryIntent(Transition());

        await store.SaveAsync(intent);
        await store.SaveAsync(intent);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(
                new TransitionCaseRecoveryIntent(
                    intent.Request with
                    {
                        TargetState = LearningCaseState.Intervening,
                    })));
    }

    [TestMethod]
    public async Task One_case_has_one_pending_lifecycle_intent_and_rejection_frees_it()
    {
        var store = new SqliteCaseLifecycleRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var transition = new TransitionCaseRecoveryIntent(Transition());
        await store.SaveAsync(transition);

        var competing = new CloseCaseRecoveryIntent(
            Close() with
            {
                OperationId =
                    Guid.Parse("82000000-0000-0000-0000-000000000099"),
            });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(competing));

        await store.MarkRejectedAsync(transition.OperationId);
        Assert.IsNull(
            await store.FindByCaseAsync(
                transition.OrganizationId,
                transition.CaseId));

        await store.SaveAsync(competing);
        Assert.AreEqual(
            competing,
            await store.FindByCaseAsync(
                competing.OrganizationId,
                competing.CaseId));
    }

    [TestMethod]
    public async Task Encrypted_reopen_recovery_does_not_expose_teacher_text_as_plaintext()
    {
        var path = CreateDatabasePath();
        var request = Reopen() with
        {
            NewPrimaryActionText = "仅用于加密验证的虚构重开行动XYZ",
        };
        var store = new SqliteCaseLifecycleRecoveryStore(path, TestMasterKey);

        await store.SaveAsync(new ReopenCaseRecoveryIntent(request));

        AssertFileDoesNotContain(path, request.NewPrimaryActionText);
        var walPath = path + "-wal";
        if (File.Exists(walPath))
        {
            AssertFileDoesNotContain(walPath, request.NewPrimaryActionText);
        }
    }

    [TestMethod]
    public async Task Invalid_formal_intent_is_rejected_before_persistence()
    {
        var store = new SqliteCaseLifecycleRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.SaveAsync(
                new TransitionCaseRecoveryIntent(
                    Transition() with { TargetState = LearningCaseState.Closed })));

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.SaveAsync(
                new ReopenCaseRecoveryIntent(
                    Reopen() with { NewPrimaryActionText = " " })));
    }

    private static TransitionLearningCaseStateRequest Transition() =>
        new(
            Guid.Parse("82000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            4,
            LearningCaseState.Stable);

    private static CloseLearningCaseRequest Close() =>
        new(
            Guid.Parse("82000000-0000-0000-0000-000000000002"),
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
            Guid.Parse("82000000-0000-0000-0000-000000000003"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            6,
            "重新检查陌生材料",
            new DateOnly(2026, 9, 29));

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
        return Path.Combine(directory, "case-lifecycle-recovery.db");
    }
}
