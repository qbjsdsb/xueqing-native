using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.ReferenceProviderE2E;

[TestClass]
public sealed class CaseLifecycleReferenceProviderE2ETest
{
    [TestMethod]
    public async Task Windows_commands_drive_close_and_reopen_through_authoritative_projections()
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
        var actorId = bootstrap.Snapshot.ActorAppUserId;
        var context = bootstrap.Snapshot.TeachingContexts.Single();
        var scope = new StudentLearningScope(
            context.OrganizationId,
            context.StudentId,
            context.SubjectProfileId);

        var create = new PostgrestCreateLearningCaseCommand(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var created = await create.ExecuteAsync(
            new CreateLearningCaseRequest(
                Guid.Parse("78000000-0000-0000-0000-000000000001"),
                context.OrganizationId,
                context.StudentId,
                context.SubjectProfileId,
                context.AssignmentId,
                "Windows E2E · lifecycle adapter",
                "关闭前保持唯一待办行动",
                null,
                null),
            actorId);
        Assert.IsTrue(created.IsSuccess, created.Failure?.Code);
        Assert.IsNotNull(created.Receipt);

        var caseId = created.Receipt.CaseId;
        var originalActionId = created.Receipt.PrimaryActionId;
        var transition = new PostgrestTransitionLearningCaseStateCommand(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);

        var states = new[]
        {
            LearningCaseState.Confirmed,
            LearningCaseState.Intervening,
            LearningCaseState.PendingVerification,
            LearningCaseState.Stable,
        };
        long expectedCaseVersion = 1;
        for (var index = 0; index < states.Length; index++)
        {
            var result = await transition.ExecuteAsync(
                new TransitionLearningCaseStateRequest(
                    Guid.Parse($"78000000-0000-0000-0000-00000000000{index + 2}"),
                    context.OrganizationId,
                    context.StudentId,
                    context.SubjectProfileId,
                    context.AssignmentId,
                    caseId,
                    expectedCaseVersion,
                    states[index]),
                actorId);
            Assert.IsTrue(result.IsSuccess, result.Failure?.Code);
            Assert.AreEqual(states[index], result.Receipt!.CaseState);
            expectedCaseVersion = result.Receipt.CaseVersion;
        }

        var close = new PostgrestCloseLearningCaseCommand(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var closed = await close.ExecuteAsync(
            new CloseLearningCaseRequest(
                Guid.Parse("78000000-0000-0000-0000-000000000006"),
                context.OrganizationId,
                context.StudentId,
                context.SubjectProfileId,
                context.AssignmentId,
                caseId,
                originalActionId,
                5,
                1),
            actorId);
        Assert.IsTrue(closed.IsSuccess, closed.Failure?.Code);
        Assert.AreEqual(LearningCaseState.Closed, closed.Receipt!.CaseState);

        var historyReader = new PostgrestStudentLearningCasesReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var afterClose = await historyReader.ReadAsync(scope, actorId);
        Assert.IsTrue(afterClose.IsSuccess, afterClose.Failure?.Code);
        var closedSummary = afterClose.Snapshot!.Cases.Single(item => item.CaseId == caseId);
        Assert.AreEqual(LearningCaseState.Closed, closedSummary.State);
        Assert.IsNull(closedSummary.PrimaryAction);

        var reopen = new PostgrestReopenLearningCaseCommand(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var reopened = await reopen.ExecuteAsync(
            new ReopenLearningCaseRequest(
                Guid.Parse("78000000-0000-0000-0000-000000000007"),
                context.OrganizationId,
                context.StudentId,
                context.SubjectProfileId,
                context.AssignmentId,
                caseId,
                6,
                "Windows E2E · 重开后的新行动",
                new DateOnly(2026, 9, 30)),
            actorId);
        Assert.IsTrue(reopened.IsSuccess, reopened.Failure?.Code);
        Assert.AreEqual(LearningCaseState.Intervening, reopened.Receipt!.CaseState);
        Assert.AreEqual(1L, reopened.Receipt.NewActionVersion);
        Assert.AreNotEqual(originalActionId, reopened.Receipt.NewPrimaryActionId);

        var afterReopen = await historyReader.ReadAsync(scope, actorId);
        Assert.IsTrue(afterReopen.IsSuccess, afterReopen.Failure?.Code);
        var openSummary = afterReopen.Snapshot!.Cases.Single(item => item.CaseId == caseId);
        Assert.AreEqual(LearningCaseState.Intervening, openSummary.State);
        Assert.IsNotNull(openSummary.PrimaryAction);
        Assert.AreEqual(
            reopened.Receipt.NewPrimaryActionId,
            openSummary.PrimaryAction.ActionId);

        var focusReader = new PostgrestStudentLearningFocusReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var focus = await focusReader.ReadAsync(scope, actorId);
        Assert.IsTrue(focus.IsSuccess, focus.Failure?.Code);
        Assert.IsTrue(
            focus.Snapshot!.Cases.Any(item =>
                item.CaseId == caseId &&
                item.PrimaryAction.ActionId == reopened.Receipt.NewPrimaryActionId));

        var todayReader = new PostgrestPersonalTodayActionsReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var today = await todayReader.ReadAsync(actorId);
        Assert.IsTrue(today.IsSuccess, today.Failure?.Code);
        Assert.IsTrue(
            today.Snapshot!.Actions.Any(item =>
                item.CaseId == caseId &&
                item.ActionId == reopened.Receipt.NewPrimaryActionId));
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
