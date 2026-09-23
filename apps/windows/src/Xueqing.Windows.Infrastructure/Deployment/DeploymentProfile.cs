using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Xueqing.Windows.Infrastructure.Deployment;

public sealed record DeploymentProfileSource(
    string Repository,
    string Commit);

public sealed record DeploymentProfile(
    string ProfileId,
    string EnvironmentId,
    string TrustDomainId,
    string ProviderId,
    Uri ProjectOrigin,
    string PublishableKey,
    string RequiredEdgeRegion,
    IReadOnlyList<string> Capabilities,
    DeploymentProfileSource Source);

public static partial class DeploymentProfileParser
{
    private static readonly HashSet<string> SupportedCapabilities =
    [
        "auth",
        "database-rpc",
        "edge-functions",
        "private-storage",
    ];

    private static readonly string[] TopLevelKeys =
    [
        "contract",
        "profile_id",
        "environment_id",
        "trust_domain_id",
        "provider_id",
        "project_origin",
        "publishable_key",
        "required_edge_region",
        "capabilities",
        "source",
    ];

    private static readonly string[] SourceKeys =
    [
        "repository",
        "commit",
    ];

    public static DeploymentProfile Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            RequireObject(root, "deployment profile");
            RequireExactKeys(root, TopLevelKeys, "deployment profile");

            if (RequiredString(root, "contract") != "deployment_profile_v1")
            {
                throw Invalid("Unsupported deployment profile contract.");
            }

            var profileId = Identifier(root, "profile_id", 64);
            var environmentId = Identifier(root, "environment_id", 64);
            var trustDomainId = Identifier(root, "trust_domain_id", 96);
            var providerId = Identifier(root, "provider_id", 32);
            var projectOrigin = ParseOrigin(RequiredString(root, "project_origin"));
            var publishableKey = ValidatePublishableKey(
                RequiredString(root, "publishable_key"));
            var requiredEdgeRegion = RequiredString(root, "required_edge_region");
            if (!RegionRegex().IsMatch(requiredEdgeRegion))
            {
                throw Invalid("Deployment profile Edge region is invalid.");
            }

            var capabilities = ParseCapabilities(root);
            var sourceElement = root.GetProperty("source");
            RequireObject(sourceElement, "deployment profile source");
            RequireExactKeys(sourceElement, SourceKeys, "deployment profile source");
            var repository = RequiredString(sourceElement, "repository");
            if (repository != "qbjsdsb/xueqing-native")
            {
                throw Invalid("Deployment profile source repository is invalid.");
            }

            var commit = RequiredString(sourceElement, "commit");
            if (!CommitRegex().IsMatch(commit))
            {
                throw Invalid("Deployment profile source commit is invalid.");
            }

            return new DeploymentProfile(
                profileId,
                environmentId,
                trustDomainId,
                providerId,
                projectOrigin,
                publishableKey,
                requiredEdgeRegion,
                capabilities,
                new DeploymentProfileSource(repository, commit));
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Deployment profile JSON is malformed.",
                error);
        }
    }

    private static IReadOnlyList<string> ParseCapabilities(JsonElement root)
    {
        if (!root.TryGetProperty("capabilities", out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("Deployment profile capabilities must be an array.");
        }

        var values = element.EnumerateArray()
            .Select(value =>
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString())
                    ? value.GetString()!
                    : throw Invalid("Deployment profile capability is invalid."))
            .ToArray();

        if (values.Length == 0 ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length ||
            !values.SequenceEqual(values.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            values.Any(value => !SupportedCapabilities.Contains(value)))
        {
            throw Invalid("Deployment profile capabilities are invalid.");
        }

        return values;
    }

    private static Uri ParseOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.Port is not (-1 or 443) ||
            uri.IsLoopback ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(uri.Host, out _))
        {
            throw Invalid("Deployment profile project origin is invalid.");
        }

        return new Uri(
            uri.GetLeftPart(UriPartial.Authority).TrimEnd('/'),
            UriKind.Absolute);
    }

    private static string ValidatePublishableKey(string value)
    {
        if (value.Length is < 16 or > 2048 ||
            value.Any(char.IsWhiteSpace))
        {
            throw Invalid("Deployment profile publishable key is invalid.");
        }

        var privilegedPrefix = string.Concat("sb_", "secret", "_");
        var privilegedRoleMarker = string.Concat("service", "_role");
        if (value.Contains(privilegedPrefix, StringComparison.OrdinalIgnoreCase) ||
            value.Contains(privilegedRoleMarker, StringComparison.OrdinalIgnoreCase) ||
            value.Contains("postgresql://", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("Deployment profile contains privileged credential material.");
        }

        return value;
    }

    private static string Identifier(
        JsonElement root,
        string propertyName,
        int maxLength)
    {
        var value = RequiredString(root, propertyName);
        if (value.Length is < 2 ||
            value.Length > maxLength ||
            !IdentifierRegex().IsMatch(value))
        {
            throw Invalid($"Deployment profile identifier '{propertyName}' is invalid.");
        }

        return value;
    }

    private static string RequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Invalid($"Deployment profile property '{propertyName}' is missing/invalid.");
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
            throw Invalid($"{label} keys do not match the accepted v1 contract.");
        }
    }

    private static InvalidDataException Invalid(string message) => new(message);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,95}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex("^[a-z]{2}-[a-z0-9-]+-[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RegionRegex();

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitRegex();
}
