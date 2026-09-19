using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

public sealed class PostgrestReschedulePrimaryActionCommand : IReschedulePrimaryActionCommand
{
    private readonly ActionProgressionTransport _transport;

    public PostgrestReschedulePrimaryActionCommand(
        HttpClient httpClient, Uri projectUri, string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider) =>
        _transport = new ActionProgressionTransport(httpClient, projectUri,
            "rest/v1/rpc/reschedule_primary_action", apiKey, accessTokenProvider);

    public async Task<ActionProgressionResult<ReschedulePrimaryActionReceipt>> ExecuteAsync(
        ReschedulePrimaryActionRequest request, Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = ActionProgressionTransport.ValidateScope(
            request.OperationId, request.OrganizationId, request.StudentId,
            request.SubjectProfileId, request.OwnerAssignmentId, request.CaseId,
            request.PrimaryActionId, request.ExpectedCaseVersion,
            request.ExpectedActionVersion, expectedActorAppUserId);
        if (validation is not null)
        {
            return ActionProgressionResult<ReschedulePrimaryActionReceipt>.Failed(
                validation.Kind, validation.Code);
        }
        var sent = await _transport.SendAsync(new
        {
            p_operation_id = request.OperationId,
            p_organization_id = request.OrganizationId,
            p_student_id = request.StudentId,
            p_subject_profile_id = request.SubjectProfileId,
            p_owner_assignment_id = request.OwnerAssignmentId,
            p_case_id = request.CaseId,
            p_primary_action_id = request.PrimaryActionId,
            p_expected_case_version = request.ExpectedCaseVersion,
            p_expected_action_version = request.ExpectedActionVersion,
            p_new_due_on = request.NewDueOn,
        }, cancellationToken).ConfigureAwait(false);
        if (sent.Failure is not null)
        {
            return ActionProgressionResult<ReschedulePrimaryActionReceipt>.Failed(
                sent.Failure.Kind, sent.Failure.Code);
        }
        try
        {
            return ActionProgressionResult<ReschedulePrimaryActionReceipt>.Success(
                ActionProgressionReceiptJsonParser.ParseReschedule(
                    sent.Body!, request, expectedActorAppUserId));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return ActionProgressionResult<ReschedulePrimaryActionReceipt>.Failed(
                ActionProgressionFailureKind.ResultUnknown, "XQ_RESULT_UNKNOWN_RECEIPT");
        }
    }
}

public sealed class PostgrestRecordVerificationAndNextActionCommand : IRecordVerificationAndNextActionCommand
{
    private readonly ActionProgressionTransport _transport;

    public PostgrestRecordVerificationAndNextActionCommand(
        HttpClient httpClient, Uri projectUri, string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider) =>
        _transport = new ActionProgressionTransport(httpClient, projectUri,
            "rest/v1/rpc/record_verification_and_next_action", apiKey, accessTokenProvider);

    public async Task<ActionProgressionResult<RecordVerificationAndNextActionReceipt>> ExecuteAsync(
        RecordVerificationAndNextActionRequest request, Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = ActionProgressionTransport.ValidateScope(
            request.OperationId, request.OrganizationId, request.StudentId,
            request.SubjectProfileId, request.OwnerAssignmentId, request.CaseId,
            request.CurrentPrimaryActionId, request.ExpectedCaseVersion,
            request.ExpectedActionVersion, expectedActorAppUserId);
        if (validation is not null)
        {
            return ActionProgressionResult<RecordVerificationAndNextActionReceipt>.Failed(
                validation.Kind, validation.Code);
        }
        if (!Enum.IsDefined(request.Outcome) ||
            request.VerificationSummary is null ||
            request.VerificationSummary.Trim().Length is < 1 or > 2000 ||
            request.NextActionText is null ||
            request.NextActionText.Trim().Length is < 1 or > 1000)
        {
            return ActionProgressionResult<RecordVerificationAndNextActionReceipt>.Failed(
                ActionProgressionFailureKind.Validation, "XQ_INVALID_VERIFICATION_INTENT");
        }

        var sent = await _transport.SendAsync(new
        {
            p_operation_id = request.OperationId,
            p_organization_id = request.OrganizationId,
            p_student_id = request.StudentId,
            p_subject_profile_id = request.SubjectProfileId,
            p_owner_assignment_id = request.OwnerAssignmentId,
            p_case_id = request.CaseId,
            p_current_primary_action_id = request.CurrentPrimaryActionId,
            p_expected_case_version = request.ExpectedCaseVersion,
            p_expected_action_version = request.ExpectedActionVersion,
            p_verification_outcome = ActionProgressionReceiptJsonParser.ToWireOutcome(request.Outcome),
            p_verification_summary = request.VerificationSummary.Trim(),
            p_next_action_text = request.NextActionText.Trim(),
            p_next_action_due_on = request.NextActionDueOn,
        }, cancellationToken).ConfigureAwait(false);
        if (sent.Failure is not null)
        {
            return ActionProgressionResult<RecordVerificationAndNextActionReceipt>.Failed(
                sent.Failure.Kind, sent.Failure.Code);
        }
        try
        {
            return ActionProgressionResult<RecordVerificationAndNextActionReceipt>.Success(
                ActionProgressionReceiptJsonParser.ParseVerification(
                    sent.Body!, request, expectedActorAppUserId));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return ActionProgressionResult<RecordVerificationAndNextActionReceipt>.Failed(
                ActionProgressionFailureKind.ResultUnknown, "XQ_RESULT_UNKNOWN_RECEIPT");
        }
    }
}

