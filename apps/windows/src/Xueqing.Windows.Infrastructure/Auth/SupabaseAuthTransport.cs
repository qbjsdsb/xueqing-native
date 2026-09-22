using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Xueqing.Windows.Infrastructure.Auth;

public enum ProviderAuthTransportFailureKind
{
    Rejected,
    RateLimited,
    Transient,
    ResultUnknown,
    InvalidResponse,
}

public sealed class ProviderAuthTransportException : InvalidOperationException
{
    public ProviderAuthTransportException(
        ProviderAuthTransportFailureKind kind,
        string code,
        string message)
        : base(message)
    {
        Kind = kind;
        Code = code;
    }

    public ProviderAuthTransportFailureKind Kind { get; }

    public string Code { get; }
}

public sealed class SupabaseAuthTransport : IProviderAuthTransport
{
    private const string PasswordTokenPath = "auth/v1/token?grant_type=password";
    private const string RefreshTokenPath = "auth/v1/token?grant_type=refresh_token";
    private const string LocalSignOutPath = "auth/v1/logout?scope=local";

    private readonly HttpClient _httpClient;
    private readonly Uri _projectOrigin;
    private readonly string _publishableKey;
    private readonly TimeProvider _timeProvider;

    public SupabaseAuthTransport(
        HttpClient httpClient,
        Uri projectOrigin,
        string publishableKey,
        TimeProvider? timeProvider = null,
        bool allowInsecureLoopbackForDevelopment = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _projectOrigin = ValidateOrigin(
            projectOrigin ?? throw new ArgumentNullException(nameof(projectOrigin)),
            allowInsecureLoopbackForDevelopment);
        _publishableKey = ValidateClientKey(publishableKey);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProviderAuthTokens> SignInWithPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        using var request = CreateJsonRequest(
            PasswordTokenPath,
            new
            {
                email,
                password,
            });

        var response = await SendAsync(
            request,
            resultUnknownOnTransportFailure: false,
            cancellationToken).ConfigureAwait(false);

        using (response)
        {
            var body = await ReadBodyAsync(
                response,
                resultUnknownOnTransportFailure: false,
                cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return ParseTokensOrThrow(
                    body,
                    ProviderAuthTransportFailureKind.InvalidResponse,
                    "XQ_AUTH_SIGNIN_RESPONSE_INVALID");
            }

            throw MapSignInFailure(response.StatusCode, body);
        }
    }

    public async Task<ProviderRefreshResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionToken(refreshToken, nameof(refreshToken));

        using var request = CreateJsonRequest(
            RefreshTokenPath,
            new
            {
                refresh_token = refreshToken,
            });

        HttpResponseMessage response;
        try
        {
            response = await SendAsync(
                request,
                resultUnknownOnTransportFailure: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderAuthTransportException error)
            when (error.Kind == ProviderAuthTransportFailureKind.ResultUnknown)
        {
            return new ProviderRefreshResult(ProviderRefreshDisposition.ResultUnknown);
        }

        using (response)
        {
            string body;
            try
            {
                body = await ReadBodyAsync(
                    response,
                    resultUnknownOnTransportFailure: true,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ProviderAuthTransportException error)
                when (error.Kind == ProviderAuthTransportFailureKind.ResultUnknown)
            {
                return new ProviderRefreshResult(ProviderRefreshDisposition.ResultUnknown);
            }

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return new ProviderRefreshResult(
                        ProviderRefreshDisposition.Success,
                        ParseTokensOrThrow(
                            body,
                            ProviderAuthTransportFailureKind.ResultUnknown,
                            "XQ_AUTH_REFRESH_RESPONSE_UNKNOWN"));
                }
                catch (ProviderAuthTransportException error)
                    when (error.Kind == ProviderAuthTransportFailureKind.ResultUnknown)
                {
                    return new ProviderRefreshResult(ProviderRefreshDisposition.ResultUnknown);
                }
            }

            var status = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.BadRequest
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden)
            {
                return new ProviderRefreshResult(ProviderRefreshDisposition.Rejected);
            }

            if (status is 408 or 425 or 429)
            {
                return new ProviderRefreshResult(ProviderRefreshDisposition.RetryableFailure);
            }

            if (status >= 500)
            {
                return new ProviderRefreshResult(ProviderRefreshDisposition.ResultUnknown);
            }

            if (status is >= 400 and < 500)
            {
                return new ProviderRefreshResult(ProviderRefreshDisposition.Rejected);
            }

