using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

public sealed class PostgrestTransitionLearningCaseStateCommand :
    ITransitionLearningCaseStateCommand
{
    private readonly CaseLifecycleTransport _transport;

    public PostgrestTransitionLearningCaseStateCommand(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider) =>
        _transport = new CaseLifecycleTransport(
            httpClient,
            projectUri,
            "rest/v1/rpc/transition_learning_case_state",
            apiKey,
            accessTokenProvider);

    public async Task<CaseLifecycleResult<TransitionLearningCaseStateReceipt>> ExecuteAsync(
        TransitionLearningCaseStateRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = CaseLifecycleTransport.ValidateCommon(
            request.OperationId,
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.OwnerAssignmentId,
            request.CaseId,
            request.ExpectedCaseVersion,
            expectedActorAppUserId);
        if (validation is not null)
        {
            return CaseLifecycleResult<TransitionLearningCaseStateReceipt>.Failed(
                validation.Kind,
                validation.Code);
        }

        string targetState;
        try
        {
            targetState = CaseLifecycleReceiptJsonParser.ToWireTargetState(
                request.TargetState);
        }
        catch (ArgumentOutOfRangeException)
        {
            return CaseLifecycleResult<TransitionLearningCaseStateReceipt>.Failed(
                CaseLifecycleFailureKind.Validation,
                "XQ_INVALID_CASE_TARGET_STATE");
        }

        var sent = await _transport.SendAsync(
            new
            {
                p_operation_id = request.OperationId,
                p_organization_id = request.OrganizationId,
                p_student_id = request.StudentId,
                p_subject_profile_id = request.SubjectProfileId,
                p_owner_assignment_id = request.OwnerAssignmentId,
                p_case_id = request.CaseId,
                p_expected_case_version = request.ExpectedCaseVersion,
                p_target_state = targetState,
            },
            cancellationToken).ConfigureAwait(false);

        if (sent.Failure is not null)
        {
            return CaseLifecycleResult<TransitionLearningCaseStateReceipt>.Failed(
                sent.Failure.Kind,
                sent.Failure.Code);
        }

        try
        {
            return CaseLifecycleResult<TransitionLearningCaseStateReceipt>.Success(
                CaseLifecycleReceiptJsonParser.ParseTransition(
                    sent.Body!,
                    request,
                    expectedActorAppUserId));
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException)
        {
            return CaseLifecycleResult<TransitionLearningCaseStateReceipt>.Failed(
                CaseLifecycleFailureKind.ResultUnknown,
                "XQ_RESULT_UNKNOWN_RECEIPT");
        }
    }
}

public sealed class PostgrestCloseLearningCaseCommand : ICloseLearningCaseCommand
{
    private readonly CaseLifecycleTransport _transport;

    public PostgrestCloseLearningCaseCommand(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider) =>
        _transport = new CaseLifecycleTransport(
            httpClient,
            projectUri,
            "rest/v1/rpc/close_learning_case",
            apiKey,
            accessTokenProvider);

    public async Task<CaseLifecycleResult<CloseLearningCaseReceipt>> ExecuteAsync(
        CloseLearningCaseRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = CaseLifecycleTransport.ValidateCommon(
            request.OperationId,
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.OwnerAssignmentId,
            request.CaseId,
            request.ExpectedCaseVersion,
            expectedActorAppUserId);
        if (validation is not null)
        {
            return CaseLifecycleResult<CloseLearningCaseReceipt>.Failed(
                validation.Kind,
                validation.Code);
        }

        if (request.PrimaryActionId == Guid.Empty ||
            request.ExpectedActionVersion <= 0 ||
            request.ExpectedActionVersion == long.MaxValue)
        {
            return CaseLifecycleResult<CloseLearningCaseReceipt>.Failed(
                CaseLifecycleFailureKind.Validation,
                "XQ_EXPECTED_ACTION_VERSION_REQUIRED");
        }

        var sent = await _transport.SendAsync(
            new
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
            },
            cancellationToken).ConfigureAwait(false);

        if (sent.Failure is not null)
        {
            return CaseLifecycleResult<CloseLearningCaseReceipt>.Failed(
                sent.Failure.Kind,
                sent.Failure.Code);
        }

