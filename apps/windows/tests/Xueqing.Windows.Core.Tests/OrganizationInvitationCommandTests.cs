using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class OrganizationInvitationCommandTests
{
    [TestMethod]
    public async Task Command_normalizes_email_and_accepts_matching_authoritative_receipt()
    {
        var request = Request();
        var handler = new StubHandler(HttpStatusCode.OK, ValidReceipt(request));
        var command = CreateCommand(handler);

        var result = await command.ExecuteAsync(request, ActorId);

        Assert.IsTrue(result.IsSuccess, result.Failure?.Code);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(request.OperationId, result.Receipt.OperationId);
        Assert.AreEqual("invite.teacher@example.com", result.Receipt.InvitedEmail);
        Assert.AreEqual(OrganizationInvitationTargetRole.Teacher, result.Receipt.TargetRole);
        Assert.IsTrue(result.Receipt.TargetCanTeach);
        Assert.AreEqual(TimeSpan.FromDays(7), result.Receipt.ExpiresAt - result.Receipt.ServerCommittedAt);
        StringAssert.Contains(handler.LastBody ?? string.Empty, "invite.teacher@example.com");
        Assert.AreEqual("Bearer access-token", handler.LastAuthorization);
    }

    [TestMethod]
    public async Task Command_treats_mismatched_receipt_as_unknown_and_requires_same_operation()
    {
        var request = Request();
        var body = ValidReceipt(request).Replace(
            ActorId.ToString("D"),
            Guid.Parse("10000000-0000-0000-0000-000000000099").ToString("D"),
            StringComparison.Ordinal);
        var result = await CreateCommand(new StubHandler(HttpStatusCode.OK, body))
            .ExecuteAsync(request, ActorId);

        Assert.AreEqual(CreateOrganizationInvitationFailureKind.ResultUnknown, result.Failure?.Kind);
        Assert.AreEqual("XQ_RESULT_UNKNOWN_RECEIPT_CONTRACT", result.Failure?.Code);
        Assert.IsTrue(result.MustRetrySameOperation);
    }

    [TestMethod]
    public async Task Command_maps_pending_duplicate_without_weakening_operation_identity()
    {
        var result = await CreateCommand(new StubHandler(
            HttpStatusCode.BadRequest,
            """{"code":"P0001","message":"XQ_INVITATION_ALREADY_PENDING"}"""))
            .ExecuteAsync(Request(), ActorId);

        Assert.AreEqual(CreateOrganizationInvitationFailureKind.AlreadyPending, result.Failure?.Kind);
        Assert.IsFalse(result.MustRetrySameOperation);
    }

    [TestMethod]
    public async Task Command_does_not_send_invalid_or_unauthenticated_intent()
    {
        var invalidHandler = new StubHandler(HttpStatusCode.OK, "{}");
        var invalid = await CreateCommand(invalidHandler).ExecuteAsync(
            Request() with { InvitedEmail = "invalid" },
            ActorId);

        Assert.AreEqual(CreateOrganizationInvitationFailureKind.Validation, invalid.Failure?.Kind);
        Assert.AreEqual(0, invalidHandler.RequestCount);

        var noTokenHandler = new StubHandler(HttpStatusCode.OK, "{}");
        var command = new PostgrestOrganizationInvitationCommand(
            new HttpClient(noTokenHandler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>(null));
        var noToken = await command.ExecuteAsync(Request(), ActorId);

        Assert.AreEqual(CreateOrganizationInvitationFailureKind.AuthenticationRequired, noToken.Failure?.Kind);
        Assert.AreEqual(0, noTokenHandler.RequestCount);
    }

    private static PostgrestOrganizationInvitationCommand CreateCommand(StubHandler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>("access-token"));

    private static CreateOrganizationInvitationRequest Request() =>
        new(
            Guid.Parse("91000000-0000-0000-0000-00000000d001"),
            OrganizationId,
            "  Invite.Teacher@Example.COM ",
            OrganizationInvitationTargetRole.Teacher,
            true);

    private static string ValidReceipt(CreateOrganizationInvitationRequest request) => $$"""
        {
          "command":"create_organization_invitation_v1",
          "operation_id":"{{request.OperationId:D}}",
          "invitation_id":"92000000-0000-0000-0000-00000000d001",
          "actor_app_user_id":"{{ActorId:D}}",
          "organization_id":"{{request.OrganizationId:D}}",
          "invited_email":"invite.teacher@example.com",
          "target_role":"teacher",
          "target_can_teach":true,
          "status":"pending",
          "expires_at":"2026-09-27T13:00:00Z",
          "server_committed_at":"2026-09-20T13:00:00Z"
        }
        """;

    private static readonly Guid ActorId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

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
