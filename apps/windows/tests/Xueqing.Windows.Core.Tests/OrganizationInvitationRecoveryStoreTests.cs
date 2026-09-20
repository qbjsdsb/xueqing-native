using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class OrganizationInvitationRecoveryStoreTests
{
    private static readonly byte[] TestMasterKey = SHA256.HashData(
        Encoding.UTF8.GetBytes("fictional-organization-invitation-recovery-key-v1"));

    [TestMethod]
    public async Task Recovery_survives_reopen_and_advances_without_changing_operation_ids()
    {
        var path = CreateDatabasePath();
        var createPending = Intent();

        var first = new SqliteOrganizationInvitationRecoveryStore(path, TestMasterKey);
        await first.SaveAsync(createPending);

        var deliveryPending = createPending.BeginDelivery(InvitationId);
        await first.SaveAsync(deliveryPending);

        var reopened = new SqliteOrganizationInvitationRecoveryStore(path, TestMasterKey);
        var restored = await reopened.ListAsync(createPending.OrganizationId);

        Assert.AreEqual(1, restored.Count);
        Assert.AreEqual(deliveryPending, restored[0]);
        Assert.AreEqual(
            createPending.CreateOperationId,
            restored[0].CreateOperationId);
        Assert.AreEqual(
            createPending.DeliveryOperationId,
            restored[0].DeliveryOperationId);

        await reopened.RemoveAsync(createPending.CreateOperationId);
        Assert.AreEqual(0, (await reopened.ListAsync(createPending.OrganizationId)).Count);
    }

    [TestMethod]
    public async Task Recovery_rejects_payload_collision_rebind_and_stage_regression()
    {
        var store = new SqliteOrganizationInvitationRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var createPending = Intent();

        await store.SaveAsync(createPending);
        await store.SaveAsync(createPending);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(
                createPending with { InvitedEmail = "different@example.com" }));

        var deliveryPending = createPending.BeginDelivery(InvitationId);
        await store.SaveAsync(deliveryPending);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(
                deliveryPending with
                {
                    InvitationId = Guid.Parse("92000000-0000-4000-8000-000000000099"),
                }));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(createPending));
    }

    [TestMethod]
    public async Task Terminal_delivery_failure_is_durable_and_immutable()
    {
        var store = new SqliteOrganizationInvitationRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var failed = Intent()
            .BeginDelivery(InvitationId)
            .MarkDeliveryFailed("XQ_PROVIDER_DELIVERY_REJECTED");

        await store.SaveAsync(failed);
        await store.SaveAsync(failed);

        var restored = (await store.ListAsync(failed.OrganizationId)).Single();
        Assert.AreEqual(
            OrganizationInvitationRecoveryStage.DeliveryFailed,
            restored.Stage);
        Assert.AreEqual(
            "XQ_PROVIDER_DELIVERY_REJECTED",
            restored.TerminalFailureCode);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(
                failed with { TerminalFailureCode = "XQ_OTHER_FAILURE" }));
    }

    [TestMethod]
    public async Task Recovery_database_does_not_expose_invited_email_as_plaintext()
    {
        var path = CreateDatabasePath();
        var intent = Intent() with
        {
            InvitedEmail = "encrypted-invite-marker@example.com",
        };
        var store = new SqliteOrganizationInvitationRecoveryStore(path, TestMasterKey);

        await store.SaveAsync(intent);

        AssertFileDoesNotContain(path, intent.InvitedEmail);
        var walPath = path + "-wal";
        if (File.Exists(walPath))
        {
            AssertFileDoesNotContain(walPath, intent.InvitedEmail);
        }
    }

    [TestMethod]
    public async Task Organization_filter_never_returns_another_scope()
    {
        var store = new SqliteOrganizationInvitationRecoveryStore(
            CreateDatabasePath(),
            TestMasterKey);
        var first = Intent();
        var second = Intent() with
        {
            CreateOperationId = Guid.Parse("91000000-0000-4000-8000-000000000002"),
            DeliveryOperationId = Guid.Parse("94000000-0000-4000-8000-000000000002"),
            OrganizationId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
            InvitedEmail = "other@example.com",
        };

        await store.SaveAsync(first);
        await store.SaveAsync(second);

        CollectionAssert.AreEqual(
            new[] { first },
            (await store.ListAsync(first.OrganizationId)).ToArray());
    }

    private static OrganizationInvitationRecoveryIntent Intent() =>
        OrganizationInvitationRecoveryIntent.Create(
            new CreateOrganizationInvitationRequest(
                Guid.Parse("91000000-0000-4000-8000-000000000001"),
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                "teacher@example.com",
                OrganizationInvitationTargetRole.Teacher,
                true),
            Guid.Parse("94000000-0000-4000-8000-000000000001"));

    private static void AssertFileDoesNotContain(string path, string value)
    {
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "xueqing-native-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "organization-invitation-recovery.db");
    }

    private static readonly Guid InvitationId =
        Guid.Parse("92000000-0000-4000-8000-000000000001");
}