        try
        {
            return CaseLifecycleResult<CloseLearningCaseReceipt>.Success(
                CaseLifecycleReceiptJsonParser.ParseClose(
                    sent.Body!,
                    request,
                    expectedActorAppUserId));
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException)
        {
            return CaseLifecycleResult<CloseLearningCaseReceipt>.Failed(
                CaseLifecycleFailureKind.ResultUnknown,
                "XQ_RESULT_UNKNOWN_RECEIPT");
        }
    }
}

public sealed class PostgrestReopenLearningCaseCommand : IReopenLearningCaseCommand
{
    private readonly CaseLifecycleTransport _transport;

    public PostgrestReopenLearningCaseCommand(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider) =>
        _transport = new CaseLifecycleTransport(
            httpClient,
            projectUri,
            "rest/v1/rpc/reopen_learning_case",
            apiKey,
            accessTokenProvider);

    public async Task<CaseLifecycleResult<ReopenLearningCaseReceipt>> ExecuteAsync(
        ReopenLearningCaseRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = CaseLifecycleTransport.ValidateCommon(
            request.OperationId,
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.OwnerAssignmentId,
            request.CaseId,
            request.ExpectedCaseVersion,
            expectedActorAppUserId);
        if (validation is not null)
        {
            return CaseLifecycleResult<ReopenLearningCaseReceipt>.Failed(
                validation.Kind,
                validation.Code);
        }

        if (request.NewPrimaryActionText is null ||
            request.NewPrimaryActionText.Trim().Length is < 1 or > 1000)
        {
            return CaseLifecycleResult<ReopenLearningCaseReceipt>.Failed(
                CaseLifecycleFailureKind.Validation,
                "XQ_INVALID_PRIMARY_ACTION");
        }

        var sent = await _transport.SendAsync(
            new
            {
                p_operation_id = request.OperationId,
                p_organization_id = request.OrganizationId,
                p_student_id = request.StudentId,
                p_subject_profile_id = request.SubjectProfileId,
                p_owner_assignment_id = request.OwnerAssignmentId,
                p_case_id = request.CaseId,
                p_expected_case_version = request.ExpectedCaseVersion,
                p_new_primary_action_text = request.NewPrimaryActionText.Trim(),
                p_new_primary_action_due_on = request.NewPrimaryActionDueOn,
            },
            cancellationToken).ConfigureAwait(false);

        if (sent.Failure is not null)
        {
            return CaseLifecycleResult<ReopenLearningCaseReceipt>.Failed(
                sent.Failure.Kind,
                sent.Failure.Code);
        }

        try
        {
            return CaseLifecycleResult<ReopenLearningCaseReceipt>.Success(
                CaseLifecycleReceiptJsonParser.ParseReopen(
                    sent.Body!,
                    request,
                    expectedActorAppUserId));
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException)
        {
            return CaseLifecycleResult<ReopenLearningCaseReceipt>.Failed(
                CaseLifecycleFailureKind.ResultUnknown,
                "XQ_RESULT_UNKNOWN_RECEIPT");
        }
    }
}

internal sealed class CaseLifecycleTransport
{
    private readonly HttpClient _httpClient;
    private readonly Uri _rpcUri;
    private readonly string _apiKey;
    private readonly Func<CancellationToken, ValueTask<string?>> _tokenProvider;

    public CaseLifecycleTransport(
        HttpClient httpClient,
        Uri projectUri,
        string path,
        string apiKey,
        Func<CancellationToken, ValueTask<string?>> tokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _rpcUri = LearningReadProviderSupport.BuildRpcUri(
            projectUri ?? throw new ArgumentNullException(nameof(projectUri)),
            path);
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("API key is required.", nameof(apiKey))
            : apiKey;
        _tokenProvider = tokenProvider ??
            throw new ArgumentNullException(nameof(tokenProvider));
    }

    public static CaseLifecycleFailure? ValidateCommon(
        Guid operationId,
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid ownerAssignmentId,
        Guid caseId,
        long expectedCaseVersion,
        Guid actorId)
    {
        if (actorId == Guid.Empty)
        {
            return new(
                CaseLifecycleFailureKind.AuthenticationRequired,
                "XQ_CLIENT_ACTOR_REQUIRED");
        }

        if (operationId == Guid.Empty)
        {
            return new(
                CaseLifecycleFailureKind.Validation,
                "XQ_OPERATION_ID_REQUIRED");
        }

        if (organizationId == Guid.Empty ||
            studentId == Guid.Empty ||
            subjectProfileId == Guid.Empty ||
            ownerAssignmentId == Guid.Empty ||
            caseId == Guid.Empty)
        {
            return new(
                CaseLifecycleFailureKind.Validation,
                "XQ_TEACHING_CONTEXT_REQUIRED");
        }

        if (expectedCaseVersion <= 0 || expectedCaseVersion == long.MaxValue)
        {
            return new(
                CaseLifecycleFailureKind.Validation,
                "XQ_EXPECTED_CASE_VERSION_REQUIRED");
        }

        return null;
    }

