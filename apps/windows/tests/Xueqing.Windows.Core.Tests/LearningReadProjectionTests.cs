using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class LearningReadProjectionTests
{
    private static readonly Guid ActorId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OrganizationId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid StudentId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid SubjectProfileId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid AssignmentId = Guid.Parse("50000000-0000-0000-0000-000000000001");

    [TestMethod]
    public async Task Focus_reader_accepts_authoritative_scope_and_business_date()
    {
        var handler = new StubHandler(HttpStatusCode.OK, FocusEnvelope());
        var reader = CreateFocusReader(handler);

        var result = await reader.ReadAsync(Scope(), ActorId);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Snapshot);
        Assert.AreEqual("Asia/Shanghai", result.Snapshot.OrganizationTimeZone);
        Assert.AreEqual(new DateOnly(2026, 9, 19), result.Snapshot.OrganizationBusinessDate);
        Assert.AreEqual(1, result.Snapshot.Cases.Count);
        Assert.AreEqual(ActionDueBucket.Today, result.Snapshot.Cases[0].PrimaryAction.DueBucket);
        Assert.AreEqual("Bearer access-token", handler.LastAuthorization);
        StringAssert.Contains(handler.LastBody ?? string.Empty, "p_subject_profile_id");
    }

    [TestMethod]
    public async Task Focus_reader_fails_closed_on_scope_actor_or_due_bucket_mismatch()
    {
        var wrongStudent = Guid.Parse("30000000-0000-0000-0000-000000000009");
        var wrongActor = Guid.Parse("10000000-0000-0000-0000-000000000009");

        var wrongScope = await CreateFocusReader(
            new StubHandler(HttpStatusCode.OK, FocusEnvelope(studentId: wrongStudent)))
            .ReadAsync(Scope(), ActorId);
        var wrongIdentity = await CreateFocusReader(
            new StubHandler(HttpStatusCode.OK, FocusEnvelope(actorId: wrongActor)))
            .ReadAsync(Scope(), ActorId);
        var wrongBucket = await CreateFocusReader(
            new StubHandler(HttpStatusCode.OK, FocusEnvelope(dueBucket: "future")))
            .ReadAsync(Scope(), ActorId);

        Assert.AreEqual(LearningReadFailureKind.InvalidResponse, wrongScope.Failure?.Kind);
        Assert.AreEqual(LearningReadFailureKind.InvalidResponse, wrongIdentity.Failure?.Kind);
        Assert.AreEqual(LearningReadFailureKind.InvalidResponse, wrongBucket.Failure?.Kind);
    }

    [TestMethod]
    public async Task Focus_reader_rejects_closed_duplicate_or_out_of_order_cases()
    {
        var closed = FocusEnvelope(caseState: "closed");

        var duplicateCases = $$"""
            [
              {{FocusCaseJson(
                  "61000000-0000-0000-0000-000000000001",
                  "62000000-0000-0000-0000-000000000001",
                  "2026-09-19T05:00:00Z")}},
              {{FocusCaseJson(
                  "61000000-0000-0000-0000-000000000001",
                  "62000000-0000-0000-0000-000000000002",
                  "2026-09-19T04:00:00Z")}}
            ]
            """;

        var outOfOrderCases = $$"""
            [
              {{FocusCaseJson(
                  "61000000-0000-0000-0000-000000000001",
                  "62000000-0000-0000-0000-000000000001",
                  "2026-09-19T04:00:00Z")}},
              {{FocusCaseJson(
                  "61000000-0000-0000-0000-000000000002",
                  "62000000-0000-0000-0000-000000000002",
                  "2026-09-19T05:00:00Z")}}
            ]
            """;

        var closedResult = await CreateFocusReader(new StubHandler(HttpStatusCode.OK, closed)).ReadAsync(Scope(), ActorId);
        var duplicateResult = await CreateFocusReader(
            new StubHandler(HttpStatusCode.OK, FocusEnvelope(casesJson: duplicateCases, hasMore: false)))
            .ReadAsync(Scope(), ActorId);
        var orderResult = await CreateFocusReader(
            new StubHandler(HttpStatusCode.OK, FocusEnvelope(casesJson: outOfOrderCases, hasMore: false)))
            .ReadAsync(Scope(), ActorId);

        Assert.AreEqual(LearningReadFailureKind.InvalidResponse, closedResult.Failure?.Kind);
        Assert.AreEqual(LearningReadFailureKind.InvalidResponse, duplicateResult.Failure?.Kind);
        Assert.AreEqual(LearningReadFailureKind.InvalidResponse, orderResult.Failure?.Kind);
    }

    [TestMethod]
    public async Task Today_reader_accepts_server_bucket_order_across_organizations()
    {
        var actions = $$"""
            [
              {{TodayActionJson(
                  "20000000-0000-0000-0000-000000000001",
                  "虚构机构甲",
                  "Asia/Shanghai",
                  "2026-09-19",
                  "61000000-0000-0000-0000-000000000001",
                  "62000000-0000-0000-0000-000000000001",
                  "2026-09-18",
                  "overdue",
                  "2026-09-19T05:00:00Z")}},
              {{TodayActionJson(
                  "20000000-0000-0000-0000-000000000002",
                  "虚构机构乙",
                  "America/New_York",
                  "2026-09-18",
                  "61000000-0000-0000-0000-000000000002",
                  "62000000-0000-0000-0000-000000000002",
                  "2026-09-18",
                  "today",
                  "2026-09-19T04:00:00Z")}},
              {{TodayActionJson(
                  "20000000-0000-0000-0000-000000000001",
                  "虚构机构甲",
                  "Asia/Shanghai",
                  "2026-09-19",
                  "61000000-0000-0000-0000-000000000003",
                  "62000000-0000-0000-0000-000000000003",
                  null,
                  "undated",
                  "2026-09-19T03:00:00Z")}}
            ]
            """;
        var handler = new StubHandler(HttpStatusCode.OK, TodayEnvelope(actions));
        var reader = CreateTodayReader(handler);

        var result = await reader.ReadAsync(ActorId);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Snapshot);
        CollectionAssert.AreEqual(
            new[] { ActionDueBucket.Overdue, ActionDueBucket.Today, ActionDueBucket.Undated },
            result.Snapshot.Actions.Select(action => action.DueBucket).ToArray());
        Assert.AreEqual(new DateOnly(2026, 9, 18), result.Snapshot.Actions[1].OrganizationBusinessDate);
        Assert.AreEqual("{}", handler.LastBody);
    }

    [TestMethod]
    public async Task Today_reader_rejects_inconsistent_business_semantics_bucket_or_order()
    {
        var inconsistentOrganization = $$"""
            [
              {{TodayActionJson(
                  "20000000-0000-0000-0000-000000000001",
                  "虚构机构甲",
                  "Asia/Shanghai",
                  "2026-09-19",
                  "61000000-0000-0000-0000-000000000001",
                  "62000000-0000-0000-0000-000000000001",
                  "2026-09-19",
                  "today",
                  "2026-09-19T05:00:00Z")}},
              {{TodayActionJson(
                  "20000000-0000-0000-0000-000000000001",
                  "虚构机构甲",
                  "Asia/Shanghai",
                  "2026-09-20",
                  "61000000-0000-0000-0000-000000000002",
                  "62000000-0000-0000-0000-000000000002",
                  "2026-09-20",
                  "today",
                  "2026-09-19T04:00:00Z")}}
            ]
            """;

        var wrongBucket = $$"""
            [
              {{TodayActionJson(
                  "20000000-0000-0000-0000-000000000001",
                  "虚构机构甲",
                  "Asia/Shanghai",
                  "2026-09-19",
                  "61000000-0000-0000-0000-000000000001",
                  "62000000-0000-0000-0000-000000000001",
                  "2026-09-20",
                  "today",
                  "2026-09-19T05:00:00Z")}}
            ]
            """;

        var wrongOrder = $$"""
            [
              {{TodayActionJson(
                  "20000000-0000-0000-0000-000000000001",
                  "虚构机构甲",
                  "Asia/Shanghai",
                  "2026-09-19",
                  "61000000-0000-0000-0000-000000000001",
                  "62000000-0000-0000-0000-000000000001",
                  "2026-09-19",
                  "today",
                  "2026-09-19T05:00:00Z")}},
              {{TodayActionJson(
                  "20000000-0000-0000-0000-000000000001",
                  "虚构机构甲",
                  "Asia/Shanghai",
                  "2026-09-19",
                  "61000000-0000-0000-0000-000000000002",
                  "62000000-0000-0000-0000-000000000002",
                  "2026-09-18",
                  "overdue",
                  "2026-09-19T04:00:00Z")}}
            ]
            """;

        foreach (var payload in new[] { inconsistentOrganization, wrongBucket, wrongOrder })
        {
            var result = await CreateTodayReader(
                new StubHandler(HttpStatusCode.OK, TodayEnvelope(payload)))
                .ReadAsync(ActorId);
            Assert.AreEqual(LearningReadFailureKind.InvalidResponse, result.Failure?.Kind);
        }
    }

    [TestMethod]
    public async Task Learning_readers_map_authority_invariant_and_transient_failures()
    {
        var focusDenied = CreateFocusReader(
            new StubHandler(
                HttpStatusCode.BadRequest,
                """{"code":"P0001","message":"XQ_TEACHING_CONTEXT_UNAVAILABLE"}"""));
        var todayInvariant = CreateTodayReader(
            new StubHandler(
                HttpStatusCode.BadRequest,
                """{"code":"P0001","message":"XQ_CASE_PRIMARY_ACTION_INVARIANT"}"""));
        var todayTransient = CreateTodayReader(new StubHandler(HttpStatusCode.ServiceUnavailable, "{}"));

        var denied = await focusDenied.ReadAsync(Scope(), ActorId);
        var invariant = await todayInvariant.ReadAsync(ActorId);
        var transient = await todayTransient.ReadAsync(ActorId);

        Assert.AreEqual(LearningReadFailureKind.AccessDenied, denied.Failure?.Kind);
        Assert.AreEqual(LearningReadFailureKind.ServerInvariant, invariant.Failure?.Kind);
        Assert.AreEqual(LearningReadFailureKind.Transient, transient.Failure?.Kind);
    }

    [TestMethod]
    public async Task Learning_readers_do_not_send_without_live_access_token()
    {
        var focusHandler = new StubHandler(HttpStatusCode.OK, FocusEnvelope());
        var todayHandler = new StubHandler(HttpStatusCode.OK, TodayEnvelope("[]"));

        var focus = new PostgrestStudentLearningFocusReader(
            new HttpClient(focusHandler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>(null));
        var today = new PostgrestPersonalTodayActionsReader(
            new HttpClient(todayHandler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>(null));

        Assert.AreEqual(
            LearningReadFailureKind.AuthenticationRequired,
            (await focus.ReadAsync(Scope(), ActorId)).Failure?.Kind);
        Assert.AreEqual(
            LearningReadFailureKind.AuthenticationRequired,
            (await today.ReadAsync(ActorId)).Failure?.Kind);
        Assert.AreEqual(0, focusHandler.RequestCount);
        Assert.AreEqual(0, todayHandler.RequestCount);
    }

    [TestMethod]
    public void Learning_readers_reject_non_loopback_plain_http()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            new PostgrestStudentLearningFocusReader(
                new HttpClient(new StubHandler(HttpStatusCode.OK, FocusEnvelope())),
                new Uri("http://example.test/"),
                "publishable-key",
                _ => ValueTask.FromResult<string?>("token")));

        Assert.ThrowsException<ArgumentException>(() =>
            new PostgrestPersonalTodayActionsReader(
                new HttpClient(new StubHandler(HttpStatusCode.OK, TodayEnvelope("[]"))),
                new Uri("http://example.test/"),
                "publishable-key",
                _ => ValueTask.FromResult<string?>("token")));
    }

    private static PostgrestStudentLearningFocusReader CreateFocusReader(StubHandler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>("access-token"));

    private static PostgrestPersonalTodayActionsReader CreateTodayReader(StubHandler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>("access-token"));

    private static StudentLearningScope Scope() => new(OrganizationId, StudentId, SubjectProfileId);

    private static string FocusEnvelope(
        Guid? studentId = null,
        Guid? actorId = null,
        string caseState = "new",
        string dueBucket = "today",
        string? casesJson = null,
        bool hasMore = false)
    {
        casesJson ??= $$"""
            [
              {{FocusCaseJson(
                  "61000000-0000-0000-0000-000000000001",
                  "62000000-0000-0000-0000-000000000001",
                  "2026-09-19T05:00:00Z",
                  caseState,
                  dueBucket)}}
            ]
            """;

        return $$"""
            {
              "contract":"student_learning_focus_v1",
              "generated_at_server":"2026-09-19T06:00:00Z",
              "actor_app_user_id":"{{(actorId ?? ActorId):D}}",
              "organization_id":"{{OrganizationId:D}}",
              "organization_name":"虚构机构甲",
              "organization_time_zone":"Asia/Shanghai",
              "organization_business_date":"2026-09-19",
              "student_id":"{{(studentId ?? StudentId):D}}",
              "student_display_name":"虚构学生甲",
              "subject_profile_id":"{{SubjectProfileId:D}}",
              "subject_key":"chinese",
              "assignment_id":"{{AssignmentId:D}}",
              "cases":{{casesJson}},
              "has_more":{{hasMore.ToString().ToLowerInvariant()}}
            }
            """;
    }

    private static string FocusCaseJson(
        string caseId,
        string actionId,
        string updatedAt,
        string caseState = "new",
        string dueBucket = "today") => $$"""
        {
          "case_id":"{{caseId}}",
          "title":"虚构学情问题",
          "state":"{{caseState}}",
          "case_version":1,
          "responsible_teacher_app_user_id":"{{ActorId:D}}",
          "owner_assignment_id":"{{AssignmentId:D}}",
          "created_at_server":"2026-09-19T01:00:00Z",
          "updated_at_server":"{{updatedAt}}",
          "primary_action":{
            "action_id":"{{actionId}}",
            "action_text":"虚构下一步行动",
            "due_on":"2026-09-19",
            "due_bucket":"{{dueBucket}}",
            "action_version":1
          }
        }
        """;

    private static string TodayEnvelope(string actionsJson) => $$"""
        {
          "contract":"personal_today_actions_v1",
          "generated_at_server":"2026-09-19T06:00:00Z",
          "actor_app_user_id":"{{ActorId:D}}",
          "actions":{{actionsJson}},
          "has_more":false
        }
        """;

    private static string TodayActionJson(
        string organizationId,
        string organizationName,
        string timeZone,
        string businessDate,
        string caseId,
        string actionId,
        string? dueOn,
        string dueBucket,
        string caseUpdatedAt)
    {
        var dueJson = dueOn is null
            ? "null"
            : System.Text.Json.JsonSerializer.Serialize(dueOn);
        return $$"""
        {
          "organization_id":"{{organizationId}}",
          "organization_name":"{{organizationName}}",
          "organization_time_zone":"{{timeZone}}",
          "organization_business_date":"{{businessDate}}",
          "student_id":"30000000-0000-0000-0000-000000000001",
          "student_display_name":"虚构学生甲",
          "subject_profile_id":"40000000-0000-0000-0000-000000000001",
          "subject_key":"chinese",
          "assignment_id":"50000000-0000-0000-0000-000000000001",
          "case_id":"{{caseId}}",
          "case_title":"虚构学情问题",
          "case_state":"new",
          "case_version":1,
          "action_id":"{{actionId}}",
          "action_text":"虚构下一步行动",
          "due_on":{{dueJson}},
          "due_bucket":"{{dueBucket}}",
          "action_version":1,
          "case_updated_at_server":"{{caseUpdatedAt}}"
        }
        """;
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
