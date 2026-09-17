using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class PersonalBootstrapProjectionTests
{
    [TestMethod]
    public async Task Reader_returns_authoritative_application_identity_and_teaching_context()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidEnvelope());
        var reader = CreateReader(handler);

        var result = await reader.ReadAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Snapshot);
        Assert.AreEqual(Guid.Parse("10000000-0000-0000-0000-000000000001"), result.Snapshot.ActorAppUserId);
        Assert.AreEqual(1, result.Snapshot.Organizations.Count);
        Assert.AreEqual(1, result.Snapshot.TeachingContexts.Count);
        Assert.AreEqual(
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            result.Snapshot.TeachingContexts[0].ObservationScope.SubjectProfileId);
        Assert.AreEqual("Bearer access-token", handler.LastAuthorization);
    }

    [TestMethod]
    public async Task Reader_rejects_teaching_context_outside_same_envelope_authority()
    {
        var json = ValidEnvelope(teachingOrganizationId: Guid.Parse("20000000-0000-0000-0000-000000000009"));
        var reader = CreateReader(new StubHandler(HttpStatusCode.OK, json));

        var result = await reader.ReadAsync();

        Assert.AreEqual(PersonalBootstrapFailureKind.InvalidResponse, result.Failure?.Kind);
        Assert.AreEqual("XQ_BOOTSTRAP_CONTRACT_INVALID", result.Failure?.Code);
    }

    [TestMethod]
    public async Task Reader_rejects_context_when_membership_is_not_teaching_capable()
    {
        var reader = CreateReader(new StubHandler(HttpStatusCode.OK, ValidEnvelope(canTeach: false)));

        var result = await reader.ReadAsync();

        Assert.AreEqual(PersonalBootstrapFailureKind.InvalidResponse, result.Failure?.Kind);
    }

    [TestMethod]
    public async Task Reader_maps_actor_disable_without_exposing_provider_error_types()
    {
        var reader = CreateReader(new StubHandler(
            HttpStatusCode.BadRequest,
            "{\"code\":\"P0001\",\"message\":\"XQ_ACTOR_DISABLED\"}"));

        var result = await reader.ReadAsync();

        Assert.AreEqual(PersonalBootstrapFailureKind.AccessDenied, result.Failure?.Kind);
        Assert.AreEqual("XQ_ACTOR_DISABLED", result.Failure?.Code);
    }

    [TestMethod]
    public async Task Reader_does_not_send_request_without_access_token()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidEnvelope());
        var reader = new PostgrestPersonalBootstrapReader(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>(null));

        var result = await reader.ReadAsync();

        Assert.AreEqual(PersonalBootstrapFailureKind.AuthenticationRequired, result.Failure?.Kind);
        Assert.AreEqual(0, handler.RequestCount);
    }

    private static PostgrestPersonalBootstrapReader CreateReader(StubHandler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>("access-token"));

    private static string ValidEnvelope(Guid? teachingOrganizationId = null, bool canTeach = true) => $$"""
        {
          "contract":"personal_bootstrap_v1",
          "generated_at_server":"2026-09-17T12:00:00Z",
          "actor":{"app_user_id":"10000000-0000-0000-0000-000000000001","display_name":"虚构老师"},
          "organizations":[
            {"organization_id":"20000000-0000-0000-0000-000000000001","name":"虚构机构","can_teach":{{canTeach.ToString().ToLowerInvariant()}}}
          ],
          "teaching_contexts":[
            {
              "organization_id":"{{(teachingOrganizationId ?? Guid.Parse("20000000-0000-0000-0000-000000000001")):D}}",
              "student_id":"30000000-0000-0000-0000-000000000001",
              "student_display_name":"虚构学生0001",
              "subject_profile_id":"40000000-0000-0000-0000-000000000001",
              "subject_key":"chinese",
              "assignment_id":"50000000-0000-0000-0000-000000000001"
            }
          ]
        }
        """;

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
