using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.ReferenceProviderE2E;

[TestClass]
public sealed class LearningReadReferenceProviderE2ETest
{
    [TestMethod]
    public async Task CreateLearningCase_projects_same_authoritative_case_into_Focus_and_Today()
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
        var bootstrap = await bootstrapReader.ReadAsync();

        Assert.IsTrue(bootstrap.IsSuccess, bootstrap.Failure?.Code);
        Assert.IsNotNull(bootstrap.Snapshot);
        Assert.AreEqual(1, bootstrap.Snapshot.TeachingContexts.Count,
            "The deterministic E2E actor must expose exactly one teaching context.");
        var context = bootstrap.Snapshot.TeachingContexts[0];

        var scope = new StudentLearningScope(
            context.OrganizationId,
            context.StudentId,
            context.SubjectProfileId);
        var focusReader = new PostgrestStudentLearningFocusReader(
            httpClient,
            projectUri,
            apiKey,
            _ => ValueTask.FromResult<string?>(accessToken));
        var todayReader = new PostgrestPersonalTodayActionsReader(
            httpClient,
            projectUri,
            apiKey,
            _ => ValueTask.FromResult<string?>(accessToken));

        var initialFocus = await focusReader.ReadAsync(
            scope,
            bootstrap.Snapshot.ActorAppUserId);
        Assert.IsTrue(initialFocus.IsSuccess, initialFocus.Failure?.Code);
        Assert.IsNotNull(initialFocus.Snapshot);

        var operationId = Guid.Parse("75000000-0000-0000-0000-000000000037");
        const string caseTitle = "Windows E2E · 虚构概括问题";
        const string actionText = "Windows E2E · 今天复核三道陌生材料";
        var dueOn = initialFocus.Snapshot.OrganizationBusinessDate;

        var firstReceipt = await CreateLearningCaseAsync(
            httpClient,
            projectUri,
            apiKey,
            accessToken,
            operationId,
            context.OrganizationId,
            context.StudentId,
            context.SubjectProfileId,
            context.AssignmentId,
            caseTitle,
            actionText,
            dueOn);
        var retryReceipt = await CreateLearningCaseAsync(
            httpClient,
            projectUri,
            apiKey,
            accessToken,
            operationId,
            context.OrganizationId,
            context.StudentId,
            context.SubjectProfileId,
            context.AssignmentId,
            caseTitle,
            actionText,
            dueOn);

        Assert.AreEqual(firstReceipt.CaseId, retryReceipt.CaseId);
        Assert.AreEqual(firstReceipt.PrimaryActionId, retryReceipt.PrimaryActionId);
        Assert.AreEqual(firstReceipt.ServerCommittedAt, retryReceipt.ServerCommittedAt);

        var focus = await focusReader.ReadAsync(
            scope,
            bootstrap.Snapshot.ActorAppUserId);
        Assert.IsTrue(focus.IsSuccess, focus.Failure?.Code);
        Assert.IsNotNull(focus.Snapshot);
        var projectedCase = focus.Snapshot.Cases.Single(item => item.CaseId == firstReceipt.CaseId);

        Assert.AreEqual(caseTitle, projectedCase.Title);
        Assert.AreEqual(firstReceipt.PrimaryActionId, projectedCase.PrimaryAction.ActionId);
        Assert.AreEqual(actionText, projectedCase.PrimaryAction.ActionText);
        Assert.AreEqual(dueOn, projectedCase.PrimaryAction.DueOn);
        Assert.AreEqual(ActionDueBucket.Today, projectedCase.PrimaryAction.DueBucket);
        Assert.AreEqual(bootstrap.Snapshot.ActorAppUserId, projectedCase.ResponsibleTeacherAppUserId);
        Assert.AreEqual(context.AssignmentId, projectedCase.OwnerAssignmentId);

        var today = await todayReader.ReadAsync(bootstrap.Snapshot.ActorAppUserId);
        Assert.IsTrue(today.IsSuccess, today.Failure?.Code);
        Assert.IsNotNull(today.Snapshot);
        var projectedAction = today.Snapshot.Actions.Single(item => item.ActionId == firstReceipt.PrimaryActionId);

        Assert.AreEqual(firstReceipt.CaseId, projectedAction.CaseId);
        Assert.AreEqual(caseTitle, projectedAction.CaseTitle);
        Assert.AreEqual(actionText, projectedAction.ActionText);
        Assert.AreEqual(dueOn, projectedAction.DueOn);
        Assert.AreEqual(ActionDueBucket.Today, projectedAction.DueBucket);
        Assert.AreEqual(initialFocus.Snapshot.OrganizationBusinessDate, projectedAction.OrganizationBusinessDate);
        Assert.AreEqual(initialFocus.Snapshot.OrganizationTimeZone, projectedAction.OrganizationTimeZone);
    }

    private static async Task<LearningCaseReceipt> CreateLearningCaseAsync(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        string accessToken,
        Guid operationId,
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid assignmentId,
        string title,
        string actionText,
        DateOnly dueOn)
    {
        var baseUri = projectUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? projectUri
            : new Uri(projectUri.AbsoluteUri + "/", UriKind.Absolute);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/create_learning_case"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Content = JsonContent.Create(new
        {
            p_operation_id = operationId,
            p_organization_id = organizationId,
            p_student_id = studentId,
            p_subject_profile_id = subjectProfileId,
            p_owner_assignment_id = assignmentId,
            p_title = title,
            p_primary_action_text = actionText,
            p_primary_action_due_on = dueOn,
            p_source_observation_id = (Guid?)null,
        });

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode,
            $"CreateLearningCase failed with HTTP {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.AreEqual("create_learning_case_v1", root.GetProperty("command").GetString());
        return new LearningCaseReceipt(
            Guid.Parse(root.GetProperty("case_id").GetString()
                ?? throw new InvalidDataException("CreateLearningCase receipt is missing case_id.")),
            Guid.Parse(root.GetProperty("primary_action_id").GetString()
                ?? throw new InvalidDataException("CreateLearningCase receipt is missing primary_action_id.")),
            root.GetProperty("server_committed_at").GetDateTimeOffset());
    }

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Required E2E environment variable '{name}' is missing.")
            : value;
    }

    private sealed record LearningCaseReceipt(
        Guid CaseId,
        Guid PrimaryActionId,
        DateTimeOffset ServerCommittedAt);
}
