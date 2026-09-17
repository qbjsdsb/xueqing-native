using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class StudentRecentObservationsProjectionTests
{
    private static readonly Guid OrganizationId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid StudentId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid SubjectProfileId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid ActorId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [TestMethod]
    public async Task Reader_accepts_valid_scope_and_preserves_authoritative_text()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidEnvelope());
        var reader = CreateReader(handler);

        var result = await reader.ReadAsync(Scope(), ActorId);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.Failure);
        Assert.IsNotNull(result.Snapshot);
        Assert.AreEqual("虚构学生0001", result.Snapshot.StudentDisplayName);
        Assert.AreEqual("  原始课堂记录保留空格  ", result.Snapshot.Observations[0].RawText);
        Assert.IsNull(result.Snapshot.Observations[0].ClientCapturedAt);
        Assert.IsFalse(result.Snapshot.HasMore);
        Assert.AreEqual("Bearer access-token", handler.LastAuthorization);
        Assert.AreEqual("publishable-key", handler.LastApiKey);
        StringAssert.Contains(handler.LastBody ?? string.Empty, "p_subject_profile_id");
    }

    [TestMethod]
    public async Task Reader_fails_closed_when_response_scope_does_not_match_request()
    {
        var wrongStudent = Guid.Parse("30000000-0000-0000-0000-000000000009");
        var handler = new StubHandler(HttpStatusCode.OK, ValidEnvelope(studentId: wrongStudent));
        var reader = CreateReader(handler);

        var result = await reader.ReadAsync(Scope(), ActorId);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(StudentRecentObservationsFailureKind.InvalidResponse, result.Failure?.Kind);
        Assert.AreEqual("XQ_PROJECTION_CONTRACT_INVALID", result.Failure?.Code);
    }

    [TestMethod]
    public async Task Reader_fails_closed_when_actor_does_not_match_active_application_identity()
    {
        var otherActor = Guid.Parse("10000000-0000-0000-0000-000000000009");
        var handler = new StubHandler(HttpStatusCode.OK, ValidEnvelope(actorId: otherActor));
        var reader = CreateReader(handler);

        var result = await reader.ReadAsync(Scope(), ActorId);

        Assert.AreEqual(StudentRecentObservationsFailureKind.InvalidResponse, result.Failure?.Kind);
    }

    [TestMethod]
    public async Task Reader_rejects_duplicate_or_out_of_order_facts()
    {
        var duplicate = """
            [
              {"observation_id":"81000000-0000-0000-0000-000000000001","actor_app_user_id":"10000000-0000-0000-0000-000000000001","raw_text":"一","client_captured_at":null,"created_at_server":"2026-09-17T12:00:00Z"},
              {"observation_id":"81000000-0000-0000-0000-000000000001","actor_app_user_id":"10000000-0000-0000-0000-000000000001","raw_text":"二","client_captured_at":null,"created_at_server":"2026-09-17T11:00:00Z"}
            ]
            """;
        var handler = new StubHandler(HttpStatusCode.OK, ValidEnvelope(observationsJson: duplicate));
        var reader = CreateReader(handler);

        var result = await reader.ReadAsync(Scope(), ActorId);

        Assert.AreEqual(StudentRecentObservationsFailureKind.InvalidResponse, result.Failure?.Kind);
    }

    [TestMethod]
    public async Task Reader_maps_auth_authority_and_transient_failures_without_provider_types()
    {
        var auth = CreateReader(new StubHandler(HttpStatusCode.Unauthorized, "{}"));
        var denied = CreateReader(new StubHandler(HttpStatusCode.BadRequest, "{\"code\":\"P0001\",\"message\":\"XQ_TEACHING_CONTEXT_UNAVAILABLE\"}"));
        var transient = CreateReader(new StubHandler(HttpStatusCode.ServiceUnavailable, "{}"));

        var authResult = await auth.ReadAsync(Scope(), ActorId);
        var deniedResult = await denied.ReadAsync(Scope(), ActorId);
        var transientResult = await transient.ReadAsync(Scope(), ActorId);

        Assert.AreEqual(StudentRecentObservationsFailureKind.AuthenticationRequired, authResult.Failure?.Kind);
        Assert.AreEqual(StudentRecentObservationsFailureKind.AccessDenied, deniedResult.Failure?.Kind);
        Assert.AreEqual(StudentRecentObservationsFailureKind.Transient, transientResult.Failure?.Kind);
    }

    [TestMethod]
    public async Task Reader_does_not_send_request_without_live_access_token()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidEnvelope());
        var reader = new PostgrestStudentRecentObservationsReader(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>(null));

        var result = await reader.ReadAsync(Scope(), ActorId);

        Assert.AreEqual(StudentRecentObservationsFailureKind.AuthenticationRequired, result.Failure?.Kind);
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public void Reader_rejects_plain_http_for_non_loopback_provider()
    {
        try
        {
            _ = new PostgrestStudentRecentObservationsReader(
                new HttpClient(new StubHandler(HttpStatusCode.OK, ValidEnvelope())),
                new Uri("http://example.test/"),
                "publishable-key",
                _ => ValueTask.FromResult<string?>("token"));
            Assert.Fail("A non-loopback HTTP provider must be rejected.");
        }
        catch (ArgumentException)
        {
        }
    }

    private static PostgrestStudentRecentObservationsReader CreateReader(StubHandler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>("access-token"));

    private static StudentObservationScope Scope() => new(OrganizationId, StudentId, SubjectProfileId);

    private static string ValidEnvelope(
        Guid? studentId = null,
        Guid? actorId = null,
        string? observationsJson = null)
    {
        observationsJson ??= """
            [
              {"observation_id":"81000000-0000-0000-0000-000000000001","actor_app_user_id":"10000000-0000-0000-0000-000000000001","raw_text":"  原始课堂记录保留空格  ","client_captured_at":null,"created_at_server":"2026-09-17T12:00:00Z"}
            ]
            """;
        return $$"""
            {
              "contract":"student_recent_observations_v1",
              "generated_at_server":"2026-09-17T12:01:00Z",
              "actor_app_user_id":"{{(actorId ?? ActorId):D}}",
              "organization_id":"{{OrganizationId:D}}",
              "student_id":"{{(studentId ?? StudentId):D}}",
              "student_display_name":"虚构学生0001",
              "subject_profile_id":"{{SubjectProfileId:D}}",
              "subject_key":"chinese",
              "assignment_id":"50000000-0000-0000-0000-000000000001",
              "observations":{{observationsJson}},
              "has_more":false
            }
            """;
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastApiKey { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastApiKey = request.Headers.TryGetValues("apikey", out var values) ? values.SingleOrDefault() : null;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
