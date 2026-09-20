using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.ReferenceProviderE2E;

[TestClass]
public sealed class StudentLearningCasesReferenceProviderE2ETest
{
    [TestMethod]
    public async Task Closed_case_is_visible_in_history_and_absent_from_open_projections()
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
        var bootstrap = await bootstrapReader.ReadAsync();
        Assert.IsTrue(bootstrap.IsSuccess, bootstrap.Failure?.Code);
        Assert.IsNotNull(bootstrap.Snapshot);
        Assert.AreEqual(1, bootstrap.Snapshot.TeachingContexts.Count);

        var actorId = bootstrap.Snapshot.ActorAppUserId;
        var context = bootstrap.Snapshot.TeachingContexts[0];
        var scope = new StudentLearningScope(
            context.OrganizationId,
            context.StudentId,
            context.SubjectProfileId);

        var createCase = new PostgrestCreateLearningCaseCommand(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var createRequest = new CreateLearningCaseRequest(
            Guid.Parse("77000000-0000-0000-0000-000000000001"),
            context.OrganizationId,
            context.StudentId,
            context.SubjectProfileId,
            context.AssignmentId,
            "Windows E2E · Case 历史关闭验证",
            "Windows E2E · 关闭前保持唯一待办行动",
            null,
            null);
        var created = await createCase.ExecuteAsync(createRequest, actorId);
        Assert.IsTrue(created.IsSuccess, created.Failure?.Code);
        Assert.IsNotNull(created.Receipt);

        var caseId = created.Receipt.CaseId;
        var actionId = created.Receipt.PrimaryActionId;

        await PostRpcAsync(
            httpClient, projectUri, apiKey, accessToken,
            "transition_learning_case_state",
            new
            {
                p_operation_id = Guid.Parse("77000000-0000-0000-0000-000000000002"),
                p_organization_id = context.OrganizationId,
                p_student_id = context.StudentId,
                p_subject_profile_id = context.SubjectProfileId,
                p_owner_assignment_id = context.AssignmentId,
                p_case_id = caseId,
                p_expected_case_version = 1,
                p_target_state = "confirmed",
            });
        await PostRpcAsync(
            httpClient, projectUri, apiKey, accessToken,
            "transition_learning_case_state",
            new
            {
                p_operation_id = Guid.Parse("77000000-0000-0000-0000-000000000003"),
                p_organization_id = context.OrganizationId,
                p_student_id = context.StudentId,
                p_subject_profile_id = context.SubjectProfileId,
                p_owner_assignment_id = context.AssignmentId,
                p_case_id = caseId,
                p_expected_case_version = 2,
                p_target_state = "intervening",
            });
        await PostRpcAsync(
            httpClient, projectUri, apiKey, accessToken,
            "transition_learning_case_state",
            new
            {
                p_operation_id = Guid.Parse("77000000-0000-0000-0000-000000000004"),
                p_organization_id = context.OrganizationId,
                p_student_id = context.StudentId,
                p_subject_profile_id = context.SubjectProfileId,
                p_owner_assignment_id = context.AssignmentId,
                p_case_id = caseId,
                p_expected_case_version = 3,
                p_target_state = "pending_verification",
            });
        await PostRpcAsync(
            httpClient, projectUri, apiKey, accessToken,
            "transition_learning_case_state",
            new
            {
                p_operation_id = Guid.Parse("77000000-0000-0000-0000-000000000005"),
                p_organization_id = context.OrganizationId,
                p_student_id = context.StudentId,
                p_subject_profile_id = context.SubjectProfileId,
                p_owner_assignment_id = context.AssignmentId,
                p_case_id = caseId,
                p_expected_case_version = 4,
                p_target_state = "stable",
            });
        await PostRpcAsync(
            httpClient, projectUri, apiKey, accessToken,
            "close_learning_case",
            new
            {
                p_operation_id = Guid.Parse("77000000-0000-0000-0000-000000000006"),
                p_organization_id = context.OrganizationId,
                p_student_id = context.StudentId,
                p_subject_profile_id = context.SubjectProfileId,
                p_owner_assignment_id = context.AssignmentId,
                p_case_id = caseId,
                p_primary_action_id = actionId,
                p_expected_case_version = 5,
                p_expected_action_version = 1,
            });

        var historyReader = new PostgrestStudentLearningCasesReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var history = await historyReader.ReadAsync(scope, actorId);
        Assert.IsTrue(history.IsSuccess, history.Failure?.Code);
        Assert.IsNotNull(history.Snapshot);

        var closed = history.Snapshot.Cases.Single(item => item.CaseId == caseId);
        Assert.AreEqual(LearningCaseState.Closed, closed.State);
        Assert.AreEqual(6L, closed.Version);
        Assert.IsTrue(closed.IsCurrentActorResponsibility);
        Assert.IsNull(closed.PrimaryAction);

        var focusReader = new PostgrestStudentLearningFocusReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var focus = await focusReader.ReadAsync(scope, actorId);
        Assert.IsTrue(focus.IsSuccess, focus.Failure?.Code);
        Assert.IsNotNull(focus.Snapshot);
        Assert.IsFalse(focus.Snapshot.Cases.Any(item => item.CaseId == caseId));

        var todayReader = new PostgrestPersonalTodayActionsReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var today = await todayReader.ReadAsync(actorId);
        Assert.IsTrue(today.IsSuccess, today.Failure?.Code);
        Assert.IsNotNull(today.Snapshot);
        Assert.IsFalse(today.Snapshot.Actions.Any(item => item.ActionId == actionId));
    }

    private static async Task PostRpcAsync(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        string accessToken,
        string rpcName,
        object payload)
    {
        var baseUri = projectUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? projectUri
            : new Uri(projectUri.AbsoluteUri + "/", UriKind.Absolute);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/" + rpcName));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Content = JsonContent.Create(payload);

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(
            response.IsSuccessStatusCode,
            $"{rpcName} failed with HTTP {(int)response.StatusCode}: {body}");
    }

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Required E2E environment variable '{name}' is missing.")
            : value;
    }
}
