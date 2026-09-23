using System.Net;
using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class CreateObservationProviderSupport
{
    public static (CreateObservationFailureKind Kind, string Code) MapFailure(
        HttpStatusCode statusCode,
        string body)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return (CreateObservationFailureKind.AuthenticationRequired, "XQ_AUTH_REQUIRED");
        }

        var message = TryReadProviderMessage(body);
        if (message is "XQ_AUTH_REQUIRED" or "XQ_ACTOR_NOT_FOUND")
        {
            return (CreateObservationFailureKind.AuthenticationRequired, message);
        }

        if (message is
            "XQ_ACTOR_DISABLED" or
            "XQ_MEMBERSHIP_REQUIRED" or
            "XQ_MEMBERSHIP_DISABLED" or
            "XQ_TEACHING_CAPABILITY_REQUIRED" or
            "XQ_STUDENT_NOT_IN_ORG" or
            "XQ_SUBJECT_PROFILE_REQUIRED" or
            "XQ_TEACHER_ASSIGNMENT_REQUIRED" or
            "XQ_TEACHING_CONTEXT_REQUIRED")
        {
            return (CreateObservationFailureKind.AuthorityChanged, message);
        }

        if (message is "XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD")
        {
            return (CreateObservationFailureKind.OperationConflict, message);
        }

        if (message is
            "XQ_OPERATION_ID_REQUIRED" or
            "XQ_INVALID_OBSERVATION_TEXT" or
            "XQ_INVALID_CAPTURE_METADATA")
        {
            return (CreateObservationFailureKind.Validation, message);
        }

        if ((int)statusCode >= 500)
        {
            return (
                CreateObservationFailureKind.ResultUnknown,
                message ?? $"XQ_RESULT_UNKNOWN_HTTP_{(int)statusCode}");
        }

        if ((int)statusCode == 429)
        {
            return (
                CreateObservationFailureKind.Transient,
                message ?? "HTTP_429");
        }

        return (
            CreateObservationFailureKind.InvalidResponse,
            message ?? $"HTTP_{(int)statusCode}");
    }

    private static string? TryReadProviderMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
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
