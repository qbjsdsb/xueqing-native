using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
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

        ValueTask<string?> AccessTokenProvider(CancellationToken _) =>
            ValueTask.FromResult<string?>(accessToken);

        var bootstrapReader = new PostgrestPersonalBootstrapReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var bootstrapResult = await bootstrapReader.ReadAsync();

        Assert.IsTrue(bootstrapResult.IsSuccess, bootstrapResult.Failure?.Code);
        Assert.IsNotNull(bootstrapResult.Snapshot);
        Assert.AreEqual(1, bootstrapResult.Snapshot.TeachingContexts.Count,
            "The deterministic reference-provider seed must expose exactly one teaching context for this E2E actor.");
        var context = bootstrapResult.Snapshot.TeachingContexts[0];

        var operationId = Guid.Parse("74000000-0000-0000-0000-000000000027");
        const string rawText = "  Windows 写读闭环 · 虚构课堂观察原文  ";
        var command = new PostgrestCreateObservationCommand(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var request = new CreateObservationRequest(
            operationId,
            context.OrganizationId,
            context.StudentId,
            context.SubjectProfileId,
            context.AssignmentId,
            rawText,
            ClientCaptureMetadata: new Dictionary<string, string>
            {
                ["source"] = "windows_reference_provider_e2e",
            });

        var first = await command.ExecuteAsync(
            request,
            bootstrapResult.Snapshot.ActorAppUserId);
        var retry = await command.ExecuteAsync(
            request,
            bootstrapResult.Snapshot.ActorAppUserId);

        Assert.IsTrue(first.IsSuccess, first.Failure?.Code);
        Assert.IsTrue(retry.IsSuccess, retry.Failure?.Code);
        Assert.IsNotNull(first.Receipt);
        Assert.IsNotNull(retry.Receipt);
        Assert.AreEqual(first.Receipt.ObservationId, retry.Receipt.ObservationId,
            "Retrying one intent must resolve to the same committed Observation.");
        Assert.AreEqual(first.Receipt.ServerCommittedAt, retry.Receipt.ServerCommittedAt,
            "Retrying one intent must reuse the authoritative receipt.");
        Assert.AreEqual(bootstrapResult.Snapshot.ActorAppUserId, first.Receipt.ActorAppUserId);

        var recentReader = new PostgrestStudentRecentObservationsReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var recentResult = await recentReader.ReadAsync(
            context.ObservationScope,
            bootstrapResult.Snapshot.ActorAppUserId);

        Assert.IsTrue(recentResult.IsSuccess, recentResult.Failure?.Code);
        Assert.IsNotNull(recentResult.Snapshot);
        Assert.AreEqual(context.StudentDisplayName, recentResult.Snapshot.StudentDisplayName);
        var projected = recentResult.Snapshot.Observations.Single(
            observation => observation.ObservationId == first.Receipt.ObservationId);
        Assert.AreEqual(rawText, projected.RawText);
        Assert.AreEqual(first.Receipt.ServerCommittedAt, projected.CreatedAtServer);
        Assert.AreEqual(bootstrapResult.Snapshot.ActorAppUserId, projected.ActorAppUserId);
    }

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Required E2E environment variable '{name}' is missing.")
            : value;
    }
}
