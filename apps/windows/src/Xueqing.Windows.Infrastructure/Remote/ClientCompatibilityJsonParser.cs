using System.Text.Json;
using System.Text.RegularExpressions;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static partial class ClientCompatibilityJsonParser
{
    private static readonly string[] TopKeys =
    [
        "contract",
        "generated_at_server",
        "policy_revision",
        "client",
        "decision",
    ];

    private static readonly string[] ClientKeys =
    [
        "platform",
        "app_version",
        "contract_version",
    ];

    private static readonly string[] DecisionKeys =
    [
        "state",
        "reason_code",
        "minimum_supported_app_version",
        "recommended_app_version",
        "minimum_supported_contract_version",
        "server_contract_version",
        "update_uri",
    ];

    public static ClientCompatibilityDecision Parse(
        string json,
        ClientCompatibilityRequest expected)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireObject(root, "compatibility response");
        RequireExactKeys(root, TopKeys, "compatibility response");
        if (RequiredString(root, "contract") != "client_compatibility_v1")
        {
            throw Invalid("Unsupported compatibility contract.");
        }

        var generatedAt = DateTimeOffset.Parse(
            RequiredString(root, "generated_at_server"),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        var revision = RequiredString(root, "policy_revision");
        if (!PolicyRevisionRegex().IsMatch(revision))
        {
            throw Invalid("Compatibility policy revision is invalid.");
        }

        var client = root.GetProperty("client");
        RequireObject(client, "compatibility client echo");
        RequireExactKeys(client, ClientKeys, "compatibility client echo");
        var platform = RequiredString(client, "platform");
        var appVersion = RequiredString(client, "app_version");
        var contractVersion = RequiredInt(client, "contract_version");
        if (platform != expected.Platform ||
            appVersion != expected.AppVersion ||
            contractVersion != expected.ContractVersion)
        {
            throw Invalid("Compatibility response client echo does not match request.");
        }

        var decision = root.GetProperty("decision");
        RequireObject(decision, "compatibility decision");
        RequireExactKeys(decision, DecisionKeys, "compatibility decision");
        var state = RequiredString(decision, "state") switch
        {
            "supported" => ClientCompatibilityState.Supported,
            "update_recommended" => ClientCompatibilityState.UpdateRecommended,
            "update_required" => ClientCompatibilityState.UpdateRequired,
            "security_blocked" => ClientCompatibilityState.SecurityBlocked,
            _ => throw Invalid("Unsupported compatibility state."),
        };
        var reason = RequiredString(decision, "reason_code");
        if (!ReasonCodeRegex().IsMatch(reason))
        {
            throw Invalid("Compatibility reason code is invalid.");
        }

        var minimumContract = RequiredInt(decision, "minimum_supported_contract_version");
        var serverContract = RequiredInt(decision, "server_contract_version");
        if (minimumContract < 1 || serverContract < minimumContract)
        {
            throw Invalid("Compatibility contract window is invalid.");
        }

        Uri? updateUri = null;
        var updateElement = decision.GetProperty("update_uri");
        if (updateElement.ValueKind != JsonValueKind.Null)
        {
            if (updateElement.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(updateElement.GetString(), UriKind.Absolute, out updateUri) ||
                updateUri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(updateUri.UserInfo))
            {
                throw Invalid("Compatibility update URI is invalid.");
            }
        }

        return new ClientCompatibilityDecision(
            generatedAt,
            revision,
            platform,
            appVersion,
            contractVersion,
            state,
            reason,
            RequiredVersion(decision, "minimum_supported_app_version"),
            RequiredVersion(decision, "recommended_app_version"),
            minimumContract,
            serverContract,
            updateUri);
    }

    private static string RequiredVersion(JsonElement root, string name)
    {
        var value = RequiredString(root, name);
        if (value.Length > 64 || value != value.Trim())
        {
            throw Invalid($"Compatibility version '{name}' is invalid.");
        }
        return value;
    }

    private static int RequiredInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
        {
            throw Invalid($"Compatibility integer '{name}' is invalid.");
        }
        return result;
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Invalid($"Compatibility string '{name}' is invalid.");
        }
        return value.GetString()!;
    }

    private static void RequireObject(JsonElement element, string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{label} must be an object.");
        }
    }

    private static void RequireExactKeys(
        JsonElement element,
        IEnumerable<string> expected,
        string label)
    {
        var actual = element.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
        {
            throw Invalid($"{label} keys do not match the v1 contract.");
        }
    }

    private static InvalidDataException Invalid(string message) => new(message);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyRevisionRegex();

    [GeneratedRegex("^XQ_[A-Z0-9_]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReasonCodeRegex();
}
