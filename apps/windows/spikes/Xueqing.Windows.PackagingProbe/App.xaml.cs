using Microsoft.UI.Xaml;

namespace Xueqing.Windows.PackagingProbe;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await PackagingProbeRunner.RunAsync();

        _window = new MainWindow();
        _window.Activate();
    }
}
