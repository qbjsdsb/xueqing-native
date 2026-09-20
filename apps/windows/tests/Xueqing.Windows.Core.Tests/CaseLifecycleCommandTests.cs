using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class CaseLifecycleCommandTests
{
    private static readonly Guid Actor = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Org = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Student = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid Profile = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid Assignment = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid Case = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid Action = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid Operation = Guid.Parse("82000000-0000-0000-0000-000000000001");

    [TestMethod]
    public async Task Transition_sends_expected_version_and_requires_bound_receipt()
    {
        var handler = new Handler(_ => Reply(HttpStatusCode.OK, TransitionReceipt()));
        var result = await TransitionCommand(handler).ExecuteAsync(Transition(), Actor);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(LearningCaseState.Stable, result.Receipt!.CaseState);
        Assert.AreEqual(5L, result.Receipt.CaseVersion);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.AreEqual(
            "stable",
            body.RootElement.GetProperty("p_target_state").GetString());
        Assert.AreEqual(
            4L,
            body.RootElement.GetProperty("p_expected_case_version").GetInt64());

        var wrongActor = await TransitionCommand(
            new Handler(_ => Reply(
                HttpStatusCode.OK,
                TransitionReceipt(actor: Guid.NewGuid()))))
            .ExecuteAsync(Transition(), Actor);
        Assert.AreEqual(
            CaseLifecycleFailureKind.ResultUnknown,
            wrongActor.Failure?.Kind);
        Assert.IsTrue(wrongActor.MustRetrySameOperation);
    }

    [TestMethod]
    public async Task Close_and_reopen_preserve_exact_Action_semantics()
    {
        var closeHandler = new Handler(_ => Reply(HttpStatusCode.OK, CloseReceipt()));
        var close = await CloseCommand(closeHandler).ExecuteAsync(Close(), Actor);
        Assert.IsTrue(close.IsSuccess);
        Assert.AreEqual(LearningCaseState.Closed, close.Receipt!.CaseState);
        Assert.AreEqual(10L, close.Receipt.CancelledActionVersion);

        var reopenHandler = new Handler(_ => Reply(HttpStatusCode.OK, ReopenReceipt()));
        var reopen = await ReopenCommand(reopenHandler).ExecuteAsync(Reopen(), Actor);
        Assert.IsTrue(reopen.IsSuccess);
        Assert.AreEqual(LearningCaseState.Intervening, reopen.Receipt!.CaseState);
        Assert.AreEqual("重新检查陌生材料", reopen.Receipt.NewActionText);
        Assert.AreEqual(1L, reopen.Receipt.NewActionVersion);

        using var body = JsonDocument.Parse(reopenHandler.Body!);
        Assert.AreEqual(
            "重新检查陌生材料",
            body.RootElement.GetProperty("p_new_primary_action_text").GetString());
    }

    [TestMethod]
    public async Task Deterministic_lifecycle_rejection_is_not_same_operation_retry()
    {
        var invalid = await CloseCommand(
            new Handler(_ => Reply(
                HttpStatusCode.BadRequest,
                "{"message":"XQ_CASE_NOT_STABLE"}")))
            .ExecuteAsync(Close(), Actor);
        Assert.AreEqual(
            CaseLifecycleFailureKind.InvalidTransition,
            invalid.Failure?.Kind);
        Assert.IsFalse(invalid.MustRetrySameOperation);

        var conflict = await TransitionCommand(
            new Handler(_ => Reply(
                HttpStatusCode.BadRequest,
                "{"message":"XQ_CASE_VERSION_CONFLICT"}")))
            .ExecuteAsync(Transition(), Actor);
        Assert.AreEqual(
            CaseLifecycleFailureKind.VersionConflict,
            conflict.Failure?.Kind);
        Assert.IsFalse(conflict.MustRetrySameOperation);
    }

    [TestMethod]
    public async Task Ambiguous_http_or_receipt_keeps_same_operation_id()
    {
        var unavailable = await TransitionCommand(
            new Handler(_ => Reply(HttpStatusCode.ServiceUnavailable, "{}")))
            .ExecuteAsync(Transition(), Actor);
        Assert.IsTrue(unavailable.MustRetrySameOperation);

        var wrongVersionJson = TransitionReceipt().Replace(
            ""case_version":5",
            ""case_version":99",
            StringComparison.Ordinal);
        var invalidReceipt = await TransitionCommand(
            new Handler(_ => Reply(HttpStatusCode.OK, wrongVersionJson)))
            .ExecuteAsync(Transition(), Actor);
        Assert.AreEqual(
            CaseLifecycleFailureKind.ResultUnknown,
            invalidReceipt.Failure?.Kind);
        Assert.IsTrue(invalidReceipt.MustRetrySameOperation);
    }

    [TestMethod]
    public async Task Invalid_intent_never_reaches_provider()
    {
        var handler = new Handler(
            _ => throw new AssertFailedException("Invalid intent reached network."));

        var invalidTarget = await TransitionCommand(handler).ExecuteAsync(
            Transition() with { TargetState = LearningCaseState.Closed },
            Actor);
        Assert.AreEqual(
            CaseLifecycleFailureKind.Validation,
            invalidTarget.Failure?.Kind);

        var invalidReopen = await ReopenCommand(handler).ExecuteAsync(
            Reopen() with { NewPrimaryActionText = " " },
            Actor);
        Assert.AreEqual(
            CaseLifecycleFailureKind.Validation,
            invalidReopen.Failure?.Kind);
    }

    private static TransitionLearningCaseStateRequest Transition() =>
        new(Operation, Org, Student, Profile, Assignment, Case, 4, LearningCaseState.Stable);

    private static CloseLearningCaseRequest Close() =>
        new(Operation, Org, Student, Profile, Assignment, Case, Action, 5, 9);

    private static ReopenLearningCaseRequest Reopen() =>
        new(
            Operation,
            Org,
            Student,
            Profile,
            Assignment,
            Case,
            6,
            " 重新检查陌生材料 ",
            new DateOnly(2026, 9, 29));

    private static PostgrestTransitionLearningCaseStateCommand TransitionCommand(Handler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.test/"),
            "test-key",
            _ => ValueTask.FromResult<string?>("test-token"));

    private static PostgrestCloseLearningCaseCommand CloseCommand(Handler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.test/"),
            "test-key",
            _ => ValueTask.FromResult<string?>("test-token"));

    private static PostgrestReopenLearningCaseCommand ReopenCommand(Handler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.test/"),
            "test-key",
            _ => ValueTask.FromResult<string?>("test-token"));

    private static string TransitionReceipt(Guid? actor = null) => JsonSerializer.Serialize(new
    {
        command = "transition_learning_case_state_v1",
        operation_id = Operation,
        organization_id = Org,
        student_id = Student,
        subject_profile_id = Profile,
        owner_assignment_id = Assignment,
        responsible_teacher_app_user_id = actor ?? Actor,
        case_id = Case,
        previous_case_state = "pending_verification",
        case_state = "stable",
        case_version = 5,
        primary_action_id = Action,
        case_event_id = Guid.NewGuid(),
        server_committed_at = "2026-09-20T01:00:00Z",
    });

    private static string CloseReceipt() => JsonSerializer.Serialize(new
    {
        command = "close_learning_case_v1",
        operation_id = Operation,
        organization_id = Org,
        student_id = Student,
        subject_profile_id = Profile,
        owner_assignment_id = Assignment,
        responsible_teacher_app_user_id = Actor,
        case_id = Case,
        previous_case_state = "stable",
        case_state = "closed",
        case_version = 6,
        cancelled_primary_action_id = Action,
        cancelled_action_version = 10,
        case_event_id = Guid.NewGuid(),
        server_committed_at = "2026-09-20T01:00:00Z",
    });

    private static string ReopenReceipt() => JsonSerializer.Serialize(new
    {
        command = "reopen_learning_case_v1",
        operation_id = Operation,
        organization_id = Org,
        student_id = Student,
        subject_profile_id = Profile,
        owner_assignment_id = Assignment,
        responsible_teacher_app_user_id = Actor,
        case_id = Case,
        previous_case_state = "closed",
        case_state = "intervening",
        case_version = 7,
        new_primary_action_id = Guid.NewGuid(),
        new_action_version = 1,
        new_action_text = "重新检查陌生材料",
        new_action_due_on = "2026-09-29",
        case_event_id = Guid.NewGuid(),
        server_committed_at = "2026-09-20T01:00:00Z",
    });

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class Handler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
