using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class CreateLearningCaseCommandTests
{
    private static readonly Guid ActorId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OperationId = Guid.Parse("76000000-0000-0000-0000-000000000001");
    private static readonly Guid OrganizationId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid StudentId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid SubjectProfileId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid AssignmentId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid ObservationId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid CaseId = Guid.Parse("61000000-0000-0000-0000-000000000001");
    private static readonly Guid ActionId = Guid.Parse("62000000-0000-0000-0000-000000000001");
    private static readonly Guid EventId = Guid.Parse("63000000-0000-0000-0000-000000000001");

    [TestMethod]
    public async Task Command_sends_only_domain_intent_and_accepts_matching_receipt()
    {
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(ValidReceipt()),
            });
        var command = CreateCommand(handler);
        var request = Request();

        var result = await command.ExecuteAsync(request, ActorId);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(OperationId, result.Receipt.OperationId);
        Assert.AreEqual(CaseId, result.Receipt.CaseId);
        Assert.AreEqual(ActionId, result.Receipt.PrimaryActionId);
        Assert.AreEqual(ActorId, result.Receipt.ResponsibleTeacherAppUserId);
        Assert.AreEqual(ObservationId, result.Receipt.SourceObservationId);
        Assert.AreEqual("Bearer access-token", handler.LastAuthorization);
        Assert.AreEqual("publishable-key", handler.LastApiKey);

        using var body = JsonDocument.Parse(handler.LastBody ?? throw new AssertFailedException("Missing command body."));
        var root = body.RootElement;
        Assert.AreEqual(OperationId.ToString("D"), root.GetProperty("p_operation_id").GetString());
        Assert.AreEqual(ObservationId.ToString("D"), root.GetProperty("p_source_observation_id").GetString());
        Assert.IsFalse(root.TryGetProperty("actor_app_user_id", out _));
        Assert.IsFalse(root.TryGetProperty("responsible_teacher_app_user_id", out _));
        Assert.IsFalse(root.TryGetProperty("p_actor_app_user_id", out _));
    }

    [TestMethod]
    public async Task Command_fails_closed_when_receipt_actor_scope_operation_or_source_does_not_match()
    {
        var cases = new[]
        {
            ValidReceipt(operationId: Guid.Parse("76000000-0000-0000-0000-000000000009")),
            ValidReceipt(actorId: Guid.Parse("10000000-0000-0000-0000-000000000009")),
            ValidReceipt(studentId: Guid.Parse("30000000-0000-0000-0000-000000000009")),
            ValidReceipt(sourceObservationId: Guid.Parse("60000000-0000-0000-0000-000000000009")),
        };

        foreach (var receipt in cases)
        {
            var result = await CreateCommand(new RecordingHandler(
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = Json(receipt),
                }))
                .ExecuteAsync(Request(), ActorId);

            Assert.AreEqual(CreateLearningCaseFailureKind.ResultUnknown, result.Failure?.Kind);
            Assert.IsTrue(result.MustRetrySameOperation);
            Assert.AreEqual("XQ_RESULT_UNKNOWN_RECEIPT_CONTRACT", result.Failure?.Code);
            Assert.IsNull(result.Receipt);
        }
    }

    [TestMethod]
    public async Task Command_rejects_non_new_or_non_v1_receipt()
    {
        foreach (var receipt in new[]
        {
            ValidReceipt(caseState: "confirmed"),
            ValidReceipt(caseVersion: 2),
        })
        {
            var result = await CreateCommand(new RecordingHandler(
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = Json(receipt),
                }))
                .ExecuteAsync(Request(), ActorId);

            Assert.AreEqual(CreateLearningCaseFailureKind.ResultUnknown, result.Failure?.Kind);
            Assert.IsTrue(result.MustRetrySameOperation);
        }
    }

    [TestMethod]
    public async Task Command_maps_stable_domain_failures_without_provider_types()
    {
        var authority = await CommandWithFailure("XQ_TEACHER_ASSIGNMENT_REQUIRED");
        var validation = await CommandWithFailure("XQ_INVALID_CASE_TITLE");
        var conflict = await CommandWithFailure("XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD");
        var auth = await CommandWithFailure("XQ_AUTH_REQUIRED", HttpStatusCode.Unauthorized);
        var transient = await CommandWithFailure(null, HttpStatusCode.ServiceUnavailable);

        Assert.AreEqual(CreateLearningCaseFailureKind.AuthorityChanged, authority.Failure?.Kind);
        Assert.AreEqual(CreateLearningCaseFailureKind.Validation, validation.Failure?.Kind);
        Assert.AreEqual(CreateLearningCaseFailureKind.OperationConflict, conflict.Failure?.Kind);
        Assert.AreEqual(CreateLearningCaseFailureKind.AuthenticationRequired, auth.Failure?.Kind);
        Assert.AreEqual(CreateLearningCaseFailureKind.ResultUnknown, transient.Failure?.Kind);
        Assert.IsTrue(transient.MustRetrySameOperation);
    }

    [TestMethod]
    public async Task Command_validates_intent_before_network()
    {
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(ValidReceipt()),
            });
        var command = CreateCommand(handler);

        var missingOperation = await command.ExecuteAsync(
            Request() with { OperationId = Guid.Empty },
            ActorId);
        var blankTitle = await command.ExecuteAsync(
            Request() with { Title = "   " },
            ActorId);
        var longAction = await command.ExecuteAsync(
            Request() with { PrimaryActionText = new string('a', 1001) },
            ActorId);
        var invalidSource = await command.ExecuteAsync(
            Request() with { SourceObservationId = Guid.Empty },
            ActorId);

        Assert.AreEqual(CreateLearningCaseFailureKind.Validation, missingOperation.Failure?.Kind);
        Assert.AreEqual(CreateLearningCaseFailureKind.Validation, blankTitle.Failure?.Kind);
        Assert.AreEqual(CreateLearningCaseFailureKind.Validation, longAction.Failure?.Kind);
        Assert.AreEqual(CreateLearningCaseFailureKind.Validation, invalidSource.Failure?.Kind);
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task Command_does_not_send_without_live_access_token()
    {
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(ValidReceipt()),
            });
        var command = new PostgrestCreateLearningCaseCommand(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>(null));

        var result = await command.ExecuteAsync(Request(), ActorId);

        Assert.AreEqual(CreateLearningCaseFailureKind.AuthenticationRequired, result.Failure?.Kind);
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task Ambiguous_network_failure_requires_same_operation_retry()
    {
        var handler = new RecordingHandler(
            requestIndex =>
            {
                if (requestIndex == 1)
                {
                    throw new HttpRequestException("simulated response loss");
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = Json(ValidReceipt()),
                };
            });
        var command = CreateCommand(handler);
        var request = Request();

        var unknown = await command.ExecuteAsync(request, ActorId);
        var retry = await command.ExecuteAsync(request, ActorId);

        Assert.AreEqual(CreateLearningCaseFailureKind.ResultUnknown, unknown.Failure?.Kind);
        Assert.IsTrue(unknown.MustRetrySameOperation);
        Assert.IsTrue(retry.IsSuccess);
        Assert.AreEqual(2, handler.RequestCount);
        Assert.AreEqual(2, handler.Bodies.Count);

        foreach (var bodyText in handler.Bodies)
        {
            using var body = JsonDocument.Parse(bodyText);
            Assert.AreEqual(
                OperationId.ToString("D"),
                body.RootElement.GetProperty("p_operation_id").GetString());
        }
    }

    [TestMethod]
    public void Command_rejects_non_loopback_plain_http()
    {
        try
        {
            _ = new PostgrestCreateLearningCaseCommand(
                new HttpClient(new RecordingHandler(
                    _ => new HttpResponseMessage(HttpStatusCode.OK))),
                new Uri("http://example.test/"),
                "publishable-key",
                _ => ValueTask.FromResult<string?>("token"));
            Assert.Fail("CreateLearningCase must reject non-loopback plain HTTP.");
        }
        catch (ArgumentException)
        {
        }
    }

    private static async Task<CreateLearningCaseResult> CommandWithFailure(
        string? message,
        HttpStatusCode statusCode = HttpStatusCode.BadRequest)
    {
        var body = message is null
            ? "{}"
            : JsonSerializer.Serialize(new { code = "P0001", message });
        return await CreateCommand(new RecordingHandler(
            _ => new HttpResponseMessage(statusCode)
            {
                Content = Json(body),
            }))
            .ExecuteAsync(Request(), ActorId);
    }

    private static PostgrestCreateLearningCaseCommand CreateCommand(RecordingHandler handler) =>
        new(
            new HttpClient(handler),
            new Uri("https://example.supabase.co/"),
            "publishable-key",
            _ => ValueTask.FromResult<string?>("access-token"));

    private static CreateLearningCaseRequest Request() =>
        new(
            OperationId,
            OrganizationId,
            StudentId,
            SubjectProfileId,
            AssignmentId,
            "概括题压缩仍不稳定",
            "下节课用陌生材料复核三道题",
            new DateOnly(2026, 9, 20),
            ObservationId);

    private static string ValidReceipt(
        Guid? operationId = null,
        Guid? actorId = null,
        Guid? studentId = null,
        Guid? sourceObservationId = null,
        string caseState = "new",
        long caseVersion = 1) =>
        JsonSerializer.Serialize(new
        {
            command = "create_learning_case_v1",
            operation_id = operationId ?? OperationId,
            case_id = CaseId,
            case_state = caseState,
            case_version = caseVersion,
            primary_action_id = ActionId,
            case_event_id = EventId,
            responsible_teacher_app_user_id = actorId ?? ActorId,
            owner_assignment_id = AssignmentId,
            organization_id = OrganizationId,
            student_id = studentId ?? StudentId,
            subject_profile_id = SubjectProfileId,
            subject_key = "chinese",
            source_observation_id = sourceObservationId ?? ObservationId,
            server_committed_at = DateTimeOffset.Parse("2026-09-19T12:40:00Z"),
        });

    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    private sealed class RecordingHandler(
        Func<int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastApiKey { get; private set; }
        public string? LastBody { get; private set; }
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastApiKey = request.Headers.TryGetValues("apikey", out var values)
                ? values.SingleOrDefault()
                : null;
            LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Bodies.Add(LastBody);
            return responder(RequestCount);
        }
    }
}
