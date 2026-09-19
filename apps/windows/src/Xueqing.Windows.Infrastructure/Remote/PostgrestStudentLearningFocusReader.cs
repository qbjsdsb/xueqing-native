using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

public sealed class PostgrestStudentLearningFocusReader : IStudentLearningFocusReader
{
    private const string RpcPath = "rest/v1/rpc/get_student_learning_focus_v1";

    private readonly HttpClient _httpClient;
    private readonly Uri _rpcUri;
    private readonly string _apiKey;
    private readonly Func<CancellationToken, ValueTask<string?>> _accessTokenProvider;

    public PostgrestStudentLearningFocusReader(
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

    public async Task<StudentLearningFocusReadResult> ReadAsync(
        StudentLearningScope scope,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        if (scope.OrganizationId == Guid.Empty ||
            scope.StudentId == Guid.Empty ||
            scope.SubjectProfileId == Guid.Empty)
        {
            return StudentLearningFocusReadResult.Failed(
                LearningReadFailureKind.InvalidResponse,
                "XQ_CLIENT_SCOPE_INVALID");
        }

        if (expectedActorAppUserId == Guid.Empty)
        {
            return StudentLearningFocusReadResult.Failed(
                LearningReadFailureKind.AuthenticationRequired,
                "XQ_CLIENT_ACTOR_REQUIRED");
        }

        var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return StudentLearningFocusReadResult.Failed(
                LearningReadFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _rpcUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("apikey", _apiKey);
        request.Content = JsonContent.Create(new
        {
            p_organization_id = scope.OrganizationId,
            p_student_id = scope.StudentId,
            p_subject_profile_id = scope.SubjectProfileId,
        });

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
            return StudentLearningFocusReadResult.Failed(
                LearningReadFailureKind.Transient,
                "XQ_NETWORK_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return StudentLearningFocusReadResult.Failed(
                LearningReadFailureKind.Transient,
                "XQ_NETWORK_UNAVAILABLE");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return StudentLearningFocusReadResult.Success(
                        StudentLearningFocusJsonParser.Parse(body, scope, expectedActorAppUserId));
                }
                catch (JsonException)
                {
                    return StudentLearningFocusReadResult.Failed(
                        LearningReadFailureKind.InvalidResponse,
                        "XQ_PROJECTION_JSON_INVALID");
                }
                catch (InvalidDataException)
                {
                    return StudentLearningFocusReadResult.Failed(
                        LearningReadFailureKind.InvalidResponse,
                        "XQ_PROJECTION_CONTRACT_INVALID");
                }
            }

            var failure = LearningReadProviderSupport.MapFailure(response.StatusCode, body);
            return StudentLearningFocusReadResult.Failed(failure.Kind, failure.Code);
        }
    }
}
