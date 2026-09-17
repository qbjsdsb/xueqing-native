using Microsoft.UI.Xaml;
using Xueqing.Windows.Integration;
using Xueqing.Windows.ViewModels;

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
        var personalWorkspace = LocalReferenceProviderStudentWorkspaceFactory.CreateFromEnvironment();
        var viewModel = new MainWindowViewModel(personalWorkspace);
        _window = new MainWindow(viewModel);
        _window.Activate();

        if (WindowsPackagedAppIntegrationProbe.IsEnabled())
        {
            _ = WindowsPackagedAppIntegrationProbe.RunAndWriteReportAsync();
        }
    }
}
