using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

public sealed class PostgrestStudentRecentObservationsReader : IStudentRecentObservationsReader
{
    private const string RpcPath = "rest/v1/rpc/get_student_recent_observations_v1";

    private readonly HttpClient _httpClient;
    private readonly Uri _rpcUri;
    private readonly string _apiKey;
    private readonly Func<CancellationToken, ValueTask<string?>> _accessTokenProvider;

    public PostgrestStudentRecentObservationsReader(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _rpcUri = BuildRpcUri(projectUri ?? throw new ArgumentNullException(nameof(projectUri)));
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("Reference-provider API key is required.", nameof(apiKey))
            : apiKey;
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
    }

    public async Task<StudentRecentObservationsReadResult> ReadAsync(
        StudentObservationScope scope,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        if (scope.OrganizationId == Guid.Empty || scope.StudentId == Guid.Empty || scope.SubjectProfileId == Guid.Empty)
        {
            return StudentRecentObservationsReadResult.Failed(
                StudentRecentObservationsFailureKind.InvalidResponse,
                "XQ_CLIENT_SCOPE_INVALID");
        }

        if (expectedActorAppUserId == Guid.Empty)
        {
            return StudentRecentObservationsReadResult.Failed(
                StudentRecentObservationsFailureKind.AuthenticationRequired,
                "XQ_CLIENT_ACTOR_REQUIRED");
        }

        var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return StudentRecentObservationsReadResult.Failed(
                StudentRecentObservationsFailureKind.AuthenticationRequired,
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
            return StudentRecentObservationsReadResult.Failed(
                StudentRecentObservationsFailureKind.Transient,
                "XQ_NETWORK_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return StudentRecentObservationsReadResult.Failed(
                StudentRecentObservationsFailureKind.Transient,
                "XQ_NETWORK_UNAVAILABLE");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return StudentRecentObservationsReadResult.Success(
                        StudentRecentObservationsJsonParser.Parse(body, scope, expectedActorAppUserId));
                }
                catch (JsonException)
                {
                    return StudentRecentObservationsReadResult.Failed(
                        StudentRecentObservationsFailureKind.InvalidResponse,
                        "XQ_PROJECTION_JSON_INVALID");
                }
                catch (InvalidDataException)
                {
                    return StudentRecentObservationsReadResult.Failed(
                        StudentRecentObservationsFailureKind.InvalidResponse,
                        "XQ_PROJECTION_CONTRACT_INVALID");
                }
            }

            return MapFailure(response.StatusCode, body);
        }
    }

    private static StudentRecentObservationsReadResult MapFailure(HttpStatusCode statusCode, string body)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return StudentRecentObservationsReadResult.Failed(
                StudentRecentObservationsFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        var message = TryReadProviderMessage(body);
        if (message is "XQ_AUTH_REQUIRED" or "XQ_ACTOR_NOT_FOUND")
        {
            return StudentRecentObservationsReadResult.Failed(
                StudentRecentObservationsFailureKind.AuthenticationRequired,
                message);
        }

        if (message is "XQ_ACTOR_DISABLED" or "XQ_TEACHING_CONTEXT_REQUIRED" or "XQ_TEACHING_CONTEXT_UNAVAILABLE")
        {
            return StudentRecentObservationsReadResult.Failed(
                StudentRecentObservationsFailureKind.AccessDenied,
                message);
        }

        if ((int)statusCode == 429 || (int)statusCode >= 500)
        {
            return StudentRecentObservationsReadResult.Failed(
                StudentRecentObservationsFailureKind.Transient,
                message ?? $"HTTP_{(int)statusCode}");
        }

        return StudentRecentObservationsReadResult.Failed(
            StudentRecentObservationsFailureKind.InvalidResponse,
            message ?? $"HTTP_{(int)statusCode}");
    }

    private static string? TryReadProviderMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static Uri BuildRpcUri(Uri projectUri)
    {
        if (!projectUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Reference-provider URI must be absolute.", nameof(projectUri));
        }

        if (projectUri.Scheme != Uri.UriSchemeHttps &&
            !(projectUri.Scheme == Uri.UriSchemeHttp && projectUri.IsLoopback))
        {
            throw new ArgumentException("Reference-provider URI must use HTTPS; loopback HTTP is allowed for development tests.", nameof(projectUri));
        }

        var normalized = projectUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? projectUri
            : new Uri(projectUri.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(normalized, RpcPath);
    }
}
