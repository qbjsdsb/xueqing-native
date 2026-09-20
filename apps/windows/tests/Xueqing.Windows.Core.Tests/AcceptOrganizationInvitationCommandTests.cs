using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class AcceptOrganizationInvitationCommandTests
{
    [TestMethod]
    public async Task Command_accepts_matching_authoritative_receipt()
    {
        var request = Request();
        var handler = new StubHandler(HttpStatusCode.OK, ValidReceipt(request));
        var command = CreateCommand(handler);

        var result = await command.ExecuteAsync(request);

        Assert.IsTrue(result.IsSuccess, result.Failure?.Code);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(request.OperationId, result.Receipt.OperationId);
        Assert.AreEqual(request.InvitationId, result.Receipt.InvitationId);
        Assert.AreEqual(OrganizationMembershipRole.Teacher, result.Receipt.MembershipRole);
        Assert.IsTrue(result.Receipt.CanTeach);
        using var requestJson = JsonDocument.Parse(handler.LastBody ?? "{}");
        Assert.AreEqual(
            "新教师",
            requestJson.RootElement.GetProperty("p_display_name").GetString());
        Assert.AreEqual("Bearer invitee-token", handler.LastAuthorization);
    }

    [TestMethod]
    public async Task Command_treats_mismatched_success_receipt_as_unknown()
    {
        var request = Request();
        var body = ValidReceipt(request).Replace(
            request.InvitationId.ToString("D"),
            Guid.Parse("95000000-0000-0000-0000-00000000ffff").ToString("D"),
            StringComparison.Ordinal);

        var result = await CreateCommand(new StubHandler(HttpStatusCode.OK, body))
            .ExecuteAsync(request);

        Assert.AreEqual(AcceptOrganizationInvitationFailureKind.ResultUnknown, result.Failure?.Kind);
        Assert.AreEqual("XQ_RESULT_UNKNOWN_RECEIPT_CONTRACT", result.Failure?.Code);
        Assert.IsTrue(result.MustRetrySameOperation);
    }

    [TestMethod]
    public async Task Command_maps_membership_conflict_without_retrying_operation()
    {
        var result = await CreateCommand(new StubHandler(
            HttpStatusCode.BadRequest,
            """{"code":"P0001","message":"XQ_MEMBERSHIP_ALREADY_EXISTS"}"""))
            .ExecuteAsync(Request());

        Assert.AreEqual(
            AcceptOrganizationInvitationFailureKind.MembershipConflict,
            result.Failure?.Kind);
        Assert.IsFalse(result.MustRetrySameOperation);
    }

    [TestMethod]
    public async Task Command_does_not_send_invalid_or_unauthenticated_intent()
    {
        var invalidHandler = new StubHandler(HttpStatusCode.OK, "{}");
        var invalid = await CreateCommand(invalidHandler).ExecuteAsync(
            Request() with { DisplayName = "   " });

        Assert.AreEqual(AcceptOrganizationInvitationFailureKind.Validation, invalid.Failure?.Kind);
        Assert.AreEqual(0, invalidHandler.RequestCount);

        var noTokenHandler = new StubHandler(HttpStatusCode.OK, "{}");
        var noToken = new PostgrestAcceptOrganizationInvitationCommand(
            new HttpClient(noTokenHandler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>(null));
        var result = await noToken.ExecuteAsync(Request());

        Assert.AreEqual(
            AcceptOrganizationInvitationFailureKind.AuthenticationRequired,
            result.Failure?.Kind);
        Assert.AreEqual(0, noTokenHandler.RequestCount);
    }

    private static PostgrestAcceptOrganizationInvitationCommand CreateCommand(
        StubHandler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>("invitee-token"));

    private static AcceptOrganizationInvitationRequest Request() =>
        new(
            Guid.Parse("96000000-0000-0000-0000-00000000d001"),
            Guid.Parse("95000000-0000-0000-0000-00000000d001"),
            " 新教师 ");

    private static string ValidReceipt(AcceptOrganizationInvitationRequest request) => $$"""
        {
          "command":"accept_organization_invitation_v1",
          "operation_id":"{{request.OperationId:D}}",
          "invitation_id":"{{request.InvitationId:D}}",
          "actor_app_user_id":"10000000-0000-0000-0000-00000000d001",
          "organization_id":"20000000-0000-0000-0000-000000000001",
          "membership_role":"teacher",
          "can_teach":true,
          "status":"accepted",
          "server_committed_at":"2026-09-20T14:00:00Z"
        }
        """;

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