            throw new ProviderAuthTransportException(
                ProviderAuthTransportFailureKind.InvalidResponse,
                TryReadErrorCode(body) ?? $"HTTP_{status}",
                "Supabase refresh returned an unrecognized HTTP response.");
        }
    }

    public async Task SignOutLocalAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionToken(accessToken, nameof(accessToken));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(LocalSignOutPath));
        AddClientHeaders(request);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { });

        var response = await SendAsync(
            request,
            resultUnknownOnTransportFailure: true,
            cancellationToken).ConfigureAwait(false);

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var body = await ReadBodyAsync(
                response,
                resultUnknownOnTransportFailure: true,
                cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            throw new ProviderAuthTransportException(
                status >= 500
                    ? ProviderAuthTransportFailureKind.ResultUnknown
                    : ProviderAuthTransportFailureKind.Rejected,
                TryReadErrorCode(body) ?? $"HTTP_{status}",
                "Supabase local sign-out was not confirmed.");
        }
    }

    private HttpRequestMessage CreateJsonRequest(string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(path))
        {
            Content = JsonContent.Create(body),
        };
        AddClientHeaders(request);
        return request;
    }

    private void AddClientHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("apikey", _publishableKey);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        bool resultUnknownOnTransportFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw TransportFailure(
                resultUnknownOnTransportFailure,
                "XQ_AUTH_NETWORK_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            throw TransportFailure(
                resultUnknownOnTransportFailure,
                "XQ_AUTH_NETWORK_UNAVAILABLE");
        }
    }

    private static async Task<string> ReadBodyAsync(
        HttpResponseMessage response,
        bool resultUnknownOnTransportFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw TransportFailure(
                resultUnknownOnTransportFailure,
                "XQ_AUTH_RESPONSE_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            throw TransportFailure(
                resultUnknownOnTransportFailure,
                "XQ_AUTH_RESPONSE_UNAVAILABLE");
        }
    }

    private ProviderAuthTokens ParseTokensOrThrow(
        string body,
        ProviderAuthTransportFailureKind failureKind,
        string failureCode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException();
            }

            var accessToken = GetRequiredString(root, "access_token");
            var refreshToken = GetRequiredString(root, "refresh_token");
            var expiresIn = GetRequiredPositiveInt64(root, "expires_in");
            var expiresAt = _timeProvider.GetUtcNow().AddSeconds(expiresIn);

            return new ProviderAuthTokens(
                accessToken,
                refreshToken,
                expiresAt);
        }
        catch (Exception error)
            when (error is JsonException
                or InvalidDataException
                or ArgumentOutOfRangeException)
        {
            throw new ProviderAuthTransportException(
                failureKind,
                failureCode,
                "Supabase Auth token response violated the accepted session contract.");
        }
    }

    private static ProviderAuthTransportException MapSignInFailure(
        HttpStatusCode statusCode,
        string body)
    {
        var status = (int)statusCode;
        var code = TryReadErrorCode(body) ?? $"HTTP_{status}";

        var kind = status switch
        {
            400 or 401 or 403 or 422 =>
                ProviderAuthTransportFailureKind.Rejected,
            429 =>
                ProviderAuthTransportFailureKind.RateLimited,
            408 or 425 =>
                ProviderAuthTransportFailureKind.Transient,
            >= 500 =>
                ProviderAuthTransportFailureKind.Transient,
            _ =>
                ProviderAuthTransportFailureKind.InvalidResponse,
        };

        return new ProviderAuthTransportException(
            kind,
            code,
            "Supabase password sign-in did not establish a session.");
    }

    private static ProviderAuthTransportException TransportFailure(
        bool resultUnknown,
        string code) =>
        new(
            resultUnknown
                ? ProviderAuthTransportFailureKind.ResultUnknown
                : ProviderAuthTransportFailureKind.Transient,
            code,
            resultUnknown
                ? "Supabase Auth request result is unknown."
                : "Supabase Auth request could not complete.");

    private Uri BuildUri(string path) =>
        new(_projectOrigin, path);

    private static Uri ValidateOrigin(
        Uri origin,
        bool allowInsecureLoopbackForDevelopment)
    {
        if (!origin.IsAbsoluteUri ||
            !string.IsNullOrEmpty(origin.UserInfo) ||
            origin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment))
        {
            throw new ArgumentException(
                "Supabase project origin must be an absolute origin without user info, path, query or fragment.",
                nameof(origin));
        }

        var secure = origin.Scheme == Uri.UriSchemeHttps;
        var developmentLoopback =
            allowInsecureLoopbackForDevelopment &&
            origin.Scheme == Uri.UriSchemeHttp &&
            origin.IsLoopback;

        if (!secure && !developmentLoopback)
        {
            throw new ArgumentException(
                "Supabase Auth requires HTTPS except explicit loopback development.",
                nameof(origin));
        }

        return new Uri(
            origin.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/",
            UriKind.Absolute);
    }

    private static string ValidateClientKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Supabase client key must not contain whitespace.",
                nameof(key));
        }

        var privilegedPrefix = string.Concat("sb_", "secret", "_");
        var privilegedRoleMarker = string.Concat("service", "_role");
        if (key.StartsWith(privilegedPrefix, StringComparison.OrdinalIgnoreCase) ||
            key.Contains(privilegedRoleMarker, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Privileged Supabase credentials must never be supplied to a native client.",
                nameof(key));
        }

        return key;
    }

    private static void ValidateSessionToken(string token, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token, parameterName);
        if (token.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Session token must not contain whitespace.",
                parameterName);
        }
    }

    private static string GetRequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"Missing/invalid Auth field: {name}");
        }

        return value.GetString()!;
    }

    private static long GetRequiredPositiveInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var number) ||
            number <= 0)
        {
            throw new InvalidDataException($"Missing/invalid Auth field: {name}");
        }

        return number;
    }

    private static string? TryReadErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in new[]
            {
                "error_code",
                "code",
                "error_description",
                "msg",
                "message",
                "error",
            })
            {
                if (document.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString()!.Trim();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
