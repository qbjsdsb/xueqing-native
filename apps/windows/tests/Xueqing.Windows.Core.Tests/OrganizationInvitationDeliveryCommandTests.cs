using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class OrganizationInvitationDeliveryCommandTests
{
    [TestMethod]
    public async Task Adapter_accepts_matching_sent_receipt()
    {
        var request = Request();
        var handler = new StubHandler(HttpStatusCode.OK, ValidReceipt(request));
        var command = CreateCommand(handler);

        var result = await command.ExecuteAsync(request);

        Assert.IsTrue(result.IsSuccess, result.Failure?.Code);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(request.OperationId, result.Receipt.OperationId);
        Assert.AreEqual(request.InvitationId, result.Receipt.InvitationId);
        Assert.AreEqual(DeliveryId, result.Receipt.DeliveryId);
        Assert.AreEqual("Bearer access-token", handler.LastAuthorization);
        StringAssert.Contains(handler.LastBody ?? string.Empty, request.OperationId.ToString("D"));
        StringAssert.Contains(handler.LastBody ?? string.Empty, request.InvitationId.ToString("D"));
        StringAssert.EndsWith(
            handler.LastRequestUri?.AbsolutePath ?? string.Empty,
            "/functions/v1/organization-invitation-delivery");
    }

    [TestMethod]
    public async Task Adapter_treats_malformed_success_as_result_unknown()
    {
        var request = Request();
        var body = ValidReceipt(request).Replace(
            request.InvitationId.ToString("D"),
            Guid.Parse("92000000-0000-0000-0000-000000000099").ToString("D"),
            StringComparison.Ordinal);

        var result = await CreateCommand(new StubHandler(HttpStatusCode.OK, body))
            .ExecuteAsync(request);

        Assert.AreEqual(
            DeliverOrganizationInvitationFailureKind.ResultUnknown,
            result.Failure?.Kind);
        Assert.IsTrue(result.MustRetrySameOperation);
    }

    [TestMethod]
    public async Task Adapter_maps_dispatching_response_to_same_operation_retry()
    {
        var result = await CreateCommand(new StubHandler(
            HttpStatusCode.Conflict,
            """
            {
              "ok":false,
              "error":"XQ_INVITATION_DELIVERY_RESULT_UNKNOWN",
              "state":"dispatching"
            }
            """))
            .ExecuteAsync(Request());

        Assert.AreEqual(
            DeliverOrganizationInvitationFailureKind.ResultUnknown,
            result.Failure?.Kind);
        Assert.IsTrue(result.MustRetrySameOperation);
    }

    [TestMethod]
    public async Task Adapter_maps_known_provider_rejection_without_retrying_send()
    {
        var result = await CreateCommand(new StubHandler(
            HttpStatusCode.UnprocessableEntity,
            """
            {
              "ok":false,
              "error":"XQ_PROVIDER_DELIVERY_REJECTED",
              "state":"failed"
            }
            """))
            .ExecuteAsync(Request());

        Assert.AreEqual(
            DeliverOrganizationInvitationFailureKind.ProviderRejected,
            result.Failure?.Kind);
        Assert.IsFalse(result.MustRetrySameOperation);
    }

    [TestMethod]
    public async Task Adapter_maps_live_management_revoke()
    {
        var result = await CreateCommand(new StubHandler(
            HttpStatusCode.Forbidden,
            """{"ok":false,"error":"XQ_ORGANIZATION_MANAGEMENT_REQUIRED"}"""))
            .ExecuteAsync(Request());

        Assert.AreEqual(
            DeliverOrganizationInvitationFailureKind.AuthorityChanged,
            result.Failure?.Kind);
    }

    [TestMethod]
    public async Task Adapter_does_not_send_invalid_or_unauthenticated_request()
    {
        var invalidHandler = new StubHandler(HttpStatusCode.OK, "{}");
        var invalid = await CreateCommand(invalidHandler).ExecuteAsync(
            Request() with { OperationId = Guid.Empty });

        Assert.AreEqual(
            DeliverOrganizationInvitationFailureKind.Validation,
            invalid.Failure?.Kind);
        Assert.AreEqual(0, invalidHandler.RequestCount);

        var noTokenHandler = new StubHandler(HttpStatusCode.OK, "{}");
        var command = new SupabaseOrganizationInvitationDeliveryCommand(
            new HttpClient(noTokenHandler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>(null));

        var noToken = await command.ExecuteAsync(Request());

        Assert.AreEqual(
            DeliverOrganizationInvitationFailureKind.AuthenticationRequired,
            noToken.Failure?.Kind);
        Assert.AreEqual(0, noTokenHandler.RequestCount);
    }

    private static SupabaseOrganizationInvitationDeliveryCommand CreateCommand(
        StubHandler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>("access-token"));

    private static DeliverOrganizationInvitationRequest Request() =>
        new(
            Guid.Parse("94000000-0000-4000-8000-00000000d001"),
            Guid.Parse("92000000-0000-4000-8000-00000000d001"));

    private static string ValidReceipt(DeliverOrganizationInvitationRequest request) => $$"""
        {
          "ok":true,
          "operation_id":"{{request.OperationId:D}}",
          "delivery_id":"{{DeliveryId:D}}",
          "invitation_id":"{{request.InvitationId:D}}",
          "state":"sent"
        }
        """;

    private static readonly Guid DeliveryId =
        Guid.Parse("95000000-0000-4000-8000-00000000d001");

    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string body) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastBody { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastRequestUri = request.RequestUri;
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
