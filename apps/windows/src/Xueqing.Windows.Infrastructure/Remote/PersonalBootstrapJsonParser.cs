using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class PersonalBootstrapJsonParser
{
    private const string ExpectedContract = "personal_bootstrap_v1";

    public static PersonalBootstrapSnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireObject(root, "root");

        if (!string.Equals(GetRequiredString(root, "contract"), ExpectedContract, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unexpected PersonalBootstrap contract.");
        }

        var generatedAt = GetRequiredTimestamp(root, "generated_at_server");
        if (!root.TryGetProperty("actor", out var actor))
        {
            throw new InvalidDataException("PersonalBootstrap actor is missing.");
        }
        RequireObject(actor, "actor");
        var actorId = GetRequiredGuid(actor, "app_user_id");
        var actorDisplayName = GetRequiredNonBlankString(actor, "display_name");

        var organizations = ParseOrganizations(root);
        var teachingContexts = ParseTeachingContexts(root, organizations);

        return new PersonalBootstrapSnapshot(
            generatedAt,
            actorId,
            actorDisplayName,
            organizations,
            teachingContexts);
    }

    private static IReadOnlyList<PersonalOrganization> ParseOrganizations(JsonElement root)
    {
        if (!root.TryGetProperty("organizations", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("PersonalBootstrap organizations must be an array.");
        }

        var organizations = new List<PersonalOrganization>();
        var ids = new HashSet<Guid>();
        foreach (var item in element.EnumerateArray())
        {
            RequireObject(item, "organization");
            var id = GetRequiredGuid(item, "organization_id");
            if (!ids.Add(id))
            {
                throw new InvalidDataException("PersonalBootstrap contains a duplicate organization.");
            }

            if (!item.TryGetProperty("can_teach", out var canTeachElement) ||
                canTeachElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidDataException("PersonalBootstrap organization can_teach must be boolean.");
            }

            organizations.Add(new PersonalOrganization(
                id,
                GetRequiredNonBlankString(item, "name"),
                canTeachElement.GetBoolean()));
        }

        return organizations;
    }

    private static IReadOnlyList<PersonalTeachingContext> ParseTeachingContexts(
        JsonElement root,
        IReadOnlyList<PersonalOrganization> organizations)
    {
        if (!root.TryGetProperty("teaching_contexts", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("PersonalBootstrap teaching_contexts must be an array.");
        }

        var teachingOrganizations = organizations
            .Where(organization => organization.CanTeach)
            .Select(organization => organization.OrganizationId)
            .ToHashSet();
        var assignmentIds = new HashSet<Guid>();
        var scopeKeys = new HashSet<(Guid OrganizationId, Guid StudentId, Guid SubjectProfileId)>();
        var contexts = new List<PersonalTeachingContext>();

        foreach (var item in element.EnumerateArray())
        {
            RequireObject(item, "teaching context");
            var organizationId = GetRequiredGuid(item, "organization_id");
            var studentId = GetRequiredGuid(item, "student_id");
            var subjectProfileId = GetRequiredGuid(item, "subject_profile_id");
            var assignmentId = GetRequiredGuid(item, "assignment_id");

            if (!teachingOrganizations.Contains(organizationId))
            {
                throw new InvalidDataException("Teaching context does not reference an active teaching-capable organization in the same envelope.");
            }
            if (!assignmentIds.Add(assignmentId))
            {
                throw new InvalidDataException("PersonalBootstrap contains a duplicate assignment.");
            }
            if (!scopeKeys.Add((organizationId, studentId, subjectProfileId)))
            {
                throw new InvalidDataException("PersonalBootstrap contains a duplicate teaching scope.");
            }

            contexts.Add(new PersonalTeachingContext(
                organizationId,
                studentId,
                GetRequiredNonBlankString(item, "student_display_name"),
                subjectProfileId,
                GetRequiredNonBlankString(item, "subject_key"),
                assignmentId));
        }

        return contexts;
    }

    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"PersonalBootstrap {name} must be an object.");
        }
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"PersonalBootstrap property '{propertyName}' must be a string.");
        }
        return property.GetString() ?? throw new InvalidDataException($"PersonalBootstrap property '{propertyName}' is null.");
    }

    private static string GetRequiredNonBlankString(JsonElement element, string propertyName)
    {
        var value = GetRequiredString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"PersonalBootstrap property '{propertyName}' must not be blank.");
        }
        return value;
    }

    private static Guid GetRequiredGuid(JsonElement element, string propertyName)
    {
        var value = GetRequiredString(element, propertyName);
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty)
        {
            throw new InvalidDataException($"PersonalBootstrap property '{propertyName}' must be a non-empty canonical UUID.");
        }
        return parsed;
    }

    private static DateTimeOffset GetRequiredTimestamp(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            !property.TryGetDateTimeOffset(out var parsed))
        {
            throw new InvalidDataException($"PersonalBootstrap property '{propertyName}' must be an ISO timestamp.");
        }
        return parsed;
    }
}
