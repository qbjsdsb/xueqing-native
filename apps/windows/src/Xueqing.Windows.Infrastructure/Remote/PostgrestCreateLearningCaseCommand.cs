using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

public sealed class PostgrestCreateLearningCaseCommand : ICreateLearningCaseCommand
{
    private const string RpcPath = "rest/v1/rpc/create_learning_case";

    private readonly HttpClient _httpClient;
    private readonly Uri _rpcUri;
    private readonly string _apiKey;
    private readonly Func<CancellationToken, ValueTask<string?>> _accessTokenProvider;

    public PostgrestCreateLearningCaseCommand(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _rpcUri = LearningReadProviderSupport.BuildRpcUri(
            projectUri ?? throw new ArgumentNullException(nameof(projectUri)),
            RpcPath);
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("Reference-provider API key is required.", nameof(apiKey))
            : apiKey;
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
    }

    public async Task<CreateLearningCaseResult> ExecuteAsync(
        CreateLearningCaseRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = Validate(request, expectedActorAppUserId);
        if (validation is not null)
        {
            return validation;
        }

        var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _rpcUri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpRequest.Headers.TryAddWithoutValidation("apikey", _apiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            p_operation_id = request.OperationId,
            p_organization_id = request.OrganizationId,
            p_student_id = request.StudentId,
            p_subject_profile_id = request.SubjectProfileId,
            p_owner_assignment_id = request.OwnerAssignmentId,
            p_title = request.Title,
            p_primary_action_text = request.PrimaryActionText,
            p_primary_action_due_on = request.PrimaryActionDueOn,
            p_source_observation_id = request.SourceObservationId,
        });

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.ResultUnknown,
                "XQ_RESULT_UNKNOWN_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.ResultUnknown,
                "XQ_RESULT_UNKNOWN_NETWORK");
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
                return CreateLearningCaseResult.Failed(
                    CreateLearningCaseFailureKind.ResultUnknown,
                    "XQ_RESULT_UNKNOWN_RESPONSE_TIMEOUT");
            }
            catch (HttpRequestException)
            {
                return CreateLearningCaseResult.Failed(
                    CreateLearningCaseFailureKind.ResultUnknown,
                    "XQ_RESULT_UNKNOWN_RESPONSE_NETWORK");
            }

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return CreateLearningCaseResult.Success(
                        CreateLearningCaseReceiptJsonParser.Parse(
                            body,
                            request,
                            expectedActorAppUserId));
                }
                catch (JsonException)
                {
                    return CreateLearningCaseResult.Failed(
                        CreateLearningCaseFailureKind.ResultUnknown,
                        "XQ_RESULT_UNKNOWN_RECEIPT_JSON");
                }
                catch (InvalidDataException)
                {
                    return CreateLearningCaseResult.Failed(
                        CreateLearningCaseFailureKind.ResultUnknown,
                        "XQ_RESULT_UNKNOWN_RECEIPT_CONTRACT");
                }
            }

            var failure = CreateLearningCaseProviderSupport.MapFailure(
                response.StatusCode,
                body);
            return CreateLearningCaseResult.Failed(failure.Kind, failure.Code);
        }
    }

    private static CreateLearningCaseResult? Validate(
        CreateLearningCaseRequest request,
        Guid expectedActorAppUserId)
    {
        if (expectedActorAppUserId == Guid.Empty)
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.AuthenticationRequired,
                "XQ_CLIENT_ACTOR_REQUIRED");
        }

        if (request.OperationId == Guid.Empty)
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.Validation,
                "XQ_OPERATION_ID_REQUIRED");
        }

        if (request.OrganizationId == Guid.Empty ||
            request.StudentId == Guid.Empty ||
            request.SubjectProfileId == Guid.Empty ||
            request.OwnerAssignmentId == Guid.Empty)
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.Validation,
                "XQ_TEACHING_CONTEXT_REQUIRED");
        }

        if (request.SourceObservationId == Guid.Empty)
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.Validation,
                "XQ_SOURCE_OBSERVATION_INVALID");
        }

        var titleLength = request.Title?.Trim().Length ?? 0;
        if (titleLength is < 1 or > 500)
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.Validation,
                "XQ_INVALID_CASE_TITLE");
        }

        var actionLength = request.PrimaryActionText?.Trim().Length ?? 0;
        if (actionLength is < 1 or > 1000)
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.Validation,
                "XQ_INVALID_PRIMARY_ACTION");
        }

        return null;
    }
}
