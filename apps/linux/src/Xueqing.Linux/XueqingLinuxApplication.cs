using Xueqing.Linux.Infrastructure.Agent;
using Xueqing.Linux.Infrastructure.Integration;
using Xueqing.Linux.Infrastructure.LocalData;
using Xueqing.Linux.Views;
using Xueqing.Windows.Core.Agent;
using Xueqing.Windows.Infrastructure.Auth;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Linux;

internal sealed class XueqingLinuxApplication : IDisposable
{
    public const string ApplicationId = "com.xueqing.app";
    public const string PrototypeModeVariable = "XUEQING_LINUX_PROTOTYPE_MODE";

    private readonly bool _prototypeMode;
    private readonly bool _smokeGui;
    private readonly Adw.Application _app;
    private LinuxProductionClientRuntime? _productionRuntime;
    private AgentNavigationRequest? _pendingAgentNavigation;
    private MainWindow? _mainWindow;
    private SignInWindow? _signInWindow;
    private bool _launched;
    private bool _smokePassed;
    private string? _smokeFailure;

    public XueqingLinuxApplication(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        _smokeGui = args.Contains("--smoke-gui", StringComparer.Ordinal);
        _prototypeMode = _smokeGui ||
            args.Contains("--prototype", StringComparer.Ordinal) ||
            string.Equals(
                Environment.GetEnvironmentVariable(PrototypeModeVariable),
                "1",
                StringComparison.Ordinal);
        _pendingAgentNavigation = LinuxProtocolActivation.ParseArguments(args);

        var appId = _smokeGui ? $"{ApplicationId}.smoke" : ApplicationId;
        var flags = _smokeGui
            ? Gio.ApplicationFlags.NonUnique
            : Gio.ApplicationFlags.HandlesOpen;

        _app = Adw.Application.New(appId, flags);
        _app.OnActivate += async (_, _) => await HandleActivateAsync();
        _app.OnOpen += async (_, openArgs) => await HandleOpenAsync(openArgs);
    }

    public int Run()
    {
        var exitCode = _app.RunWithSynchronizationContext(Array.Empty<string>());
        if (_smokeGui)
        {
            if (!_smokePassed)
            {
                Console.Error.WriteLine($"GUI SMOKE FAILED: {_smokeFailure ?? "unknown error"}");
                return 1;
            }

            Console.WriteLine("GUI SMOKE PASSED: Xueqing.Linux Adw.ApplicationWindow + MainWindowViewModel + xueqing:// routing + GLib SynchronizationContext verified.");
        }

        return exitCode;
    }

    private async Task HandleOpenAsync(Gio.Application.OpenSignalArgs args)
    {
        foreach (var file in args.Files)
        {
            var parsed = LinuxProtocolActivation.ParseUri(file.GetUri());
            if (parsed is not null)
            {
                _pendingAgentNavigation = parsed;
                break;
            }
        }

        if (!_launched)
        {
            await HandleActivateAsync();
            return;
        }

        if (_mainWindow is not null)
        {
            _mainWindow.Present();
            if (_pendingAgentNavigation is { } nav)
            {
                _pendingAgentNavigation = null;
                await _mainWindow.ApplyAgentNavigationAsync(nav);
            }
        }
        else if (_signInWindow is not null)
        {
            // Retain _pendingAgentNavigation across the ordinary login boundary.
            _signInWindow.Present();
        }
    }

    private async Task HandleActivateAsync()
    {
        if (_launched)
        {
            _mainWindow?.Present();
            _signInWindow?.Present();
            return;
        }

        _launched = true;

        var localRefWorkspace = LinuxProductionClientRuntime.TryCreateLocalReferenceWorkspaceFromEnvironment();
        if (localRefWorkspace is not null)
        {
            ShowMainWindow(new MainWindow(
                _app,
                new MainWindowViewModel(localRefWorkspace),
                initialAgentNavigation: ConsumePendingAgentNavigation()));
            return;
        }

        if (_prototypeMode)
        {
            var viewModel = new MainWindowViewModel();
            var window = new MainWindow(
                _app,
                viewModel,
                initialAgentNavigation: ConsumePendingAgentNavigation());
            ShowMainWindow(window);

            if (_smokeGui)
            {
                await RunGuiSmokeAsync(viewModel, window);
            }

            return;
        }

        await EnterProductionAsync();
    }

