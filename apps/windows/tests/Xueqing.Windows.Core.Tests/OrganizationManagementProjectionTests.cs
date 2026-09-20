using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class OrganizationManagementProjectionTests
{
    [TestMethod]
    public async Task Reader_returns_authoritative_owner_roster_and_capabilities()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidEnvelope());
        var reader = CreateReader(handler);

        var result = await reader.ReadAsync(OrganizationId);

        Assert.IsTrue(result.IsSuccess, result.Failure?.Code);
        Assert.IsNotNull(result.Snapshot);
        Assert.AreEqual(OrganizationId, result.Snapshot.OrganizationId);
        Assert.AreEqual(OrganizationMembershipRole.Owner, result.Snapshot.ActorMembershipRole);
        Assert.AreEqual(3, result.Snapshot.Members.Count);
        Assert.IsFalse(result.Snapshot.Capabilities.CanInviteOwner);
        Assert.IsTrue(result.Snapshot.Capabilities.CanInviteAdmin);
        Assert.IsTrue(result.Snapshot.Capabilities.CanInviteTeacher);
        Assert.IsTrue(result.Snapshot.Members.Any(member =>
            member.MembershipStatus == OrganizationMembershipStatus.Disabled));
        Assert.AreEqual("Bearer access-token", handler.LastAuthorization);
        Assert.IsTrue(handler.LastBody?.Contains(OrganizationId.ToString("D"), StringComparison.Ordinal) == true);
    }

    [TestMethod]
    public async Task Reader_rejects_success_envelope_bound_to_another_organization()
    {
        var reader = CreateReader(new StubHandler(
            HttpStatusCode.OK,
            ValidEnvelope(organizationId: Guid.Parse("20000000-0000-0000-0000-000000000099"))));

        var result = await reader.ReadAsync(OrganizationId);

        Assert.AreEqual(OrganizationManagementFailureKind.InvalidResponse, result.Failure?.Kind);
        Assert.AreEqual("XQ_ORGANIZATION_MANAGEMENT_CONTRACT_INVALID", result.Failure?.Code);
    }

    [TestMethod]
    public async Task Reader_rejects_teacher_actor_even_if_provider_returns_success()
    {
        var reader = CreateReader(new StubHandler(
            HttpStatusCode.OK,
            ValidEnvelope(actorRole: "teacher")));

        var result = await reader.ReadAsync(OrganizationId);

        Assert.AreEqual(OrganizationManagementFailureKind.InvalidResponse, result.Failure?.Kind);
    }

    [TestMethod]
    public async Task Reader_rejects_capabilities_that_disagree_with_actor_role()
    {
        var reader = CreateReader(new StubHandler(
            HttpStatusCode.OK,
            ValidEnvelope(canInviteOwner: true)));

        var result = await reader.ReadAsync(OrganizationId);

        Assert.AreEqual(OrganizationManagementFailureKind.InvalidResponse, result.Failure?.Kind);
    }

    [TestMethod]
    public async Task Reader_rejects_duplicate_member_identity()
    {
        var reader = CreateReader(new StubHandler(
            HttpStatusCode.OK,
            DuplicateMemberEnvelope()));

        var result = await reader.ReadAsync(OrganizationId);

        Assert.AreEqual(OrganizationManagementFailureKind.InvalidResponse, result.Failure?.Kind);
    }

    [TestMethod]
    public async Task Reader_maps_management_denial_without_exposing_provider_types()
    {
        var reader = CreateReader(new StubHandler(
            HttpStatusCode.BadRequest,
            """{"code":"P0001","message":"XQ_ORGANIZATION_MANAGEMENT_REQUIRED"}"""));

        var result = await reader.ReadAsync(OrganizationId);

        Assert.AreEqual(OrganizationManagementFailureKind.AccessDenied, result.Failure?.Kind);
        Assert.AreEqual("XQ_ORGANIZATION_MANAGEMENT_REQUIRED", result.Failure?.Code);
    }

    [TestMethod]
    public async Task Reader_does_not_send_request_without_access_token()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidEnvelope());
        var reader = new PostgrestOrganizationManagementReader(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>(null));

        var result = await reader.ReadAsync(OrganizationId);

        Assert.AreEqual(OrganizationManagementFailureKind.AuthenticationRequired, result.Failure?.Kind);
        Assert.AreEqual(0, handler.RequestCount);
    }

    private static PostgrestOrganizationManagementReader CreateReader(StubHandler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>("access-token"));

    private static string ValidEnvelope(
        Guid? organizationId = null,
        string actorRole = "owner",
        bool canInviteOwner = false) => $$"""
        {
          "contract":"organization_management_v1",
          "generated_at_server":"2026-09-20T12:45:00Z",
          "actor":{
            "app_user_id":"10000000-0000-0000-0000-000000000001",
            "display_name":"虚构教师甲",
            "membership_role":"{{actorRole}}"
          },
          "organization":{
            "organization_id":"{{(organizationId ?? OrganizationId):D}}",
            "name":"虚构机构甲",
            "time_zone":"Asia/Shanghai"
          },
          "capabilities":{
            "can_invite_owner":{{canInviteOwner.ToString().ToLowerInvariant()}},
            "can_invite_admin":true,
            "can_invite_teacher":true
          },
          "members":[
            {
              "app_user_id":"10000000-0000-0000-0000-000000000001",
              "display_name":"虚构教师甲",
              "app_user_enabled":true,
              "membership_role":"{{actorRole}}",
              "membership_status":"active",
              "can_teach":true
            },
            {
              "app_user_id":"10000000-0000-0000-0000-000000000002",
              "display_name":"虚构教师乙",
              "app_user_enabled":true,
              "membership_role":"admin",
              "membership_status":"active",
              "can_teach":true
            },
            {
              "app_user_id":"10000000-0000-0000-0000-000000000003",
              "display_name":"虚构停用教师",
              "app_user_enabled":true,
              "membership_role":"teacher",
              "membership_status":"disabled",
              "can_teach":true
            }
          ]
        }
        """;

    private static string DuplicateMemberEnvelope() => """
        {
          "contract":"organization_management_v1",
          "generated_at_server":"2026-09-20T12:45:00Z",
          "actor":{"app_user_id":"10000000-0000-0000-0000-000000000001","display_name":"虚构教师甲","membership_role":"owner"},
          "organization":{"organization_id":"20000000-0000-0000-0000-000000000001","name":"虚构机构甲","time_zone":"Asia/Shanghai"},
          "capabilities":{"can_invite_owner":false,"can_invite_admin":true,"can_invite_teacher":true},
          "members":[
            {"app_user_id":"10000000-0000-0000-0000-000000000001","display_name":"虚构教师甲","app_user_enabled":true,"membership_role":"owner","membership_status":"active","can_teach":true},
            {"app_user_id":"10000000-0000-0000-0000-000000000001","display_name":"重复","app_user_enabled":true,"membership_role":"teacher","membership_status":"active","can_teach":true}
          ]
        }
        """;

    private static readonly Guid OrganizationId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
