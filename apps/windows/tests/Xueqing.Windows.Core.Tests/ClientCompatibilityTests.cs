using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class ClientCompatibilityTests
{
    private static readonly ClientCompatibilityRequest Request =
        new("windows", "1.0.0.0", 1);

    [TestMethod]
    public async Task Reader_sends_exact_request_and_parses_supported_decision()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidEnvelope("supported", "XQ_CLIENT_SUPPORTED"));
        var reader = CreateReader(handler);

        var result = await reader.ReadAsync(Request);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(ClientCompatibilityState.Supported, result.Decision?.State);
        Assert.IsTrue(result.Decision!.State.AllowsConsequentialWrite());
        Assert.AreEqual("Bearer access-token", handler.LastAuthorization);
        StringAssert.Contains(handler.LastBody!, "\"p_platform\":\"windows\"");
        StringAssert.Contains(handler.LastBody!, "\"p_app_version\":\"1.0.0.0\"");
    }

    [TestMethod]
    public async Task Recommended_update_remains_write_compatible()
    {
        var result = await CreateReader(new StubHandler(
            HttpStatusCode.OK,
            ValidEnvelope("update_recommended", "XQ_UPDATE_RECOMMENDED")))
            .ReadAsync(Request);

        Assert.AreEqual(ClientCompatibilityState.UpdateRecommended, result.Decision?.State);
        Assert.IsTrue(result.Decision!.State.AllowsConsequentialWrite());
    }

    [TestMethod]
    public async Task Required_or_security_blocked_decisions_deny_consequential_write()
    {
        foreach (var (state, reason) in new[]
        {
            ("update_required", "XQ_CLIENT_VERSION_UNSUPPORTED"),
            ("security_blocked", "XQ_CLIENT_SECURITY_BLOCKED"),
        })
        {
            var result = await CreateReader(new StubHandler(
                HttpStatusCode.OK,
                ValidEnvelope(state, reason))).ReadAsync(Request);

            Assert.IsFalse(result.Decision!.State.AllowsConsequentialWrite());
        }
    }

    [TestMethod]
    public async Task Missing_policy_and_transport_failure_never_become_supported()
    {
        var policy = await CreateReader(new StubHandler(
            HttpStatusCode.BadRequest,
            "{\"code\":\"P0001\",\"message\":\"XQ_COMPATIBILITY_POLICY_UNAVAILABLE\"}"))
            .ReadAsync(Request);

        Assert.AreEqual(ClientCompatibilityFailureKind.PolicyUnavailable, policy.Failure?.Kind);
        Assert.IsNull(policy.Decision);

        var noToken = new PostgrestClientCompatibilityReader(
            new HttpClient(new StubHandler(HttpStatusCode.OK, ValidEnvelope("supported", "XQ_CLIENT_SUPPORTED"))),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>(null));

        var auth = await noToken.ReadAsync(Request);
        Assert.AreEqual(ClientCompatibilityFailureKind.AuthenticationRequired, auth.Failure?.Kind);
        Assert.IsNull(auth.Decision);
    }

    [TestMethod]
    public async Task Client_echo_mismatch_and_unknown_state_fail_closed()
    {
        var mismatch = ValidEnvelope("supported", "XQ_CLIENT_SUPPORTED")
            .Replace("\"app_version\":\"1.0.0.0\"", "\"app_version\":\"other\"", StringComparison.Ordinal);
        var future = ValidEnvelope("supported", "XQ_CLIENT_SUPPORTED")
            .Replace("\"state\":\"supported\"", "\"state\":\"unknown\"", StringComparison.Ordinal);

        var mismatchResult = await CreateReader(new StubHandler(HttpStatusCode.OK, mismatch)).ReadAsync(Request);
        var futureResult = await CreateReader(new StubHandler(HttpStatusCode.OK, future)).ReadAsync(Request);

        Assert.AreEqual(ClientCompatibilityFailureKind.InvalidResponse, mismatchResult.Failure?.Kind);
        Assert.AreEqual(ClientCompatibilityFailureKind.InvalidResponse, futureResult.Failure?.Kind);
    }

    private static PostgrestClientCompatibilityReader CreateReader(StubHandler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>("access-token"));

    private static string ValidEnvelope(string state, string reason) => $$"""
        {
          "contract":"client_compatibility_v1",
          "generated_at_server":"2026-09-23T03:30:00Z",
          "policy_revision":"v1-initial",
          "client":{
            "platform":"windows",
            "app_version":"1.0.0.0",
            "contract_version":1
          },
          "decision":{
            "state":"{{state}}",
            "reason_code":"{{reason}}",
            "minimum_supported_app_version":"0.9.0.0",
            "recommended_app_version":"1.0.0.0",
            "minimum_supported_contract_version":1,
            "server_contract_version":1,
            "update_uri":"https://github.com/qbjsdsb/xueqing-native/releases"
          }
        }
        """;

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public string? LastAuthorization { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
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
