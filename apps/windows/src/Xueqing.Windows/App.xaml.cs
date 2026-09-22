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
        var localReferenceWorkspace =
            LocalReferenceProviderTeachingWorkspaceFactory.CreateFromEnvironment();

        if (localReferenceWorkspace is not null)
        {
            ReplaceWindow(new MainWindow(new MainWindowViewModel(localReferenceWorkspace)));
            return;
        }

        if (IsExplicitPrototypeMode())
        {
            ReplaceWindow(new MainWindow(new MainWindowViewModel()));
            return;
        }

        await EnterProductionAsync();
    }

    private async Task EnterProductionAsync()
    {
        try
        {
            _productionRuntime = ProductionProviderClientRuntime.Create();
            var restore = await _productionRuntime.AuthCoordinator.RestoreAsync();

            switch (restore)
            {
                case ProviderRefreshOutcome.Usable:
                    ShowAuthenticatedWorkspace();
                    break;

                case ProviderRefreshOutcome.SignedOut:
                    ShowSignIn(
                        SignInSurfaceMode.Credentials,
                        "请使用你的学情账号登录。");
                    break;

                case ProviderRefreshOutcome.Invalid:
                    ShowSignIn(
                        SignInSurfaceMode.Credentials,
                        "登录状态已失效，请重新登录。");
                    break;

                case ProviderRefreshOutcome.RefreshRequired:
                    ShowSignIn(
                        SignInSurfaceMode.ReconnectRequired,
                        "暂时无法确认当前登录状态。可以重试连接，或退出此设备账号后重新登录。");
                    break;

                default:
                    ShowConfigurationUnavailable();
                    break;
            }
        }
        catch (Exception error) when (error is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            RefreshTokenVaultUnavailableException)
        {
            _productionRuntime = null;
            ShowConfigurationUnavailable();
        }
    }

    private async Task<SignInActionResult> SignInAsync(
        string email,
        string password)
    {
        if (_productionRuntime is null)
        {
            return new SignInActionResult(
                false,
                SignInSurfaceMode.Credentials,
                "当前客户端配置不可用，无法登录。");
        }

        try
        {
            await _productionRuntime.AuthCoordinator.SignInWithPasswordAsync(
                email,
                password);
            ShowAuthenticatedWorkspace();
            return new SignInActionResult(
                true,
                SignInSurfaceMode.Credentials,
                string.Empty);
        }
        catch (ProviderAuthTransportException error)
        {
            var message = error.Kind switch
            {
                ProviderAuthTransportFailureKind.Rejected =>
                    "邮箱或密码不正确，或账号当前不可用。",
                ProviderAuthTransportFailureKind.RateLimited =>
                    "尝试次数过多，请稍后再试。",
                ProviderAuthTransportFailureKind.Transient or
                ProviderAuthTransportFailureKind.ResultUnknown =>
                    "网络暂时不可用，登录没有完成，请稍后重试。",
                _ =>
                    "登录服务返回无法验证的结果，已拒绝建立会话。",
            };
            return new SignInActionResult(
                false,
                SignInSurfaceMode.Credentials,
                message);
        }
        catch (Exception error) when (error is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            InvalidOperationException)
        {
            return new SignInActionResult(
                false,
                SignInSurfaceMode.Credentials,
                "暂时无法建立安全登录会话，请稍后重试。");
        }
    }

    private async Task<SignInActionResult> RetrySessionAsync()
    {
        if (_productionRuntime is null)
        {
            return new SignInActionResult(
                false,
                SignInSurfaceMode.Credentials,
                "当前客户端配置不可用，无法恢复登录。");
        }

        try
        {
            var result = await _productionRuntime.AuthCoordinator.RestoreAsync();
            return result switch
            {
                ProviderRefreshOutcome.Usable => CompleteAuthenticatedRetry(),
                ProviderRefreshOutcome.SignedOut =>
                    new SignInActionResult(
                        false,
                        SignInSurfaceMode.Credentials,
                        "当前没有可恢复的登录，请重新登录。"),
                ProviderRefreshOutcome.Invalid =>
                    new SignInActionResult(
                        false,
                        SignInSurfaceMode.Credentials,
                        "登录状态已失效，请重新登录。"),
                ProviderRefreshOutcome.RefreshRequired =>
                    new SignInActionResult(
                        false,
                        SignInSurfaceMode.ReconnectRequired,
                        "仍然无法确认登录状态，请检查网络后重试。"),
                _ =>
                    new SignInActionResult(
                        false,
                        SignInSurfaceMode.ReconnectRequired,
                        "暂时无法恢复登录状态。"),
            };
        }
        catch
        {
            return new SignInActionResult(
                false,
                SignInSurfaceMode.ReconnectRequired,
                "暂时无法恢复登录状态，请稍后再试。");
        }
    }

    private SignInActionResult CompleteAuthenticatedRetry()
    {
        ShowAuthenticatedWorkspace();
        return new SignInActionResult(
            true,
            SignInSurfaceMode.Credentials,
            string.Empty);
    }

    private async Task<SignInActionResult> ClearSessionAsync()
    {
        if (_productionRuntime is null)
        {
            return new SignInActionResult(
                false,
                SignInSurfaceMode.Credentials,
                "当前客户端配置不可用。");
        }

        try
        {
            var result = await _productionRuntime.AuthCoordinator.SignOutAsync();
            var message = result == ProviderSignOutOutcome.LocalOnlyRemoteUnconfirmed
                ? "已退出本机登录；服务器撤销暂未确认。重新登录前请确认网络正常。"
                : "已退出此设备账号，请重新登录。";
            return new SignInActionResult(
                false,
                SignInSurfaceMode.Credentials,
                message);
        }
        catch
        {
            return new SignInActionResult(
                false,
                SignInSurfaceMode.Credentials,
                "本机安全会话无法完整清除，已停止继续使用旧会话。");
        }
    }

    private async Task SignOutCurrentWorkspaceAsync()
    {
        if (_productionRuntime is null)
        {
            ShowConfigurationUnavailable();
            return;
        }

        try
        {
            var result = await _productionRuntime.AuthCoordinator.SignOutAsync();
            ShowSignIn(
                SignInSurfaceMode.Credentials,
                result == ProviderSignOutOutcome.LocalOnlyRemoteUnconfirmed
                    ? "已退出本机登录；服务器撤销暂未确认。"
                    : "已退出登录。");
        }
        catch
        {
            ShowConfigurationUnavailable(
                "本机安全会话无法完整清除，已停止继续显示原账号工作区。");
        }
    }

    private void ShowAuthenticatedWorkspace()
    {
        if (_productionRuntime is null)
        {
            ShowConfigurationUnavailable();
            return;
        }

        ReplaceWindow(
            new MainWindow(
                new MainWindowViewModel(
                    _productionRuntime.CreateTeachingWorkspace()),
                SignOutCurrentWorkspaceAsync));
    }

    private void ShowSignIn(
        SignInSurfaceMode mode,
        string statusText)
    {
        ReplaceWindow(
            new SignInWindow(
                mode,
                statusText,
                SignInAsync,
                RetrySessionAsync,
                ClearSessionAsync));
    }

    private void ShowConfigurationUnavailable(
        string? message = null)
    {
        ReplaceWindow(
            new MainWindow(
                MainWindowViewModel.CreateConfigurationUnavailable(
                    string.IsNullOrWhiteSpace(message)
                        ? "当前客户端配置或安全会话不可用，教学数据未加载。"
                        : message)));
    }

    private void ReplaceWindow(Window next)
    {
        var previous = _window;
        _window = next;
        next.Activate();
        previous?.Close();

        if (next is MainWindow &&
            WindowsPackagedAppIntegrationProbe.IsEnabled())
        {
            _ = WindowsPackagedAppIntegrationProbe.RunAndWriteReportAsync();
        }
    }

    private static bool IsExplicitPrototypeMode() =>
        string.Equals(
            Environment.GetEnvironmentVariable(PrototypeModeVariable),
            "1",
            StringComparison.Ordinal);
}
