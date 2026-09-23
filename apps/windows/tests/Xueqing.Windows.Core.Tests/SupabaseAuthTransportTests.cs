using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Infrastructure.Auth;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class SupabaseAuthTransportTests
{
    [TestMethod]
    public async Task Password_signin_posts_to_gotrue_with_publishable_key()
    {
        var handler = new RecordingHandler(
            Response(
                HttpStatusCode.OK,
                """{"access_token":"access","refresh_token":"refresh","expires_in":3600}"""));
        var transport = Create(handler);

        var tokens = await transport.SignInWithPasswordAsync(
            "teacher@example.invalid",
            "fictional-password");

        Assert.AreEqual("access", tokens.AccessToken);
        Assert.AreEqual("refresh", tokens.RefreshToken);
        Assert.AreEqual(
            new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero).AddHours(1),
            tokens.AccessTokenExpiresAt);
        Assert.AreEqual(
            "/auth/v1/token?grant_type=password",
            handler.LastRequestUri?.PathAndQuery);
        Assert.AreEqual("sb_publishable_fictional", handler.LastApiKey);
        StringAssert.Contains(handler.LastBody ?? "", "\"email\"");
        StringAssert.Contains(handler.LastBody ?? "", "\"password\"");
    }

    [TestMethod]
    public async Task Refresh_network_ambiguity_is_result_unknown()
    {
        var handler = new RecordingHandler(new HttpRequestException("fictional network loss"));
        var transport = Create(handler);

        var result = await transport.RefreshAsync("old-refresh");

        Assert.AreEqual(
            ProviderRefreshDisposition.ResultUnknown,
            result.Disposition);
    }

    [TestMethod]
    public async Task Refresh_rejection_and_rate_limit_keep_distinct_semantics()
    {
        var rejected = Create(new RecordingHandler(
            Response(HttpStatusCode.BadRequest, """{"error_code":"refresh_token_not_found"}""")));
        var limited = Create(new RecordingHandler(
            Response((HttpStatusCode)429, """{"message":"rate limited"}""")));

        Assert.AreEqual(
            ProviderRefreshDisposition.Rejected,
            (await rejected.RefreshAsync("refresh")).Disposition);
        Assert.AreEqual(
            ProviderRefreshDisposition.RetryableFailure,
            (await limited.RefreshAsync("refresh")).Disposition);
    }

    [TestMethod]
    public async Task Malformed_refresh_success_is_result_unknown_because_rotation_may_have_happened()
    {
        var transport = Create(new RecordingHandler(
            Response(HttpStatusCode.OK, """{"access_token":"access"}""")));

        var result = await transport.RefreshAsync("refresh");

        Assert.AreEqual(
            ProviderRefreshDisposition.ResultUnknown,
            result.Disposition);
    }

    [TestMethod]
    public async Task Local_signout_is_scoped_and_uses_bearer_session()
    {
        var handler = new RecordingHandler(Response(HttpStatusCode.NoContent, ""));
        var transport = Create(handler);

        await transport.SignOutLocalAsync("access-token");

        Assert.AreEqual(
            "/auth/v1/logout?scope=local",
            handler.LastRequestUri?.PathAndQuery);
        Assert.AreEqual("Bearer", handler.LastAuthorizationScheme);
        Assert.AreEqual("access-token", handler.LastAuthorizationParameter);
    }

    [TestMethod]
    public void Production_transport_rejects_insecure_origin_and_privileged_key()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new SupabaseAuthTransport(
                new HttpClient(),
                new Uri("http://example.invalid/"),
                "sb_publishable_fictional"));

        Assert.ThrowsExactly<ArgumentException>(
            () => new SupabaseAuthTransport(
                new HttpClient(),
                new Uri("https://example.invalid/"),
                "sb_secret_never_ship"));
    }

    private static SupabaseAuthTransport Create(RecordingHandler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.invalid/"),
            "sb_publishable_fictional",
            new FixedTimeProvider(
                new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero)));

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        string body) =>
        new(status)
        {
            Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json"),
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;

        public RecordingHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public RecordingHandler(Exception exception)
        {
            _exception = exception;
        }

        public Uri? LastRequestUri { get; private set; }

        public string? LastApiKey { get; private set; }

        public string? LastAuthorizationScheme { get; private set; }

        public string? LastAuthorizationParameter { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastApiKey = request.Headers.TryGetValues("apikey", out var apiKeys)
                ? apiKeys.Single()
                : null;
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastAuthorizationParameter = request.Headers.Authorization?.Parameter;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_exception is not null)
            {
                throw _exception;
            }

            return _response
                ?? throw new InvalidOperationException("No fake response configured.");
        }
    }
}
