using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Agent;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class AgentNavigationRequestTests
{
    private static readonly Guid StudentId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");

    private static readonly Guid CaseId =
        Guid.Parse("22222222-2222-4222-8222-222222222222");

    [TestMethod]
    public void Today_route_is_navigation_only()
    {
        Assert.IsTrue(
            AgentNavigationRequest.TryParse(
                new Uri("xueqing://today"),
                out var request));

        Assert.AreEqual(AgentNavigationTargetKind.Today, request!.Kind);
        Assert.IsNull(request.EntityId);
        Assert.AreEqual("xueqing://today/", request.ToUri().AbsoluteUri);
    }

    [TestMethod]
    public void Student_route_accepts_one_canonical_application_id()
    {
        Assert.IsTrue(
            AgentNavigationRequest.TryParse(
                new Uri($"xueqing://student/{StudentId:D}"),
                out var request));

        Assert.AreEqual(AgentNavigationTargetKind.Student, request!.Kind);
        Assert.AreEqual(StudentId, request.EntityId);
    }

    [TestMethod]
    public void Learning_route_accepts_one_canonical_application_id()
    {
        Assert.IsTrue(
            AgentNavigationRequest.TryParse(
                new Uri($"xueqing://learning/{CaseId:D}"),
                out var request));

        Assert.AreEqual(AgentNavigationTargetKind.LearningCase, request!.Kind);
        Assert.AreEqual(CaseId, request.EntityId);
    }

    [TestMethod]
    [DataRow("https://today")]
    [DataRow("xueqing://unknown")]
    [DataRow("xueqing://today/extra")]
    [DataRow("xueqing://student/not-a-guid")]
    [DataRow("xueqing://student:1234/11111111-1111-4111-8111-111111111111")]
    [DataRow("xueqing://student/11111111-1111-4111-8111-111111111111/extra")]
    [DataRow("xueqing://student/11111111-1111-4111-8111-111111111111?organization=aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")]
    [DataRow("xueqing://learning/22222222-2222-4222-8222-222222222222#submit")]
    public void Non_contract_routes_fail_closed(string value)
    {
        Assert.IsFalse(
            AgentNavigationRequest.TryParse(
                new Uri(value),
                out var request));
        Assert.IsNull(request);
    }

    [TestMethod]
    public void Entity_route_rejects_empty_guid()
    {
        Assert.IsFalse(
            AgentNavigationRequest.TryParse(
                new Uri($"xueqing://student/{Guid.Empty:D}"),
                out _));
    }
}
