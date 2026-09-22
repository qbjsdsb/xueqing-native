using Microsoft.UI.Xaml;
using Xueqing.Windows.Infrastructure.Auth;
using Xueqing.Windows.Integration;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Windows;

public partial class App : Application
{
    private const string PrototypeModeVariable = "XUEQING_WINDOWS_PROTOTYPE_MODE";
    private Window? _window;
    private ProductionProviderClientRuntime? _productionRuntime;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindowViewModel viewModel;
        var localReferenceWorkspace =
            LocalReferenceProviderTeachingWorkspaceFactory.CreateFromEnvironment();

        if (localReferenceWorkspace is not null)
        {
            viewModel = new MainWindowViewModel(localReferenceWorkspace);
        }
        else if (IsExplicitPrototypeMode())
        {
            viewModel = new MainWindowViewModel();
        }
        else
        {
            viewModel = await CreateProductionViewModelAsync();
        }

        _window = new MainWindow(viewModel);
        _window.Activate();

        if (WindowsPackagedAppIntegrationProbe.IsEnabled())
        {
            _ = WindowsPackagedAppIntegrationProbe.RunAndWriteReportAsync();
        }
    }

    private async Task<MainWindowViewModel> CreateProductionViewModelAsync()
    {
        try
        {
            _productionRuntime = ProductionProviderClientRuntime.Create();
            var restore = await _productionRuntime.AuthCoordinator.RestoreAsync();

            return restore switch
            {
                ProviderRefreshOutcome.Usable =>
                    new MainWindowViewModel(_productionRuntime.CreateTeachingWorkspace()),
                ProviderRefreshOutcome.SignedOut =>
                    MainWindowViewModel.CreateSignedOut(),
                ProviderRefreshOutcome.Invalid =>
                    MainWindowViewModel.CreateSignedOut("登录状态已失效，请重新登录。"),
                ProviderRefreshOutcome.RefreshRequired =>
                    MainWindowViewModel.CreateSignedOut(
                        "暂时无法确认登录状态，请联网后重新登录或重试。"),
                _ => MainWindowViewModel.CreateConfigurationUnavailable(),
            };
        }
        catch (Exception error) when (error is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            RefreshTokenVaultUnavailableException)
        {
            _productionRuntime = null;
            return MainWindowViewModel.CreateConfigurationUnavailable(
                "当前客户端配置或安全会话不可用，教学数据未加载。");
        }
    }

    private static bool IsExplicitPrototypeMode() =>
        string.Equals(
            Environment.GetEnvironmentVariable(PrototypeModeVariable),
            "1",
            StringComparison.Ordinal);
}
