using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class ActionProgressionCommandTests
{
    private static readonly Guid Actor = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Org = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Student = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid Profile = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid Assignment = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid Case = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid Action = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid Operation = Guid.Parse("80000000-0000-0000-0000-000000000001");

    [TestMethod]
    public async Task Reschedule_sends_expected_versions_and_accepts_only_bound_receipt()
    {
        var handler = new Handler(_ => Reply(HttpStatusCode.OK, RescheduleReceipt()));
        var request = Reschedule();
        var result = await RescheduleCommand(handler).ExecuteAsync(request, Actor);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(3L, result.Receipt!.CaseVersion);
        Assert.AreEqual(5L, result.Receipt.ActionVersion);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.AreEqual(Operation.ToString("D"), body.RootElement.GetProperty("p_operation_id").GetString());
        Assert.AreEqual(2L, body.RootElement.GetProperty("p_expected_case_version").GetInt64());
        Assert.AreEqual(4L, body.RootElement.GetProperty("p_expected_action_version").GetInt64());
        Assert.IsFalse(body.RootElement.TryGetProperty("actor_app_user_id", out _));

        var wrongActor = await RescheduleCommand(
            new Handler(_ => Reply(HttpStatusCode.OK, RescheduleReceipt(actor: Guid.NewGuid()))))
            .ExecuteAsync(request, Actor);
        Assert.AreEqual(ActionProgressionFailureKind.ResultUnknown, wrongActor.Failure?.Kind);
        Assert.IsTrue(wrongActor.MustRetrySameOperation);
        Assert.IsNull(wrongActor.Receipt);
    }

    [TestMethod]
    public async Task Verification_uses_one_rpc_and_requires_atomic_replacement_receipt()
    {
        var handler = new Handler(_ => Reply(HttpStatusCode.OK, VerificationReceipt()));
        var result = await VerificationCommand(handler).ExecuteAsync(Verification(), Actor);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreNotEqual(Action, result.Receipt!.NextPrimaryActionId);
        Assert.AreEqual(VerificationOutcome.PartiallyMet, result.Receipt.Outcome);
        Assert.IsTrue(handler.Uri!.AbsolutePath.EndsWith(
            "/record_verification_and_next_action", StringComparison.Ordinal));
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.AreEqual("partially_met", body.RootElement.GetProperty("p_verification_outcome").GetString());
        Assert.AreEqual("还有遗漏", body.RootElement.GetProperty("p_verification_summary").GetString());
        Assert.AreEqual("再练跨段概括", body.RootElement.GetProperty("p_next_action_text").GetString());
        Assert.AreEqual(Operation.ToString("D"), body.RootElement.GetProperty("p_operation_id").GetString());

        var falseReplacement = await VerificationCommand(
            new Handler(_ => Reply(HttpStatusCode.OK, VerificationReceipt(nextAction: Action))))
            .ExecuteAsync(Verification(), Actor);
        Assert.AreEqual(ActionProgressionFailureKind.ResultUnknown, falseReplacement.Failure?.Kind);
    }

    [TestMethod]
    public async Task Ambiguous_response_keeps_same_operation_while_deterministic_conflict_is_distinct()
    {
        var unknown = await RescheduleCommand(
            new Handler(_ => Reply(HttpStatusCode.ServiceUnavailable, "{}")))
            .ExecuteAsync(Reschedule(), Actor);
        Assert.IsTrue(unknown.MustRetrySameOperation);
        Assert.AreEqual(ActionProgressionFailureKind.ResultUnknown, unknown.Failure?.Kind);

        var conflict = await VerificationCommand(
            new Handler(_ => Reply(HttpStatusCode.BadRequest,
                "{\"message\":\"XQ_ACTION_VERSION_CONFLICT\"}")))
            .ExecuteAsync(Verification(), Actor);
        Assert.AreEqual(ActionProgressionFailureKind.VersionConflict, conflict.Failure?.Kind);
        Assert.IsFalse(conflict.MustRetrySameOperation);
    }

    [TestMethod]
    public async Task Invalid_intent_never_reaches_provider()
    {
        var handler = new Handler(_ => throw new AssertFailedException("Invalid intent reached network."));
        var invalid = await VerificationCommand(handler).ExecuteAsync(
            Verification() with { NextActionText = " " }, Actor);
        Assert.AreEqual(ActionProgressionFailureKind.Validation, invalid.Failure?.Kind);
        var missingVersion = await RescheduleCommand(handler).ExecuteAsync(
            Reschedule() with { ExpectedCaseVersion = 0 }, Actor);
        Assert.AreEqual(ActionProgressionFailureKind.Validation, missingVersion.Failure?.Kind);
    }

    private static ReschedulePrimaryActionRequest Reschedule() =>
        new(Operation, Org, Student, Profile, Assignment, Case, Action, 2, 4,
            new DateOnly(2026, 9, 24));

    private static RecordVerificationAndNextActionRequest Verification() =>
        new(Operation, Org, Student, Profile, Assignment, Case, Action, 2, 4,
            VerificationOutcome.PartiallyMet, " 还有遗漏 ", " 再练跨段概括 ",
            new DateOnly(2026, 9, 24));

    private static PostgrestReschedulePrimaryActionCommand RescheduleCommand(Handler handler) =>
        new(new HttpClient(handler), new Uri("https://example.test/"), "test-key",
            _ => ValueTask.FromResult<string?>("test-token"));

    private static PostgrestRecordVerificationAndNextActionCommand VerificationCommand(Handler handler) =>
        new(new HttpClient(handler), new Uri("https://example.test/"), "test-key",
            _ => ValueTask.FromResult<string?>("test-token"));

    private static string RescheduleReceipt(Guid? actor = null) => JsonSerializer.Serialize(new
    {
        command = "reschedule_primary_action_v1", operation_id = Operation,
        organization_id = Org, student_id = Student, subject_profile_id = Profile,
        owner_assignment_id = Assignment, responsible_teacher_app_user_id = actor ?? Actor,
        case_id = Case, case_state = "intervening", case_version = 3,
        primary_action_id = Action, action_version = 5,
        previous_due_on = "2026-09-21", due_on = "2026-09-24",
        case_event_id = Guid.NewGuid(), server_committed_at = "2026-09-19T12:00:00Z",
    });

    private static string VerificationReceipt(Guid? nextAction = null) => JsonSerializer.Serialize(new
    {
        command = "record_verification_and_next_action_v1", operation_id = Operation,
        organization_id = Org, student_id = Student, subject_profile_id = Profile,
        owner_assignment_id = Assignment, responsible_teacher_app_user_id = Actor,
        case_id = Case, case_state = "intervening", case_version = 3,
        completed_primary_action_id = Action, completed_action_version = 5,
        verification_id = Guid.NewGuid(), verification_outcome = "partially_met",
        verification_summary = "还有遗漏", next_primary_action_id = nextAction ?? Guid.NewGuid(),
        next_action_version = 1, next_action_text = "再练跨段概括",
        next_action_due_on = "2026-09-24", case_event_id = Guid.NewGuid(),
        server_committed_at = "2026-09-19T12:00:00Z",
    });

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public Uri? Uri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
