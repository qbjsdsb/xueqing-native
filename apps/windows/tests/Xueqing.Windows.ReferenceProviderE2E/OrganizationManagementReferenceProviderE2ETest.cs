using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.ReferenceProviderE2E;

[TestClass]
public sealed class OrganizationManagementReferenceProviderE2ETest
{
    [TestMethod]
    public async Task Owner_reads_authoritative_management_roster_without_changing_teaching_scope()
    {
        var providerUrl = RequiredEnvironment("XUEQING_REFERENCE_PROVIDER_URL");
        var apiKey = RequiredEnvironment("XUEQING_REFERENCE_API_KEY");
        var accessToken = RequiredEnvironment("XUEQING_REFERENCE_ACCESS_TOKEN");
        var projectUri = new Uri(providerUrl, UriKind.Absolute);
        using var httpClient = new HttpClient();

        var bootstrapReader = new PostgrestPersonalBootstrapReader(
            httpClient,
            projectUri,
            apiKey,
            _ => ValueTask.FromResult<string?>(accessToken));
        var bootstrapBefore = await bootstrapReader.ReadAsync();
        Assert.IsTrue(bootstrapBefore.IsSuccess, bootstrapBefore.Failure?.Code);
        Assert.IsNotNull(bootstrapBefore.Snapshot);

        var organization = bootstrapBefore.Snapshot.Organizations.Single(
            item => item.OrganizationId == OrganizationId);
        Assert.IsTrue(organization.CanTeach);

        var reader = new PostgrestOrganizationManagementReader(
            httpClient,
            projectUri,
            apiKey,
            _ => ValueTask.FromResult<string?>(accessToken));
        var management = await reader.ReadAsync(OrganizationId);

        Assert.IsTrue(management.IsSuccess, management.Failure?.Code);
        Assert.IsNotNull(management.Snapshot);
        Assert.AreEqual(OrganizationMembershipRole.Owner, management.Snapshot.ActorMembershipRole);
        Assert.AreEqual(3, management.Snapshot.Members.Count);
        Assert.IsTrue(management.Snapshot.Capabilities.CanInviteAdmin);
        Assert.IsTrue(management.Snapshot.Capabilities.CanInviteTeacher);
        Assert.IsFalse(management.Snapshot.Capabilities.CanInviteOwner);
        Assert.IsTrue(management.Snapshot.Members.Any(member =>
            member.AppUserId == Guid.Parse("10000000-0000-0000-0000-000000000003") &&
            member.MembershipStatus == OrganizationMembershipStatus.Disabled));

        var bootstrapAfter = await bootstrapReader.ReadAsync();
        Assert.IsTrue(bootstrapAfter.IsSuccess, bootstrapAfter.Failure?.Code);
        Assert.IsNotNull(bootstrapAfter.Snapshot);
        CollectionAssert.AreEqual(
            bootstrapBefore.Snapshot.TeachingContexts.Select(context => context.AssignmentId).ToArray(),
            bootstrapAfter.Snapshot.TeachingContexts.Select(context => context.AssignmentId).ToArray(),
            "Reading Organization Management must not fabricate or mutate teaching assignments.");
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
}
