using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class StudentRecentObservationsJsonParser
{
    private const string ExpectedContract = "student_recent_observations_v1";
    private const int MaximumItems = 20;

    public static StudentRecentObservationsSnapshot Parse(
        string json,
        StudentObservationScope requestedScope,
        Guid expectedActorAppUserId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireObject(root, "root");

        if (!string.Equals(GetRequiredString(root, "contract"), ExpectedContract, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unexpected projection contract.");
        }

        var actorId = GetRequiredGuid(root, "actor_app_user_id");
        if (actorId != expectedActorAppUserId)
        {
            throw new InvalidDataException("Projection actor does not match the active application identity.");
        }

        var organizationId = GetRequiredGuid(root, "organization_id");
        var studentId = GetRequiredGuid(root, "student_id");
        var subjectProfileId = GetRequiredGuid(root, "subject_profile_id");
        if (organizationId != requestedScope.OrganizationId ||
            studentId != requestedScope.StudentId ||
            subjectProfileId != requestedScope.SubjectProfileId)
        {
            throw new InvalidDataException("Projection scope does not match the requested teaching context.");
        }

        var generatedAt = GetRequiredTimestamp(root, "generated_at_server");
        var assignmentId = GetRequiredGuid(root, "assignment_id");
        var studentDisplayName = GetRequiredNonBlankString(root, "student_display_name");
        var subjectKey = GetRequiredNonBlankString(root, "subject_key");

        if (!root.TryGetProperty("observations", out var observationsElement) ||
            observationsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Projection observations must be an array.");
        }

        var observations = new List<StudentRecentObservation>();
        var seenIds = new HashSet<Guid>();
        StudentRecentObservation? previous = null;
        foreach (var item in observationsElement.EnumerateArray())
        {
            if (observations.Count >= MaximumItems)
            {
                throw new InvalidDataException("Projection exceeded the v1 item bound.");
            }

            RequireObject(item, "observation");
            var observation = new StudentRecentObservation(
                GetRequiredGuid(item, "observation_id"),
                GetRequiredGuid(item, "actor_app_user_id"),
                GetRequiredNonBlankString(item, "raw_text"),
                GetOptionalTimestamp(item, "client_captured_at"),
                GetRequiredTimestamp(item, "created_at_server"));

            if (!seenIds.Add(observation.ObservationId))
            {
                throw new InvalidDataException("Projection contains a duplicate observation id.");
            }

            if (previous is not null && ComesBefore(observation, previous))
            {
                throw new InvalidDataException("Projection observations are not in authoritative descending order.");
            }

            observations.Add(observation);
            previous = observation;
        }

        if (!root.TryGetProperty("has_more", out var hasMoreElement) ||
            hasMoreElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException("Projection has_more must be boolean.");
        }

        var hasMore = hasMoreElement.GetBoolean();
        if (hasMore && observations.Count != MaximumItems)
        {
            throw new InvalidDataException("A truncated v1 snapshot must contain the full visible item bound.");
        }

        return new StudentRecentObservationsSnapshot(
            generatedAt,
            actorId,
            organizationId,
            studentId,
            studentDisplayName,
            subjectProfileId,
            subjectKey,
            assignmentId,
            observations,
            hasMore);
    }

    private static bool ComesBefore(StudentRecentObservation current, StudentRecentObservation previous)
    {
        if (current.CreatedAtServer > previous.CreatedAtServer)
        {
            return true;
        }

        if (current.CreatedAtServer < previous.CreatedAtServer)
        {
            return false;
        }

        return string.CompareOrdinal(
            current.ObservationId.ToString("D"),
            previous.ObservationId.ToString("D")) > 0;
    }

    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Projection {name} must be an object.");
        }
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must be a string.");
        }

        return property.GetString() ?? throw new InvalidDataException($"Projection property '{propertyName}' is null.");
    }

    private static string GetRequiredNonBlankString(JsonElement element, string propertyName)
    {
        var value = GetRequiredString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must not be blank.");
        }

        return value;
    }

    private static Guid GetRequiredGuid(JsonElement element, string propertyName)
    {
        var value = GetRequiredString(element, propertyName);
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty)
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must be a non-empty canonical UUID.");
        }

        return parsed;
    }

    private static DateTimeOffset GetRequiredTimestamp(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            !property.TryGetDateTimeOffset(out var parsed))
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must be an ISO timestamp.");
        }

        return parsed;
    }

    private static DateTimeOffset? GetOptionalTimestamp(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String || !property.TryGetDateTimeOffset(out var parsed))
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must be null or an ISO timestamp.");
        }

        return parsed;
    }
}