// Shared HTTP uncertainty classification only. Domain requests and results stay strongly typed.
internal sealed class ActionProgressionTransport
{
    private readonly HttpClient _httpClient;
    private readonly Uri _rpcUri;
    private readonly string _apiKey;
    private readonly Func<CancellationToken, ValueTask<string?>> _tokenProvider;

    public ActionProgressionTransport(
        HttpClient httpClient, Uri projectUri, string path, string apiKey,
        Func<CancellationToken, ValueTask<string?>> tokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _rpcUri = LearningReadProviderSupport.BuildRpcUri(
            projectUri ?? throw new ArgumentNullException(nameof(projectUri)), path);
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("API key is required.", nameof(apiKey))
            : apiKey;
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public static ActionProgressionFailure? ValidateScope(
        Guid operationId, Guid organizationId, Guid studentId, Guid subjectProfileId,
        Guid ownerAssignmentId, Guid caseId, Guid actionId,
        long caseVersion, long actionVersion, Guid actorId)
    {
        if (actorId == Guid.Empty)
            return new(ActionProgressionFailureKind.AuthenticationRequired, "XQ_CLIENT_ACTOR_REQUIRED");
        if (operationId == Guid.Empty)
            return new(ActionProgressionFailureKind.Validation, "XQ_OPERATION_ID_REQUIRED");
        if (organizationId == Guid.Empty || studentId == Guid.Empty ||
            subjectProfileId == Guid.Empty || ownerAssignmentId == Guid.Empty ||
            caseId == Guid.Empty || actionId == Guid.Empty)
            return new(ActionProgressionFailureKind.Validation, "XQ_TEACHING_CONTEXT_REQUIRED");
        if (caseVersion < 1 || caseVersion == long.MaxValue ||
            actionVersion < 1 || actionVersion == long.MaxValue)
            return new(ActionProgressionFailureKind.Validation, "XQ_EXPECTED_VERSION_REQUIRED");
        return null;
    }

    public async Task<(string? Body, ActionProgressionFailure? Failure)> SendAsync(
        object payload, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
            return (null, new(ActionProgressionFailureKind.AuthenticationRequired, "XQ_AUTH_REQUIRED"));

        using var request = new HttpRequestMessage(HttpMethod.Post, _rpcUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("apikey", _apiKey);
        request.Content = JsonContent.Create(payload);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, new(ActionProgressionFailureKind.ResultUnknown, "XQ_RESULT_UNKNOWN_TIMEOUT"));
        }
        catch (HttpRequestException)
        {
            return (null, new(ActionProgressionFailureKind.ResultUnknown, "XQ_RESULT_UNKNOWN_NETWORK"));
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (null, new(ActionProgressionFailureKind.ResultUnknown, "XQ_RESULT_UNKNOWN_RESPONSE_TIMEOUT"));
            }
            catch (HttpRequestException)
            {
                return (null, new(ActionProgressionFailureKind.ResultUnknown, "XQ_RESULT_UNKNOWN_RESPONSE_NETWORK"));
            }
            if (response.IsSuccessStatusCode) return (body, null);
            return (null, MapFailure(response.StatusCode, body));
        }
    }

    private static ActionProgressionFailure MapFailure(HttpStatusCode status, string body)
    {
        string? code = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
                code = message.GetString();
        }
        catch (JsonException) { }

        if (status == HttpStatusCode.Unauthorized ||
            code is "XQ_AUTH_REQUIRED" or "XQ_ACTOR_NOT_FOUND")
            return new(ActionProgressionFailureKind.AuthenticationRequired, code ?? "XQ_AUTH_REQUIRED");
        if (code is "XQ_CASE_VERSION_CONFLICT" or "XQ_ACTION_VERSION_CONFLICT")
            return new(ActionProgressionFailureKind.VersionConflict, code);
        if (code is "XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD")
            return new(ActionProgressionFailureKind.OperationConflict, code);
        if (code is "XQ_ACTOR_DISABLED" or "XQ_MEMBERSHIP_REQUIRED" or
            "XQ_MEMBERSHIP_DISABLED" or "XQ_TEACHING_CAPABILITY_REQUIRED" or
            "XQ_STUDENT_NOT_IN_ORG" or "XQ_SUBJECT_PROFILE_REQUIRED" or
            "XQ_TEACHER_ASSIGNMENT_REQUIRED" or "XQ_LEARNING_CASE_REQUIRED" or
            "XQ_PRIMARY_ACTION_REQUIRED" or "XQ_CASE_CLOSED")
            return new(ActionProgressionFailureKind.AuthorityChanged, code);
        if (code is "XQ_OPERATION_ID_REQUIRED" or "XQ_TEACHING_CONTEXT_REQUIRED" or
            "XQ_EXPECTED_CASE_VERSION_REQUIRED" or "XQ_EXPECTED_ACTION_VERSION_REQUIRED" or
            "XQ_INVALID_VERIFICATION_OUTCOME" or "XQ_INVALID_VERIFICATION_SUMMARY" or
            "XQ_INVALID_NEXT_ACTION" or "XQ_ACTION_DUE_UNCHANGED")
            return new(ActionProgressionFailureKind.Validation, code);
        // A response with no recognized deterministic server rejection cannot prove
        // that this operation did not commit. Reconcile with the same operation ID.
        return new(ActionProgressionFailureKind.ResultUnknown,
            code ?? $"XQ_RESULT_UNKNOWN_HTTP_{(int)status}");
    }
}
