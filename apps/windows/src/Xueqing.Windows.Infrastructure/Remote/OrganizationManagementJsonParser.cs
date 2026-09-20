using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class OrganizationManagementJsonParser
{
    private const string ExpectedContract = "organization_management_v1";

    public static OrganizationManagementSnapshot Parse(string json, Guid expectedOrganizationId)
    {
        if (expectedOrganizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization id must not be empty.", nameof(expectedOrganizationId));
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireObject(root, "root");

        if (!string.Equals(GetRequiredString(root, "contract"), ExpectedContract, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unexpected OrganizationManagement contract.");
        }

        var generatedAt = GetRequiredTimestamp(root, "generated_at_server");

        if (!root.TryGetProperty("actor", out var actor))
        {
            throw new InvalidDataException("OrganizationManagement actor is missing.");
        }
        RequireObject(actor, "actor");
        var actorId = GetRequiredGuid(actor, "app_user_id");
        var actorDisplayName = GetRequiredNonBlankString(actor, "display_name");
        var actorRole = ParseRole(GetRequiredString(actor, "membership_role"));
        if (actorRole == OrganizationMembershipRole.Teacher)
        {
            throw new InvalidDataException("Teacher-only actor cannot be a management projection actor.");
        }

        if (!root.TryGetProperty("organization", out var organization))
        {
            throw new InvalidDataException("OrganizationManagement organization is missing.");
        }
        RequireObject(organization, "organization");
        var organizationId = GetRequiredGuid(organization, "organization_id");
        if (organizationId != expectedOrganizationId)
        {
            throw new InvalidDataException("OrganizationManagement organization does not match the requested scope.");
        }

        var organizationName = GetRequiredNonBlankString(organization, "name");
        var timeZone = GetRequiredNonBlankString(organization, "time_zone");

        if (!root.TryGetProperty("capabilities", out var capabilitiesElement))
        {
            throw new InvalidDataException("OrganizationManagement capabilities are missing.");
        }
        RequireObject(capabilitiesElement, "capabilities");
        var capabilities = new OrganizationManagementCapabilities(
            GetRequiredBoolean(capabilitiesElement, "can_invite_owner"),
            GetRequiredBoolean(capabilitiesElement, "can_invite_admin"),
            GetRequiredBoolean(capabilitiesElement, "can_invite_teacher"));
        ValidateCapabilities(actorRole, capabilities);

        var members = ParseMembers(root);
        var actorMember = members.SingleOrDefault(member => member.AppUserId == actorId)
            ?? throw new InvalidDataException("OrganizationManagement actor is missing from member roster.");
        if (!actorMember.AppUserEnabled ||
            actorMember.MembershipStatus != OrganizationMembershipStatus.Active ||
            actorMember.MembershipRole != actorRole ||
            !string.Equals(actorMember.DisplayName, actorDisplayName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("OrganizationManagement actor and roster disagree.");
        }

        return new OrganizationManagementSnapshot(
            generatedAt,
            actorId,
            actorDisplayName,
            actorRole,
            organizationId,
            organizationName,
            timeZone,
            capabilities,
            members);
    }

    private static IReadOnlyList<OrganizationManagementMember> ParseMembers(JsonElement root)
    {
        if (!root.TryGetProperty("members", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("OrganizationManagement members must be an array.");
        }

        var members = new List<OrganizationManagementMember>();
        var ids = new HashSet<Guid>();
        foreach (var item in element.EnumerateArray())
        {
            RequireObject(item, "member");
            var appUserId = GetRequiredGuid(item, "app_user_id");
            if (!ids.Add(appUserId))
            {
                throw new InvalidDataException("OrganizationManagement contains duplicate members.");
            }

            members.Add(new OrganizationManagementMember(
                appUserId,
                GetRequiredNonBlankString(item, "display_name"),
                GetRequiredBoolean(item, "app_user_enabled"),
                ParseRole(GetRequiredString(item, "membership_role")),
                ParseStatus(GetRequiredString(item, "membership_status")),
                GetRequiredBoolean(item, "can_teach")));
        }

        if (members.Count == 0)
        {
            throw new InvalidDataException("OrganizationManagement member roster must not be empty.");
        }

        return members;
    }

    private static void ValidateCapabilities(
        OrganizationMembershipRole actorRole,
        OrganizationManagementCapabilities capabilities)
    {
        var expected = actorRole switch
        {
            OrganizationMembershipRole.Owner => new OrganizationManagementCapabilities(false, true, true),
            OrganizationMembershipRole.Admin => new OrganizationManagementCapabilities(true, false, true),
            _ => throw new InvalidDataException("Unsupported management actor role."),
        };

        if (capabilities != expected)
        {
            throw new InvalidDataException("OrganizationManagement capabilities disagree with actor role.");
        }
    }

    private static OrganizationMembershipRole ParseRole(string value) => value switch
    {
        "owner" => OrganizationMembershipRole.Owner,
        "admin" => OrganizationMembershipRole.Admin,
        "teacher" => OrganizationMembershipRole.Teacher,
        _ => throw new InvalidDataException("OrganizationManagement contains an unknown membership role."),
    };

    private static OrganizationMembershipStatus ParseStatus(string value) => value switch
    {
        "active" => OrganizationMembershipStatus.Active,
        "disabled" => OrganizationMembershipStatus.Disabled,
        _ => throw new InvalidDataException("OrganizationManagement contains an unknown membership status."),
    };

    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"OrganizationManagement {name} must be an object.");
        }
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"OrganizationManagement property '{propertyName}' must be a string.");
        }
        return property.GetString()
            ?? throw new InvalidDataException($"OrganizationManagement property '{propertyName}' is null.");
    }

    private static string GetRequiredNonBlankString(JsonElement element, string propertyName)
    {
        var value = GetRequiredString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"OrganizationManagement property '{propertyName}' must not be blank.");
        }
        return value;
    }

    private static Guid GetRequiredGuid(JsonElement element, string propertyName)
    {
        var value = GetRequiredString(element, propertyName);
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty)
        {
            throw new InvalidDataException($"OrganizationManagement property '{propertyName}' must be a canonical non-empty UUID.");
        }
        return parsed;
    }

    private static bool GetRequiredBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"OrganizationManagement property '{propertyName}' must be boolean.");
        }
        return property.GetBoolean();
    }

    private static DateTimeOffset GetRequiredTimestamp(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            !property.TryGetDateTimeOffset(out var parsed))
        {
            throw new InvalidDataException($"OrganizationManagement property '{propertyName}' must be an ISO timestamp.");
        }
        return parsed;
    }
}