    public async Task<(string? Body, CaseLifecycleFailure? Failure)> SendAsync(
        object payload,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return (
                null,
                new(
                    CaseLifecycleFailureKind.AuthenticationRequired,
                    "XQ_AUTH_REQUIRED"));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _rpcUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("apikey", _apiKey);
        request.Content = JsonContent.Create(payload);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (
                null,
                new(
                    CaseLifecycleFailureKind.ResultUnknown,
                    "XQ_RESULT_UNKNOWN_TIMEOUT"));
        }
        catch (HttpRequestException)
        {
            return (
                null,
                new(
                    CaseLifecycleFailureKind.ResultUnknown,
                    "XQ_RESULT_UNKNOWN_NETWORK"));
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (
                    null,
                    new(
                        CaseLifecycleFailureKind.ResultUnknown,
                        "XQ_RESULT_UNKNOWN_RESPONSE_TIMEOUT"));
            }
            catch (HttpRequestException)
            {
                return (
                    null,
                    new(
                        CaseLifecycleFailureKind.ResultUnknown,
                        "XQ_RESULT_UNKNOWN_RESPONSE_NETWORK"));
            }

            if (response.IsSuccessStatusCode)
            {
                return (body, null);
            }

            return (null, MapFailure(response.StatusCode, body));
        }
    }

    private static CaseLifecycleFailure MapFailure(
        HttpStatusCode status,
        string body)
    {
        string? code = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                code = message.GetString();
            }
        }
        catch (JsonException)
        {
        }

        if (status == HttpStatusCode.Unauthorized ||
            code is "XQ_AUTH_REQUIRED" or "XQ_ACTOR_NOT_FOUND")
        {
            return new(
                CaseLifecycleFailureKind.AuthenticationRequired,
                code ?? "XQ_AUTH_REQUIRED");
        }

        if (code is "XQ_CASE_VERSION_CONFLICT" or "XQ_ACTION_VERSION_CONFLICT")
        {
            return new(CaseLifecycleFailureKind.VersionConflict, code);
        }

        if (code is "XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD")
        {
            return new(CaseLifecycleFailureKind.OperationConflict, code);
        }

        if (code is "XQ_INVALID_CASE_TRANSITION" or
            "XQ_CASE_NOT_STABLE" or
            "XQ_CASE_NOT_CLOSED" or
            "XQ_CASE_CLOSED")
        {
            return new(CaseLifecycleFailureKind.InvalidTransition, code);
        }

        if (code is "XQ_CLOSED_CASE_HAS_PENDING_ACTION")
        {
            return new(CaseLifecycleFailureKind.ServerInvariant, code);
        }

        if (code is "XQ_ACTOR_DISABLED" or
            "XQ_MEMBERSHIP_REQUIRED" or
            "XQ_MEMBERSHIP_DISABLED" or
            "XQ_TEACHING_CAPABILITY_REQUIRED" or
            "XQ_STUDENT_NOT_IN_ORG" or
            "XQ_SUBJECT_PROFILE_REQUIRED" or
            "XQ_TEACHER_ASSIGNMENT_REQUIRED" or
            "XQ_LEARNING_CASE_REQUIRED" or
            "XQ_PRIMARY_ACTION_REQUIRED")
        {
            return new(CaseLifecycleFailureKind.AuthorityChanged, code);
        }

        if (code is "XQ_OPERATION_ID_REQUIRED" or
            "XQ_TEACHING_CONTEXT_REQUIRED" or
            "XQ_EXPECTED_CASE_VERSION_REQUIRED" or
            "XQ_EXPECTED_ACTION_VERSION_REQUIRED" or
            "XQ_INVALID_CASE_TARGET_STATE" or
            "XQ_INVALID_PRIMARY_ACTION")
        {
            return new(CaseLifecycleFailureKind.Validation, code);
        }

        return new(
            CaseLifecycleFailureKind.ResultUnknown,
            code ?? $"XQ_RESULT_UNKNOWN_HTTP_{(int)status}");
    }
}
