namespace Xueqing.Windows.Core.Agent;

public enum AgentNavigationTargetKind
{
    Today,
    Student,
    LearningCase,
}

/// <summary>
/// Navigation-only contract for external automation.
///
/// The URI may select a product surface or an application-owned entity id.
/// It never carries provider identity, organization authority, assignment,
/// credentials, operation ids, or write intent.
/// </summary>
public sealed record AgentNavigationRequest(
    AgentNavigationTargetKind Kind,
    Guid? EntityId = null)
{
    public const string Scheme = "xueqing";

    public static bool TryParse(Uri? uri, out AgentNavigationRequest? request)
    {
        request = null;

        if (uri is null ||
            !uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();

        if (host == "today")
        {
            if (uri.AbsolutePath is not ("" or "/"))
            {
                return false;
            }

            request = new AgentNavigationRequest(AgentNavigationTargetKind.Today);
            return true;
        }

        if (host is not ("student" or "learning"))
        {
            return false;
        }

        // Entity routes are deliberately one canonical UUID path segment.
        // Reject trailing slashes, encoded separators and arbitrary parameters.
        if (uri.AbsolutePath.Length != 37 ||
            uri.AbsolutePath[0] != '/' ||
            !Guid.TryParseExact(uri.AbsolutePath.AsSpan(1), "D", out var entityId) ||
            entityId == Guid.Empty)
        {
            return false;
        }

        request = new AgentNavigationRequest(
            host == "student"
                ? AgentNavigationTargetKind.Student
                : AgentNavigationTargetKind.LearningCase,
            entityId);
        return true;
    }

    public Uri ToUri() =>
        Kind switch
        {
            AgentNavigationTargetKind.Today =>
                new Uri($"{Scheme}://today"),
            AgentNavigationTargetKind.Student when EntityId is { } studentId =>
                new Uri($"{Scheme}://student/{studentId:D}"),
            AgentNavigationTargetKind.LearningCase when EntityId is { } caseId =>
                new Uri($"{Scheme}://learning/{caseId:D}"),
            _ => throw new InvalidOperationException(
                "Entity navigation requires a non-empty application-owned id."),
        };
}