    private async Task EnterProductionAsync()
    {
        try
        {
            _productionRuntime = LinuxProductionClientRuntime.Create();
            var restore = await _productionRuntime.AuthCoordinator.RestoreAsync();
            switch (restore)
            {
                case ProviderRefreshOutcome.Usable:
                    ShowAuthenticatedWorkspace();
                    break;

                case ProviderRefreshOutcome.SignedOut:
                    ShowSignIn(SignInSurfaceMode.Credentials, "请使用你的学情账号登录。");
                    break;

                case ProviderRefreshOutcome.Invalid:
                    ShowSignIn(SignInSurfaceMode.Credentials, "登录状态已失效，请重新登录。");
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
            _productionRuntime?.Dispose();
            _productionRuntime = null;
            ShowConfigurationUnavailable();
        }
    }

    private async Task<SignInActionResult> SignInAsync(string email, string password)
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
            await _productionRuntime.AuthCoordinator.SignInWithPasswordAsync(email, password);
            ShowAuthenticatedWorkspace();
            return new SignInActionResult(true, SignInSurfaceMode.Credentials, string.Empty);
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
                _ => "登录服务返回无法验证的结果，已拒绝建立会话。",
            };
            return new SignInActionResult(false, SignInSurfaceMode.Credentials, message);
        }
        catch (Exception error) when (error is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            InvalidOperationException or
            RefreshTokenVaultUnavailableException)
        {
            return new SignInActionResult(
                false,
                SignInSurfaceMode.Credentials,
                "暂时无法建立安全登录会话，请检查系统密钥环后稍后重试。");
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
            switch (result)
            {
                case ProviderRefreshOutcome.Usable:
                    ShowAuthenticatedWorkspace();
                    return new SignInActionResult(true, SignInSurfaceMode.Credentials, string.Empty);

                case ProviderRefreshOutcome.SignedOut:
                    return new SignInActionResult(false, SignInSurfaceMode.Credentials, "当前没有可恢复的登录，请重新登录。");

                case ProviderRefreshOutcome.Invalid:
                    return new SignInActionResult(false, SignInSurfaceMode.Credentials, "登录状态已失效，请重新登录。");

                case ProviderRefreshOutcome.RefreshRequired:
                    return new SignInActionResult(false, SignInSurfaceMode.ReconnectRequired, "仍然无法确认登录状态，请检查网络后重试。");

                default:
                    return new SignInActionResult(false, SignInSurfaceMode.ReconnectRequired, "暂时无法恢复登录状态。");
            }
        }
        catch
        {
            return new SignInActionResult(false, SignInSurfaceMode.ReconnectRequired, "暂时无法恢复登录状态，请稍后再试。");
        }
    }

    private async Task<SignInActionResult> ClearSessionAsync()
    {
        if (_productionRuntime is null)
        {
            return new SignInActionResult(false, SignInSurfaceMode.Credentials, "当前客户端配置不可用。");
        }

        try
        {
            var result = await _productionRuntime.AuthCoordinator.SignOutAsync();
            var message = result == ProviderSignOutOutcome.LocalOnlyRemoteUnconfirmed
                ? "已退出本机登录；服务器撤销暂未确认。重新登录前请确认网络正常。"
                : "已退出此设备账号，请重新登录。";
            return new SignInActionResult(false, SignInSurfaceMode.Credentials, message);
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
            ShowConfigurationUnavailable("本机安全会话无法完整清除，已停止继续显示原账号工作区。");
        }
    }

    private void ShowAuthenticatedWorkspace()
    {
        if (_productionRuntime is null)
        {
            ShowConfigurationUnavailable();
            return;
        }

        ShowMainWindow(new MainWindow(
            _app,
            new MainWindowViewModel(_productionRuntime.CreateTeachingWorkspace()),
            SignOutCurrentWorkspaceAsync,
            ConsumePendingAgentNavigation(),
            _productionRuntime.CreateDiagnosticsArchive));
    }

    private void ShowSignIn(SignInSurfaceMode mode, string statusText)
    {
        var previousMain = _mainWindow;
        var previousSignIn = _signInWindow;
        _mainWindow = null;

        _signInWindow = new SignInWindow(
            _app,
            mode,
            statusText,
            SignInAsync,
            RetrySessionAsync,
            ClearSessionAsync);
        _signInWindow.Present();

        previousMain?.Close();
        previousSignIn?.Close();
    }

    private void ShowConfigurationUnavailable(string? message = null)
    {
        ShowMainWindow(new MainWindow(
            _app,
            MainWindowViewModel.CreateConfigurationUnavailable(
                string.IsNullOrWhiteSpace(message)
                    ? "当前客户端配置或安全会话不可用，教学数据未加载。如需体验原型界面请使用 --prototype 启动。"
                    : message)));
    }

    private void ShowMainWindow(MainWindow next)
    {
        var previousMain = _mainWindow;
        var previousSignIn = _signInWindow;
        _signInWindow = null;
        _mainWindow = next;
        next.Present();

        previousMain?.Close();
        previousSignIn?.Close();
    }

    private AgentNavigationRequest? ConsumePendingAgentNavigation()
    {
        var nav = _pendingAgentNavigation;
        _pendingAgentNavigation = null;
        return nav;
    }

    private async Task RunGuiSmokeAsync(MainWindowViewModel viewModel, MainWindow window)
    {
        try
        {
            var mainThreadId = Environment.CurrentManagedThreadId;
            if (viewModel.Students.Count != 1_000)
            {
                throw new InvalidOperationException($"Expected 1000 prototype students, got {viewModel.Students.Count}.");
            }

            await Task.Delay(120);
            if (Environment.CurrentManagedThreadId != mainThreadId)
            {
                throw new InvalidOperationException(
                    $"GLib SynchronizationContext did not marshal back to main thread (expected {mainThreadId}, got {Environment.CurrentManagedThreadId}).");
            }

            var targetStudent = viewModel.Students[5];
            window.StudentsView.FilterStudentsProgrammatically(targetStudent.DisplayName);
            if (viewModel.Students.Count == 0 || viewModel.Students.Count >= 1_000)
            {
                throw new InvalidOperationException($"Student filter failed (filtered count={viewModel.Students.Count}).");
            }

            window.StudentsView.FilterStudentsProgrammatically(string.Empty);
            if (viewModel.Students.Count != 1_000)
            {
                throw new InvalidOperationException("Student filter reset failed.");
            }

            // Verify xueqing://student/<uuid> navigation selects exact student
            var targetUuid = PrototypeDraftIdentity.ResolveStudentUuid(targetStudent.Id);
            var navReq = LinuxProtocolActivation.ParseUri($"xueqing://student/{targetUuid:D}")
                ?? throw new InvalidOperationException("Failed to parse valid xueqing://student URI.");
            await window.ApplyAgentNavigationAsync(navReq);
            if (window.CurrentSurfaceName != "students" || viewModel.SelectedStudent?.Id != targetStudent.Id)
            {
                throw new InvalidOperationException(
                    $"xueqing://student routing failed (surface={window.CurrentSurfaceName}, selected={viewModel.SelectedStudent?.Id}).");
            }

            // Verify unknown UUID fails closed to Students with null selection
            var unknownNav = new AgentNavigationRequest(AgentNavigationTargetKind.Student, Guid.NewGuid());
            await window.ApplyAgentNavigationAsync(unknownNav);
            if (window.CurrentSurfaceName != "students" || viewModel.SelectedStudent is not null)
            {
                throw new InvalidOperationException("Unknown student UUID did not fail closed.");
            }

            _smokePassed = true;
        }
        catch (Exception ex)
        {
            _smokeFailure = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            _app.Quit();
        }
    }

    public void Dispose()
    {
        _productionRuntime?.Dispose();
    }
}
