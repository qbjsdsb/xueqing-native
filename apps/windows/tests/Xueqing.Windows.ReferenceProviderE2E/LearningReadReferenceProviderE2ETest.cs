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

        const string sourceText = "Windows E2E · 从真实课堂观察形成学情问题";
        var sourceObservationId = await CreateSourceObservationAsync(
            httpClient,
            projectUri,
            apiKey,
            accessToken,
            context,
            sourceText);

        var recentReader = new PostgrestStudentRecentObservationsReader(
            httpClient,
            projectUri,
            apiKey,
            _ => ValueTask.FromResult<string?>(accessToken));
        var recent = await recentReader.ReadAsync(
            context.ObservationScope,
            bootstrap.Snapshot.ActorAppUserId);
        Assert.IsTrue(recent.IsSuccess, recent.Failure?.Code);
        Assert.IsNotNull(recent.Snapshot);
        Assert.AreEqual(
            sourceText,
            recent.Snapshot.Observations.Single(
                item => item.ObservationId == sourceObservationId).RawText);

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
            sourceObservationId);

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
        Assert.AreEqual(sourceObservationId, firstReceipt.SourceObservationId);

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


    [TestMethod]
    public async Task Action_progression_updates_Focus_and_Today_through_real_reference_provider()
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
        var focusReader = new PostgrestStudentLearningFocusReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var todayReader = new PostgrestPersonalTodayActionsReader(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);

        var baselineFocus = await focusReader.ReadAsync(scope, actorId);
        Assert.IsTrue(baselineFocus.IsSuccess, baselineFocus.Failure?.Code);
        Assert.IsNotNull(baselineFocus.Snapshot);
        var businessDate = baselineFocus.Snapshot.OrganizationBusinessDate;

        var createCase = new PostgrestCreateLearningCaseCommand(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var createRequest = new CreateLearningCaseRequest(
            Guid.Parse("76000000-0000-0000-0000-000000000001"),
            context.OrganizationId,
            context.StudentId,
            context.SubjectProfileId,
            context.AssignmentId,
            "Windows E2E · 行动推进闭环",
            "Windows E2E · 第一轮行动",
            businessDate,
            null);
        var created = await createCase.ExecuteAsync(createRequest, actorId);
        Assert.IsTrue(created.IsSuccess, created.Failure?.Code);
        Assert.IsNotNull(created.Receipt);

        var focusAfterCreate = await focusReader.ReadAsync(scope, actorId);
        Assert.IsTrue(focusAfterCreate.IsSuccess, focusAfterCreate.Failure?.Code);
        Assert.IsNotNull(focusAfterCreate.Snapshot);
        var createdCase = focusAfterCreate.Snapshot.Cases.Single(
            item => item.CaseId == created.Receipt.CaseId);
        var initialTarget = ActionProgressionTargetResolver.FromFocus(
            focusAfterCreate.Snapshot,
            createdCase);

        var reschedule = new PostgrestReschedulePrimaryActionCommand(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var rescheduleDue = businessDate.AddDays(2);
        var rescheduleRequest = initialTarget.CreateRescheduleRequest(
            Guid.Parse("76000000-0000-0000-0000-000000000002"),
            rescheduleDue);

        var rescheduled = await reschedule.ExecuteAsync(rescheduleRequest, actorId);
        var rescheduledReplay = await reschedule.ExecuteAsync(rescheduleRequest, actorId);

        Assert.IsTrue(rescheduled.IsSuccess, rescheduled.Failure?.Code);
        Assert.IsTrue(rescheduledReplay.IsSuccess, rescheduledReplay.Failure?.Code);
        Assert.IsNotNull(rescheduled.Receipt);
        Assert.IsNotNull(rescheduledReplay.Receipt);
        Assert.AreEqual(rescheduled.Receipt.CaseEventId, rescheduledReplay.Receipt.CaseEventId);
        Assert.AreEqual(rescheduled.Receipt.ServerCommittedAt, rescheduledReplay.Receipt.ServerCommittedAt);
        Assert.AreEqual(initialTarget.CaseVersion + 1, rescheduled.Receipt.CaseVersion);
        Assert.AreEqual(initialTarget.ActionVersion + 1, rescheduled.Receipt.ActionVersion);
        Assert.AreEqual(initialTarget.CurrentDueOn, rescheduled.Receipt.PreviousDueOn);
        Assert.AreEqual(rescheduleDue, rescheduled.Receipt.DueOn);

        var focusAfterReschedule = await focusReader.ReadAsync(scope, actorId);
        Assert.IsTrue(focusAfterReschedule.IsSuccess, focusAfterReschedule.Failure?.Code);
        Assert.IsNotNull(focusAfterReschedule.Snapshot);
        var rescheduledCase = focusAfterReschedule.Snapshot.Cases.Single(
            item => item.CaseId == created.Receipt.CaseId);
        Assert.AreEqual(rescheduleDue, rescheduledCase.PrimaryAction.DueOn);
        Assert.AreEqual(rescheduled.Receipt.CaseVersion, rescheduledCase.Version);
        Assert.AreEqual(rescheduled.Receipt.ActionVersion, rescheduledCase.PrimaryAction.Version);

        var todayAfterReschedule = await todayReader.ReadAsync(actorId);
        Assert.IsTrue(todayAfterReschedule.IsSuccess, todayAfterReschedule.Failure?.Code);
        Assert.IsNotNull(todayAfterReschedule.Snapshot);
        var rescheduledToday = todayAfterReschedule.Snapshot.Actions.Single(
            item => item.ActionId == created.Receipt.PrimaryActionId);
        Assert.AreEqual(rescheduleDue, rescheduledToday.DueOn);
        Assert.AreEqual(ActionDueBucket.Future, rescheduledToday.DueBucket);
        Assert.AreEqual(rescheduled.Receipt.CaseVersion, rescheduledToday.CaseVersion);
        Assert.AreEqual(rescheduled.Receipt.ActionVersion, rescheduledToday.ActionVersion);

        var targetAfterReschedule = ActionProgressionTargetResolver.FromFocus(
            focusAfterReschedule.Snapshot,
            rescheduledCase);
        var verification = new PostgrestRecordVerificationAndNextActionCommand(
            httpClient,
            projectUri,
            apiKey,
            AccessTokenProvider);
        var nextDue = businessDate.AddDays(3);
        var verificationRequest = targetAfterReschedule.CreateVerificationRequest(
            Guid.Parse("76000000-0000-0000-0000-000000000003"),
            VerificationOutcome.PartiallyMet,
            "Windows E2E · 已有进步，但限制条件仍有遗漏。",
            "Windows E2E · 下一轮只检查限制条件是否保留。",
            nextDue);

        var verified = await verification.ExecuteAsync(verificationRequest, actorId);
        var verifiedReplay = await verification.ExecuteAsync(verificationRequest, actorId);

        Assert.IsTrue(verified.IsSuccess, verified.Failure?.Code);
        Assert.IsTrue(verifiedReplay.IsSuccess, verifiedReplay.Failure?.Code);
        Assert.IsNotNull(verified.Receipt);
        Assert.IsNotNull(verifiedReplay.Receipt);
        Assert.AreEqual(verified.Receipt.VerificationId, verifiedReplay.Receipt.VerificationId);
        Assert.AreEqual(verified.Receipt.NextPrimaryActionId, verifiedReplay.Receipt.NextPrimaryActionId);
        Assert.AreEqual(verified.Receipt.ServerCommittedAt, verifiedReplay.Receipt.ServerCommittedAt);
        Assert.AreEqual(targetAfterReschedule.CaseVersion + 1, verified.Receipt.CaseVersion);
        Assert.AreEqual(
            targetAfterReschedule.ActionVersion + 1,
            verified.Receipt.CompletedActionVersion);
        Assert.AreEqual(1L, verified.Receipt.NextActionVersion);

        var focusAfterVerification = await focusReader.ReadAsync(scope, actorId);
        Assert.IsTrue(focusAfterVerification.IsSuccess, focusAfterVerification.Failure?.Code);
        Assert.IsNotNull(focusAfterVerification.Snapshot);
        var progressedCase = focusAfterVerification.Snapshot.Cases.Single(
            item => item.CaseId == created.Receipt.CaseId);
        Assert.AreEqual(verified.Receipt.CaseVersion, progressedCase.Version);
        Assert.AreEqual(verified.Receipt.NextPrimaryActionId, progressedCase.PrimaryAction.ActionId);
        Assert.AreEqual(verificationRequest.NextActionText, progressedCase.PrimaryAction.ActionText);
        Assert.AreEqual(nextDue, progressedCase.PrimaryAction.DueOn);
        Assert.AreEqual(1L, progressedCase.PrimaryAction.Version);

        var todayAfterVerification = await todayReader.ReadAsync(actorId);
        Assert.IsTrue(todayAfterVerification.IsSuccess, todayAfterVerification.Failure?.Code);
        Assert.IsNotNull(todayAfterVerification.Snapshot);
        Assert.IsFalse(
            todayAfterVerification.Snapshot.Actions.Any(
                item => item.ActionId == created.Receipt.PrimaryActionId),
            "Completed primary Action must disappear from Personal Today.");
        var nextToday = todayAfterVerification.Snapshot.Actions.Single(
            item => item.ActionId == verified.Receipt.NextPrimaryActionId);
        Assert.AreEqual(verificationRequest.NextActionText, nextToday.ActionText);
        Assert.AreEqual(nextDue, nextToday.DueOn);
        Assert.AreEqual(ActionDueBucket.Future, nextToday.DueBucket);
        Assert.AreEqual(verified.Receipt.CaseVersion, nextToday.CaseVersion);
        Assert.AreEqual(1L, nextToday.ActionVersion);
    }


    private static async Task<Guid> CreateSourceObservationAsync(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        string accessToken,
        PersonalTeachingContext context,
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
            p_operation_id = Guid.Parse("74000000-0000-0000-0000-000000000038"),
            p_organization_id = context.OrganizationId,
            p_student_id = context.StudentId,
            p_subject_profile_id = context.SubjectProfileId,
            p_assignment_id = context.AssignmentId,
            p_raw_text = rawText,
            p_client_captured_at = (DateTimeOffset?)null,
            p_client_capture_metadata = new
            {
                source = "windows_learning_case_e2e",
            },
        });

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(
            response.IsSuccessStatusCode,
            $"CreateObservation fixture failed with HTTP {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        return Guid.Parse(
            document.RootElement.GetProperty("observation_id").GetString()
            ?? throw new InvalidDataException("CreateObservation receipt is missing observation_id."));
    }

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Required E2E environment variable '{name}' is missing.")
            : value;
    }
}
