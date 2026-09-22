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
            return ReadNavigation(
                AppInstance.GetCurrent().GetActivatedEventArgs());
        }
        catch
        {
            // External activation is never allowed to prevent normal startup.
            return null;
        }
    }

    public static AgentNavigationRequest? ReadNavigation(
        AppActivationArguments? activation)
    {
        try
        {
            if (activation is null ||
                activation.Kind != ExtendedActivationKind.Protocol ||
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
            // Malformed/unsupported activation fails closed to the ordinary
            // authenticated application path.
            return null;
        }
    }
}
