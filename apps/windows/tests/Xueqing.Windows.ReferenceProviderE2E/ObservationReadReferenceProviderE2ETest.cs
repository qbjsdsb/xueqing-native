using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.ReferenceProviderE2E;

[TestClass]
public sealed class ObservationReadReferenceProviderE2ETest
{
    [TestMethod]
    public async Task PersonalBootstrap_createObservation_and_recentRead_share_authoritative_fact()
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
        var bootstrapResult = await bootstrapReader.ReadAsync();

        Assert.IsTrue(bootstrapResult.IsSuccess, bootstrapResult.Failure?.Code);
        Assert.IsNotNull(bootstrapResult.Snapshot);
        Assert.AreEqual(1, bootstrapResult.Snapshot.TeachingContexts.Count,
            "The deterministic reference-provider seed must expose exactly one teaching context for this E2E actor.");
        var context = bootstrapResult.Snapshot.TeachingContexts[0];

        var operationId = Guid.Parse("74000000-0000-0000-0000-000000000027");
        const string rawText = "  Windows 读取闭环 · 虚构课堂观察原文  ";
        var firstReceipt = await CreateObservationAsync(
            httpClient,
            projectUri,
            apiKey,
            accessToken,
            operationId,
            context.OrganizationId,
            context.StudentId,
            context.SubjectProfileId,
            context.AssignmentId,
            rawText);
        var retryReceipt = await CreateObservationAsync(
            httpClient,
            projectUri,
            apiKey,
            accessToken,
            operationId,
            context.OrganizationId,
            context.StudentId,
            context.SubjectProfileId,
            context.AssignmentId,
            rawText);

        Assert.AreEqual(firstReceipt.ObservationId, retryReceipt.ObservationId,
            "Retrying one intent must resolve to the same committed Observation.");
        Assert.AreEqual(firstReceipt.ServerCommittedAt, retryReceipt.ServerCommittedAt,
            "Retrying one intent must reuse the authoritative receipt.");

        var recentReader = new PostgrestStudentRecentObservationsReader(
            httpClient,
            projectUri,
            apiKey,
            _ => ValueTask.FromResult<string?>(accessToken));
        var recentResult = await recentReader.ReadAsync(
            context.ObservationScope,
            bootstrapResult.Snapshot.ActorAppUserId);

        Assert.IsTrue(recentResult.IsSuccess, recentResult.Failure?.Code);
        Assert.IsNotNull(recentResult.Snapshot);
        Assert.AreEqual(context.StudentDisplayName, recentResult.Snapshot.StudentDisplayName);
        var projected = recentResult.Snapshot.Observations.Single(
            observation => observation.ObservationId == firstReceipt.ObservationId);
        Assert.AreEqual(rawText, projected.RawText);
        Assert.AreEqual(firstReceipt.ServerCommittedAt, projected.CreatedAtServer);
        Assert.AreEqual(bootstrapResult.Snapshot.ActorAppUserId, projected.ActorAppUserId);
    }

    private static async Task<ObservationReceipt> CreateObservationAsync(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        string accessToken,
        Guid operationId,
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid assignmentId,
        string rawText)
    {
        var baseUri = projectUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? projectUri
            : new Uri(projectUri.AbsoluteUri + "/", UriKind.Absolute);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/create_observation"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Content = JsonContent.Create(new
        {
            p_operation_id = operationId,
            p_organization_id = organizationId,
            p_student_id = studentId,
            p_subject_profile_id = subjectProfileId,
            p_assignment_id = assignmentId,
            p_raw_text = rawText,
            p_client_captured_at = (DateTimeOffset?)null,
            p_client_capture_metadata = new { source = "windows_reference_provider_e2e" },
        });

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode,
            $"CreateObservation failed with HTTP {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var observationId = Guid.Parse(root.GetProperty("observation_id").GetString()
            ?? throw new InvalidDataException("CreateObservation receipt is missing observation_id."));
        var committedAt = root.GetProperty("server_committed_at").GetDateTimeOffset();
        return new ObservationReceipt(observationId, committedAt);
    }

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Required E2E environment variable '{name}' is missing.")
            : value;
    }

    private sealed record ObservationReceipt(Guid ObservationId, DateTimeOffset ServerCommittedAt);
}
