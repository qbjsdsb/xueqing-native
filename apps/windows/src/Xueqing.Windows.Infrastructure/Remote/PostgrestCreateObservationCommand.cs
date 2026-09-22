using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

public sealed class PostgrestCreateObservationCommand : ICreateObservationCommand
{
    private const string RpcPath = "rest/v1/rpc/create_observation";
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>();

    private readonly HttpClient _httpClient;
    private readonly Uri _rpcUri;
    private readonly string _apiKey;
    private readonly Func<CancellationToken, ValueTask<string?>> _accessTokenProvider;

    public PostgrestCreateObservationCommand(
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
        _accessTokenProvider = accessTokenProvider
            ?? throw new ArgumentNullException(nameof(accessTokenProvider));
    }

    public async Task<CreateObservationResult> ExecuteAsync(
        CreateObservationRequest request,
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
            return CreateObservationResult.Failed(
                CreateObservationFailureKind.AuthenticationRequired,
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
            p_assignment_id = request.AssignmentId,
            p_raw_text = request.RawText,
            p_client_captured_at = request.ClientCapturedAt,
            p_client_capture_metadata = request.ClientCaptureMetadata ?? EmptyMetadata,
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
            return CreateObservationResult.Failed(
                CreateObservationFailureKind.ResultUnknown,
                "XQ_RESULT_UNKNOWN_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return CreateObservationResult.Failed(
                CreateObservationFailureKind.ResultUnknown,
                "XQ_RESULT_UNKNOWN_NETWORK");
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return CreateObservationResult.Failed(
                    CreateObservationFailureKind.ResultUnknown,
                    "XQ_RESULT_UNKNOWN_RESPONSE_TIMEOUT");
            }
            catch (HttpRequestException)
            {
                return CreateObservationResult.Failed(
                    CreateObservationFailureKind.ResultUnknown,
                    "XQ_RESULT_UNKNOWN_RESPONSE_NETWORK");
            }

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return CreateObservationResult.Success(
                        CreateObservationReceiptJsonParser.Parse(
                            body,
                            request,
                            expectedActorAppUserId));
                }
                catch (JsonException)
                {
                    return CreateObservationResult.Failed(
                        CreateObservationFailureKind.ResultUnknown,
                        "XQ_RESULT_UNKNOWN_RECEIPT_JSON");
                }
                catch (InvalidDataException)
                {
                    return CreateObservationResult.Failed(
                        CreateObservationFailureKind.ResultUnknown,
                        "XQ_RESULT_UNKNOWN_RECEIPT_CONTRACT");
                }
            }

            var failure = CreateObservationProviderSupport.MapFailure(
                response.StatusCode,
                body);
            return CreateObservationResult.Failed(failure.Kind, failure.Code);
        }
    }

    private static CreateObservationResult? Validate(
        CreateObservationRequest request,
        Guid expectedActorAppUserId)
    {
        if (expectedActorAppUserId == Guid.Empty)
        {
            return CreateObservationResult.Failed(
                CreateObservationFailureKind.AuthenticationRequired,
                "XQ_CLIENT_ACTOR_REQUIRED");
        }

        if (request.OperationId == Guid.Empty)
        {
            return CreateObservationResult.Failed(
                CreateObservationFailureKind.Validation,
                "XQ_OPERATION_ID_REQUIRED");
        }

        if (request.OrganizationId == Guid.Empty ||
            request.StudentId == Guid.Empty ||
            request.SubjectProfileId == Guid.Empty ||
            request.AssignmentId == Guid.Empty)
        {
            return CreateObservationResult.Failed(
                CreateObservationFailureKind.Validation,
                "XQ_TEACHING_CONTEXT_REQUIRED");
        }

        var textLength = request.RawText?.Trim().Length ?? 0;
        if (textLength is < 1 or > 10_000)
        {
            return CreateObservationResult.Failed(
                CreateObservationFailureKind.Validation,
                "XQ_INVALID_OBSERVATION_TEXT");
        }

        return null;
    }
}
