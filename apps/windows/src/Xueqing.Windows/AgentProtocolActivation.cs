using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Xueqing.Windows.Core.Agent;

namespace Xueqing.Windows;

internal static class AgentProtocolActivation
{
    public static AgentNavigationRequest? ReadCurrentNavigation()
    {
        try
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activation.Kind != ExtendedActivationKind.Protocol ||
                activation.Data is not IProtocolActivatedEventArgs protocolArgs)
            {
                return null;
            }

            return AgentNavigationRequest.TryParse(
                protocolArgs.Uri,
                out var request)
                    ? request
                    : null;
        }
        catch
        {
            // External activation is never allowed to prevent normal startup.
            // Malformed/unsupported activation therefore fails closed to the
            // ordinary authenticated application path.
            return null;
        }
    }
}
