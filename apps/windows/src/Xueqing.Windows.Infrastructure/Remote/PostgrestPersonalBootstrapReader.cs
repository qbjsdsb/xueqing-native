using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

public sealed class PostgrestPersonalBootstrapReader : IPersonalBootstrapReader
{
    private const string RpcPath = "rest/v1/rpc/get_personal_bootstrap_v1";

    private readonly HttpClient _httpClient;
    private readonly Uri _rpcUri;
    private readonly string _apiKey;
    private readonly Func<CancellationToken, ValueTask<string?>> _accessTokenProvider;

    public PostgrestPersonalBootstrapReader(
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

    public async Task<PersonalBootstrapReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return PersonalBootstrapReadResult.Failed(
                PersonalBootstrapFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _rpcUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("apikey", _apiKey);
        request.Content = JsonContent.Create(new { });

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
            return PersonalBootstrapReadResult.Failed(
                PersonalBootstrapFailureKind.Transient,
                "XQ_NETWORK_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return PersonalBootstrapReadResult.Failed(
                PersonalBootstrapFailureKind.Transient,
                "XQ_NETWORK_UNAVAILABLE");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return PersonalBootstrapReadResult.Success(PersonalBootstrapJsonParser.Parse(body));
                }
                catch (JsonException)
                {
                    return PersonalBootstrapReadResult.Failed(
                        PersonalBootstrapFailureKind.InvalidResponse,
                        "XQ_BOOTSTRAP_JSON_INVALID");
                }
                catch (InvalidDataException)
                {
                    return PersonalBootstrapReadResult.Failed(
                        PersonalBootstrapFailureKind.InvalidResponse,
                        "XQ_BOOTSTRAP_CONTRACT_INVALID");
                }
            }

            return MapFailure(response.StatusCode, body);
        }
    }

    private static PersonalBootstrapReadResult MapFailure(HttpStatusCode statusCode, string body)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return PersonalBootstrapReadResult.Failed(
                PersonalBootstrapFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        var message = TryReadProviderMessage(body);
        if (message is "XQ_AUTH_REQUIRED" or "XQ_ACTOR_NOT_FOUND")
        {
            return PersonalBootstrapReadResult.Failed(PersonalBootstrapFailureKind.AuthenticationRequired, message);
        }
        if (message == "XQ_ACTOR_DISABLED")
        {
            return PersonalBootstrapReadResult.Failed(PersonalBootstrapFailureKind.AccessDenied, message);
        }
        if ((int)statusCode == 429 || (int)statusCode >= 500)
        {
            return PersonalBootstrapReadResult.Failed(
                PersonalBootstrapFailureKind.Transient,
                message ?? $"HTTP_{(int)statusCode}");
        }

        return PersonalBootstrapReadResult.Failed(
            PersonalBootstrapFailureKind.InvalidResponse,
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
