using System.Net;
using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class LearningReadProviderSupport
{
    public static Uri BuildRpcUri(Uri projectUri, string rpcPath)
    {
        if (!projectUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Reference-provider URI must be absolute.", nameof(projectUri));
        }

        if (projectUri.Scheme != Uri.UriSchemeHttps &&
            !(projectUri.Scheme == Uri.UriSchemeHttp && projectUri.IsLoopback))
        {
            throw new ArgumentException(
                "Reference-provider URI must use HTTPS; loopback HTTP is allowed for development tests.",
                nameof(projectUri));
        }

        var normalized = projectUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? projectUri
            : new Uri(projectUri.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(normalized, rpcPath);
    }

    public static (LearningReadFailureKind Kind, string Code) MapFailure(
        HttpStatusCode statusCode,
        string body)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return (LearningReadFailureKind.AuthenticationRequired, "XQ_AUTH_REQUIRED");
        }

        var message = TryReadProviderMessage(body);
        if (message is "XQ_AUTH_REQUIRED" or "XQ_ACTOR_NOT_FOUND")
        {
            return (LearningReadFailureKind.AuthenticationRequired, message);
        }

        if (message is "XQ_ACTOR_DISABLED" or
            "XQ_TEACHING_CONTEXT_REQUIRED" or
            "XQ_TEACHING_CONTEXT_UNAVAILABLE")
        {
            return (LearningReadFailureKind.AccessDenied, message);
        }

        if (message is "XQ_CASE_PRIMARY_ACTION_INVARIANT" or
            "XQ_INVALID_ORGANIZATION_TIME_ZONE" or
            "XQ_BUSINESS_DATE_INPUT_REQUIRED")
        {
            return (LearningReadFailureKind.ServerInvariant, message);
        }

        if ((int)statusCode == 429 || (int)statusCode >= 500)
        {
            return (LearningReadFailureKind.Transient, message ?? $"HTTP_{(int)statusCode}");
        }

        return (LearningReadFailureKind.InvalidResponse, message ?? $"HTTP_{(int)statusCode}");
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
}
