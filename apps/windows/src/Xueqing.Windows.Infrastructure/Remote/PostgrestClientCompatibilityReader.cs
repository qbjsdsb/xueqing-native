using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

public sealed class PostgrestClientCompatibilityReader : IClientCompatibilityReader
{
    private const string RpcPath = "rest/v1/rpc/get_client_compatibility_v1";

    private readonly HttpClient _httpClient;
    private readonly Uri _rpcUri;
    private readonly string _apiKey;
    private readonly Func<CancellationToken, ValueTask<string?>> _accessTokenProvider;

    public PostgrestClientCompatibilityReader(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _rpcUri = BuildRpcUri(projectUri ?? throw new ArgumentNullException(nameof(projectUri)));
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("Provider API key is required.", nameof(apiKey))
            : apiKey;
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
    }

    public async Task<ClientCompatibilityReadResult> ReadAsync(
        ClientCompatibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return ClientCompatibilityReadResult.Failed(
                ClientCompatibilityFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, _rpcUri);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.TryAddWithoutValidation("apikey", _apiKey);
        message.Content = JsonContent.Create(new
        {
            p_platform = request.Platform,
            p_app_version = request.AppVersion,
            p_client_contract_version = request.ContractVersion,
        });

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ClientCompatibilityReadResult.Failed(
                ClientCompatibilityFailureKind.Transient,
                "XQ_NETWORK_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return ClientCompatibilityReadResult.Failed(
                ClientCompatibilityFailureKind.Transient,
                "XQ_NETWORK_UNAVAILABLE");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return ClientCompatibilityReadResult.Success(
                        ClientCompatibilityJsonParser.Parse(body, request));
                }
                catch (JsonException)
                {
                    return ClientCompatibilityReadResult.Failed(
                        ClientCompatibilityFailureKind.InvalidResponse,
                        "XQ_COMPATIBILITY_JSON_INVALID");
                }
                catch (InvalidDataException)
                {
                    return ClientCompatibilityReadResult.Failed(
                        ClientCompatibilityFailureKind.InvalidResponse,
                        "XQ_COMPATIBILITY_CONTRACT_INVALID");
                }
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return ClientCompatibilityReadResult.Failed(
                    ClientCompatibilityFailureKind.AuthenticationRequired,
                    "XQ_AUTH_REQUIRED");
            }

            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
            {
                return ClientCompatibilityReadResult.Failed(
                    ClientCompatibilityFailureKind.Transient,
                    TryReadProviderMessage(body) ?? $"HTTP_{(int)response.StatusCode}");
            }

            var error = TryReadProviderMessage(body);
            if (error == "XQ_COMPATIBILITY_POLICY_UNAVAILABLE")
            {
                return ClientCompatibilityReadResult.Failed(
                    ClientCompatibilityFailureKind.PolicyUnavailable,
                    error);
            }
            if (error == "XQ_AUTH_REQUIRED")
            {
                return ClientCompatibilityReadResult.Failed(
                    ClientCompatibilityFailureKind.AuthenticationRequired,
                    error);
            }

            return ClientCompatibilityReadResult.Failed(
                ClientCompatibilityFailureKind.InvalidResponse,
                error ?? $"HTTP_{(int)response.StatusCode}");
        }
    }

    private static string? TryReadProviderMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("message", out var message) &&
                   message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Uri BuildRpcUri(Uri projectUri)
    {
        if (!projectUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Provider URI must be absolute.", nameof(projectUri));
        }
        if (projectUri.Scheme != Uri.UriSchemeHttps &&
            !(projectUri.Scheme == Uri.UriSchemeHttp && projectUri.IsLoopback))
        {
            throw new ArgumentException(
                "Provider URI must use HTTPS; loopback HTTP is allowed for development tests.",
                nameof(projectUri));
        }

        var normalized = projectUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? projectUri
            : new Uri(projectUri.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(normalized, RpcPath);
    }
}
