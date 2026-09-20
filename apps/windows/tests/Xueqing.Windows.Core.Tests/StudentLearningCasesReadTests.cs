using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class StudentLearningCasesReadTests
{
    private static readonly StudentLearningScope Scope = new(
        Guid.Parse("20000000-0000-0000-0000-000000000001"),
        Guid.Parse("30000000-0000-0000-0000-000000000001"),
        Guid.Parse("40000000-0000-0000-0000-000000000001"));

    private static readonly Guid ActorId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    [TestMethod]
    public void Parser_accepts_current_open_and_historical_closed_cases()
    {
        var snapshot = StudentLearningCasesJsonParser.Parse(
            ValidJson(),
            Scope,
            ActorId);

        Assert.AreEqual(2, snapshot.Cases.Count);
        Assert.AreEqual(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            snapshot.AssignmentId);

        var openCase = snapshot.Cases[0];
        Assert.AreEqual(LearningCaseState.Intervening, openCase.State);
        Assert.IsTrue(openCase.IsCurrentActorResponsibility);
        Assert.IsNotNull(openCase.PrimaryAction);
        Assert.AreEqual("今天复核限制条件", openCase.PrimaryAction.ActionText);
        Assert.AreEqual(ActionDueBucket.Today, openCase.PrimaryAction.DueBucket);

        var closedCase = snapshot.Cases[1];
        Assert.AreEqual(LearningCaseState.Closed, closedCase.State);
        Assert.IsFalse(closedCase.IsCurrentActorResponsibility);
        Assert.IsNull(closedCase.PrimaryAction);
        Assert.AreEqual(
            Guid.Parse("10000000-0000-0000-0000-000000000002"),
            closedCase.ResponsibleTeacherAppUserId);
    }

    [TestMethod]
    public void Parser_rejects_responsibility_marker_that_conflicts_with_ids()
    {
        var root = JsonNode.Parse(ValidJson())!.AsObject();
        root["cases"]!.AsArray()[0]!["is_current_actor_responsibility"] = false;

        ExpectThrows<InvalidDataException>(
            () => StudentLearningCasesJsonParser.Parse(root.ToJsonString(), Scope, ActorId));
    }

    [TestMethod]
    public void Parser_rejects_current_action_on_closed_case()
    {
        var root = JsonNode.Parse(ValidJson())!.AsObject();
        root["cases"]!.AsArray()[1]!["primary_action"] = JsonNode.Parse(
            """
            {
              "action_id":"71000000-0000-0000-0000-000000000002",
              "action_text":"不应存在",
              "due_on":null,
              "due_bucket":"undated",
              "action_version":2
            }
            """);

        ExpectThrows<InvalidDataException>(
            () => StudentLearningCasesJsonParser.Parse(root.ToJsonString(), Scope, ActorId));
    }

    [TestMethod]
    public void Parser_rejects_missing_action_on_open_case()
    {
        var root = JsonNode.Parse(ValidJson())!.AsObject();
        root["cases"]!.AsArray()[0]!["primary_action"] = null;

        ExpectThrows<InvalidDataException>(
            () => StudentLearningCasesJsonParser.Parse(root.ToJsonString(), Scope, ActorId));
    }

    [TestMethod]
    public async Task Reader_rejects_unverifiable_projection_contract()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"contract":"wrong"}""");
        using var client = new HttpClient(handler);
        var reader = new PostgrestStudentLearningCasesReader(
            client,
            new Uri("https://example.test"),
            "test-key",
            _ => ValueTask.FromResult<string?>("token"));

        var result = await reader.ReadAsync(Scope, ActorId);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            LearningReadFailureKind.InvalidResponse,
            result.Failure?.Kind);
        Assert.AreEqual("XQ_PROJECTION_CONTRACT_INVALID", result.Failure?.Code);
        StringAssert.Contains(handler.LastBody ?? string.Empty, Scope.StudentId.ToString("D"));
    }

    [TestMethod]
    public async Task Coordinator_maps_success_and_access_denial_without_leaking_old_snapshot()
    {
        var success = StudentLearningCasesJsonParser.Parse(ValidJson(), Scope, ActorId);
        var reader = new SequenceReader(
            StudentLearningCasesReadResult.Success(success),
            StudentLearningCasesReadResult.Failed(
                LearningReadFailureKind.AccessDenied,
                "XQ_TEACHING_CONTEXT_UNAVAILABLE"));
        var coordinator = new StudentLearningCasesCoordinator(reader);

        var loaded = await coordinator.LoadAsync(Scope, ActorId);
        Assert.AreEqual(StudentLearningCasesViewStatus.Data, loaded.Status);
        Assert.IsNotNull(loaded.Snapshot);

        var denied = await coordinator.LoadAsync(Scope, ActorId);
        Assert.AreEqual(StudentLearningCasesViewStatus.AccessDenied, denied.Status);
        Assert.IsNull(denied.Snapshot);
        Assert.AreEqual("XQ_TEACHING_CONTEXT_UNAVAILABLE", denied.FailureCode);
    }

    private static TException ExpectThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        Assert.Fail($"Expected exception of type {typeof(TException).Name}.");
        throw new InvalidOperationException("Unreachable.");
    }

    private static string ValidJson() =>
        """
        {
          "contract":"student_learning_cases_v1",
          "generated_at_server":"2026-09-20T00:00:00Z",
          "actor_app_user_id":"10000000-0000-0000-0000-000000000001",
          "organization_id":"20000000-0000-0000-0000-000000000001",
          "organization_name":"虚构机构甲",
          "organization_time_zone":"Asia/Shanghai",
          "organization_business_date":"2026-09-20",
          "student_id":"30000000-0000-0000-0000-000000000001",
          "student_display_name":"虚构学生甲",
          "subject_profile_id":"40000000-0000-0000-0000-000000000001",
          "subject_key":"chinese",
          "assignment_id":"50000000-0000-0000-0000-000000000001",
          "cases":[
            {
              "case_id":"70000000-0000-0000-0000-000000000001",
              "title":"当前 Case",
              "state":"intervening",
              "case_version":4,
              "responsible_teacher_app_user_id":"10000000-0000-0000-0000-000000000001",
              "owner_assignment_id":"50000000-0000-0000-0000-000000000001",
              "is_current_actor_responsibility":true,
              "created_at_server":"2026-09-18T00:00:00Z",
              "updated_at_server":"2026-09-20T00:00:00Z",
              "primary_action":{
                "action_id":"71000000-0000-0000-0000-000000000001",
                "action_text":"今天复核限制条件",
                "due_on":"2026-09-20",
                "due_bucket":"today",
                "action_version":3
              }
            },
            {
              "case_id":"70000000-0000-0000-0000-000000000002",
              "title":"历史 Case",
              "state":"closed",
              "case_version":6,
              "responsible_teacher_app_user_id":"10000000-0000-0000-0000-000000000002",
              "owner_assignment_id":"50000000-0000-0000-0000-000000000002",
              "is_current_actor_responsibility":false,
              "created_at_server":"2026-08-01T00:00:00Z",
              "updated_at_server":"2026-09-19T00:00:00Z",
              "primary_action":null
            }
          ],
          "has_more":false
        }
        """;

    private sealed class SequenceReader(params StudentLearningCasesReadResult[] results)
        : IStudentLearningCasesReader
    {
        private readonly Queue<StudentLearningCasesReadResult> _results = new(results);

        public Task<StudentLearningCasesReadResult> ReadAsync(
            StudentLearningScope scope,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_results.Dequeue());
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body)
        : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
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
