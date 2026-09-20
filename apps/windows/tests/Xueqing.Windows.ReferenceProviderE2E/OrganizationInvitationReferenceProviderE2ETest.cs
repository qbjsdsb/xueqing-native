using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.ReferenceProviderE2E;

[TestClass]
public sealed class OrganizationInvitationReferenceProviderE2ETest
{
    [TestMethod]
    public async Task Owner_creates_and_delivers_idempotent_invitation_without_creating_membership()
    {
        var providerUrl = RequiredEnvironment("XUEQING_REFERENCE_PROVIDER_URL");
        var apiKey = RequiredEnvironment("XUEQING_REFERENCE_API_KEY");
        var accessToken = RequiredEnvironment("XUEQING_REFERENCE_ACCESS_TOKEN");
        var projectUri = new Uri(providerUrl, UriKind.Absolute);
        using var httpClient = new HttpClient();

        ValueTask<string?> Token(CancellationToken _) =>
            ValueTask.FromResult<string?>(accessToken);

        var managementReader = new PostgrestOrganizationManagementReader(
            httpClient,
            projectUri,
            apiKey,
            Token);
        var before = await managementReader.ReadAsync(OrganizationId);
        Assert.IsTrue(before.IsSuccess, before.Failure?.Code);
        Assert.IsNotNull(before.Snapshot);

        var command = new PostgrestOrganizationInvitationCommand(
            httpClient,
            projectUri,
            apiKey,
            Token);
        var request = new CreateOrganizationInvitationRequest(
            OperationId,
            OrganizationId,
            "windows.e2e.invite@example.com",
            OrganizationInvitationTargetRole.Teacher,
            true);

        var first = await command.ExecuteAsync(
            request,
            before.Snapshot.ActorAppUserId);
        Assert.IsTrue(first.IsSuccess, first.Failure?.Code);
        Assert.IsNotNull(first.Receipt);
        Assert.AreEqual("windows.e2e.invite@example.com", first.Receipt.InvitedEmail);
        Assert.AreEqual(OrganizationInvitationTargetRole.Teacher, first.Receipt.TargetRole);
        Assert.IsTrue(first.Receipt.TargetCanTeach);

        var replay = await command.ExecuteAsync(
            request,
            before.Snapshot.ActorAppUserId);
        Assert.IsTrue(replay.IsSuccess, replay.Failure?.Code);
        Assert.IsNotNull(replay.Receipt);
        Assert.AreEqual(first.Receipt.InvitationId, replay.Receipt.InvitationId);
        Assert.AreEqual(first.Receipt.ServerCommittedAt, replay.Receipt.ServerCommittedAt);

        var delivery = new SupabaseOrganizationInvitationDeliveryCommand(
            httpClient,
            projectUri,
            apiKey,
            Token);
        var deliveryRequest = new DeliverOrganizationInvitationRequest(
            DeliveryOperationId,
            first.Receipt.InvitationId);

        var delivered = await delivery.ExecuteAsync(deliveryRequest);
        Assert.IsTrue(delivered.IsSuccess, delivered.Failure?.Code);
        Assert.IsNotNull(delivered.Receipt);
        Assert.AreEqual(first.Receipt.InvitationId, delivered.Receipt.InvitationId);

        var deliveryReplay = await delivery.ExecuteAsync(deliveryRequest);
        Assert.IsTrue(deliveryReplay.IsSuccess, deliveryReplay.Failure?.Code);
        Assert.IsNotNull(deliveryReplay.Receipt);
        Assert.AreEqual(delivered.Receipt.DeliveryId, deliveryReplay.Receipt.DeliveryId);
        Assert.AreEqual(delivered.Receipt.OperationId, deliveryReplay.Receipt.OperationId);

        var after = await managementReader.ReadAsync(OrganizationId);
        Assert.IsTrue(after.IsSuccess, after.Failure?.Code);
        Assert.IsNotNull(after.Snapshot);
        CollectionAssert.AreEqual(
            before.Snapshot.Members.Select(member => member.AppUserId).ToArray(),
            after.Snapshot.Members.Select(member => member.AppUserId).ToArray(),
            "Creating an invitation must not fabricate a Membership.");
    }

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Required E2E environment variable '{name}' is missing.")
            : value;
    }

    private static readonly Guid OrganizationId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static readonly Guid OperationId =
        Guid.Parse("91000000-0000-0000-0000-00000000e201");

    private static readonly Guid DeliveryOperationId =
        Guid.Parse("94000000-0000-4000-8000-00000000e201");
}
