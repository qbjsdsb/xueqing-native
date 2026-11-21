using Xueqing.Linux.Infrastructure.LocalData;
using Xueqing.Windows.Core.Agent;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Linux.Infrastructure.Agent;

/// <summary>
/// Parses external xueqing:// protocol activations from Linux desktop launchers,
/// xdg-open, or CLI arguments using the exact fail-closed AgentNavigationRequest contract.
/// </summary>
public static class LinuxProtocolActivation
{
    public static AgentNavigationRequest? ParseUri(string? uriText)
    {
        if (string.IsNullOrWhiteSpace(uriText))
        {
            return null;
        }

        if (!Uri.TryCreate(uriText.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return AgentNavigationRequest.TryParse(uri, out var request)
            ? request
            : null;
    }

    public static AgentNavigationRequest? ParseArguments(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith('-'))
            {
                continue;
            }

            if (arg.StartsWith($"{AgentNavigationRequest.Scheme}://", StringComparison.OrdinalIgnoreCase))
            {
                return ParseUri(arg);
            }
        }

        return null;
    }

    public static StudentSummary? FindPrototypeStudentByUuid(
        Guid studentUuid,
        IEnumerable<StudentSummary> students)
    {
        if (studentUuid == Guid.Empty)
        {
            return null;
        }

        return students.FirstOrDefault(student =>
            PrototypeDraftIdentity.ResolveStudentUuid(student.Id) == studentUuid);
    }
}
