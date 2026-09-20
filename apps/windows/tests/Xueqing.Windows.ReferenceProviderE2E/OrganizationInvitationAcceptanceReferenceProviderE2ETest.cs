using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.ReferenceProviderE2E;

[TestClass]
public sealed class OrganizationInvitationAcceptanceReferenceProviderE2ETest
{
    [TestMethod]
    public async Task New_external_identity_accepts_invitation_idempotently_without_teaching_assignment()
    {
        var providerUrl = RequiredEnvironment("XUEQING_REFERENCE_PROVIDER_URL");
        var apiKey = RequiredEnvironment("XUEQING_REFERENCE_API_KEY");
        var ownerToken = RequiredEnvironment("XUEQING_REFERENCE_ACCESS_TOKEN");
        var inviteeToken = RequiredEnvironment("XUEQING_REFERENCE_INVITEE_ACCESS_TOKEN");
        var projectUri = new Uri(providerUrl, UriKind.Absolute);
        using var httpClient = new HttpClient();

        ValueTask<string?> OwnerToken(CancellationToken _) =>
            ValueTask.FromResult<string?>(ownerToken);
        ValueTask<string?> InviteeToken(CancellationToken _) =>
            ValueTask.FromResult<string?>(inviteeToken);

        var ownerManagement = new PostgrestOrganizationManagementReader(
            httpClient,
            projectUri,
            apiKey,
            OwnerToken);
        var before = await ownerManagement.ReadAsync(OrganizationId);
        Assert.IsTrue(before.IsSuccess, before.Failure?.Code);
        Assert.IsNotNull(before.Snapshot);

        var createInvitation = new PostgrestOrganizationInvitationCommand(
            httpClient,
            projectUri,
            apiKey,
            OwnerToken);
        var created = await createInvitation.ExecuteAsync(
            new CreateOrganizationInvitationRequest(
                CreateOperationId,
                OrganizationId,
                InviteeEmail,
                OrganizationInvitationTargetRole.Teacher,
                true),
            before.Snapshot.ActorAppUserId);
        Assert.IsTrue(created.IsSuccess, created.Failure?.Code);
        Assert.IsNotNull(created.Receipt);

        var accept = new PostgrestAcceptOrganizationInvitationCommand(
            httpClient,
            projectUri,
            apiKey,
            InviteeToken);
        var request = new AcceptOrganizationInvitationRequest(
            AcceptOperationId,
            created.Receipt.InvitationId,
            "验收新教师");

        var first = await accept.ExecuteAsync(request);
        Assert.IsTrue(first.IsSuccess, first.Failure?.Code);
        Assert.IsNotNull(first.Receipt);
        Assert.AreEqual(OrganizationMembershipRole.Teacher, first.Receipt.MembershipRole);
        Assert.IsTrue(first.Receipt.CanTeach);

        var replay = await accept.ExecuteAsync(request);
        Assert.IsTrue(replay.IsSuccess, replay.Failure?.Code);
        Assert.IsNotNull(replay.Receipt);
        Assert.AreEqual(first.Receipt.ActorAppUserId, replay.Receipt.ActorAppUserId);
        Assert.AreEqual(first.Receipt.ServerCommittedAt, replay.Receipt.ServerCommittedAt);

        var inviteeBootstrap = new PostgrestPersonalBootstrapReader(
            httpClient,
            projectUri,
            apiKey,
            InviteeToken);
        var bootstrap = await inviteeBootstrap.ReadAsync();
        Assert.IsTrue(bootstrap.IsSuccess, bootstrap.Failure?.Code);
        Assert.IsNotNull(bootstrap.Snapshot);
        Assert.AreEqual(first.Receipt.ActorAppUserId, bootstrap.Snapshot.ActorAppUserId);
        Assert.IsTrue(bootstrap.Snapshot.Organizations.Any(
            organization => organization.OrganizationId == OrganizationId));
        Assert.AreEqual(
            0,
            bootstrap.Snapshot.TeachingContexts.Count,
            "Invitation acceptance may grant can_teach but must not create a StudentTeacherAssignment.");

        var after = await ownerManagement.ReadAsync(OrganizationId);
        Assert.IsTrue(after.IsSuccess, after.Failure?.Code);
        Assert.IsNotNull(after.Snapshot);
        Assert.AreEqual(before.Snapshot.Members.Count + 1, after.Snapshot.Members.Count);
        Assert.IsTrue(after.Snapshot.Members.Any(member =>
            member.AppUserId == first.Receipt.ActorAppUserId &&
            member.MembershipRole == OrganizationMembershipRole.Teacher &&
            member.MembershipStatus == OrganizationMembershipStatus.Active &&
            member.CanTeach));
    }

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Required E2E environment variable '{name}' is missing.")
            : value;
    }

    private const string InviteeEmail = "windows.acceptance@example.com";

    private static readonly Guid OrganizationId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static readonly Guid CreateOperationId =
        Guid.Parse("97000000-0000-0000-0000-00000000e201");

    private static readonly Guid AcceptOperationId =
        Guid.Parse("97000000-0000-0000-0000-00000000e202");
}
