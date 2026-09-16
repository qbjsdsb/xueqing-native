using Microsoft.UI.Xaml;
using Xueqing.Windows.Integration;

namespace Xueqing.Windows;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();

        if (WindowsPackagedAppIntegrationProbe.IsEnabled())
        {
            _ = WindowsPackagedAppIntegrationProbe.RunAndWriteReportAsync();
        }
    }
}
