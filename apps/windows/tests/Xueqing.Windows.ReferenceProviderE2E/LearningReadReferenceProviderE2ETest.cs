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

        var createCase = new PostgrestCreateLearningCaseCommand(
            httpClient,
            projectUri,
            apiKey,
            _ => ValueTask.FromResult<string?>(accessToken));
        var commandRequest = new CreateLearningCaseRequest(
            operationId,
            context.OrganizationId,
            context.StudentId,
            context.SubjectProfileId,
            context.AssignmentId,
            caseTitle,
            actionText,
            dueOn,
            null);

        var firstResult = await createCase.ExecuteAsync(
            commandRequest,
            bootstrap.Snapshot.ActorAppUserId);
        var retryResult = await createCase.ExecuteAsync(
            commandRequest,
            bootstrap.Snapshot.ActorAppUserId);

        Assert.IsTrue(firstResult.IsSuccess, firstResult.Failure?.Code);
        Assert.IsTrue(retryResult.IsSuccess, retryResult.Failure?.Code);
        Assert.IsNotNull(firstResult.Receipt);
        Assert.IsNotNull(retryResult.Receipt);
        var firstReceipt = firstResult.Receipt;
        var retryReceipt = retryResult.Receipt;

        Assert.AreEqual(firstReceipt.CaseId, retryReceipt.CaseId);
        Assert.AreEqual(firstReceipt.PrimaryActionId, retryReceipt.PrimaryActionId);
        Assert.AreEqual(firstReceipt.ServerCommittedAt, retryReceipt.ServerCommittedAt);
        Assert.AreEqual(operationId, firstReceipt.OperationId);
        Assert.AreEqual(bootstrap.Snapshot.ActorAppUserId, firstReceipt.ResponsibleTeacherAppUserId);
        Assert.AreEqual(context.AssignmentId, firstReceipt.OwnerAssignmentId);

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

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Required E2E environment variable '{name}' is missing.")
            : value;
    }
}
