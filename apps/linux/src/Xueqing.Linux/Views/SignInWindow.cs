using Xueqing.Linux.UI;

namespace Xueqing.Linux.Views;

internal enum SignInSurfaceMode
{
    Credentials,
    ReconnectRequired,
}

internal sealed record SignInActionResult(
    bool Authenticated,
    SignInSurfaceMode Mode,
    string StatusText);

internal sealed class SignInWindow
{
    private readonly Func<string, string, Task<SignInActionResult>> _signIn;
    private readonly Func<Task<SignInActionResult>> _retry;
    private readonly Func<Task<SignInActionResult>> _clearSession;

    private readonly Adw.ApplicationWindow _window;
    private readonly Gtk.Box _credentialsBox;
    private readonly Gtk.Box _reconnectBox;
    private readonly Adw.EntryRow _emailRow;
    private readonly Adw.PasswordEntryRow _passwordRow;
    private readonly Gtk.Button _signInButton;
    private readonly Gtk.Button _retryButton;
    private readonly Gtk.Button _clearSessionButton;
    private readonly Gtk.Label _statusLabel;

    public SignInWindow(
        Adw.Application application,
        SignInSurfaceMode mode,
        string statusText,
        Func<string, string, Task<SignInActionResult>> signIn,
        Func<Task<SignInActionResult>> retry,
        Func<Task<SignInActionResult>> clearSession)
    {
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        _retry = retry ?? throw new ArgumentNullException(nameof(retry));
        _clearSession = clearSession ?? throw new ArgumentNullException(nameof(clearSession));

        _window = Adw.ApplicationWindow.New(application);
        _window.SetTitle("学情 · 登录");
        _window.SetDefaultSize(520, 560);

        var headerBar = Adw.HeaderBar.New();
        var toolbarView = Adw.ToolbarView.New();
        toolbarView.AddTopBar(headerBar);

        var rootBox = Gtk.Box.New(Gtk.Orientation.Vertical, 18);
        rootBox.SetMarginStart(28);
        rootBox.SetMarginEnd(28);
        rootBox.SetMarginTop(36);
        rootBox.SetMarginBottom(36);
        rootBox.SetValign(Gtk.Align.Center);

        var title = GtkUiHelpers.CreateHeading("学情", "title-1");
        var subtitle = GtkUiHelpers.CreateDimLabel("登录后进入你的教学工作区。");
        _statusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        GtkUiHelpers.SetAutomationId(_statusLabel, "AuthStatus");

        rootBox.Append(title);
        rootBox.Append(subtitle);
        rootBox.Append(_statusLabel);

        _emailRow = Adw.EntryRow.New();
        _emailRow.SetTitle("邮箱");
        GtkUiHelpers.SetAutomationId(_emailRow, "AuthEmail");

        _passwordRow = Adw.PasswordEntryRow.New();
        _passwordRow.SetTitle("密码");
        GtkUiHelpers.SetAutomationId(_passwordRow, "AuthPassword");

        var credentialsGroup = Adw.PreferencesGroup.New();
        credentialsGroup.Add(_emailRow);
        credentialsGroup.Add(_passwordRow);

        _signInButton = Gtk.Button.NewWithLabel("登录");
        _signInButton.AddCssClass("suggested-action");
        _signInButton.AddCssClass("pill");
        GtkUiHelpers.SetAutomationId(_signInButton, "AuthSubmit");
        _signInButton.OnClicked += async (_, _) => await HandleSignInAsync();

        _emailRow.OnEntryActivated += (_, _) => _passwordRow.GrabFocus();
        _passwordRow.OnEntryActivated += async (_, _) => await HandleSignInAsync();

        _credentialsBox = Gtk.Box.New(Gtk.Orientation.Vertical, 14);
        _credentialsBox.Append(credentialsGroup);
        _credentialsBox.Append(_signInButton);

        _retryButton = Gtk.Button.NewWithLabel("重试连接");
        _retryButton.AddCssClass("suggested-action");
        _retryButton.AddCssClass("pill");
        GtkUiHelpers.SetAutomationId(_retryButton, "AuthRetry");
        _retryButton.OnClicked += async (_, _) => await RunActionAsync(_retry);

        _clearSessionButton = Gtk.Button.NewWithLabel("退出此设备账号");
        _clearSessionButton.AddCssClass("destructive-action");
        _clearSessionButton.AddCssClass("pill");
        GtkUiHelpers.SetAutomationId(_clearSessionButton, "AuthClearSession");
        _clearSessionButton.OnClicked += async (_, _) => await RunActionAsync(_clearSession);

        _reconnectBox = Gtk.Box.New(Gtk.Orientation.Vertical, 10);
        _reconnectBox.Append(_retryButton);
        _reconnectBox.Append(_clearSessionButton);

        rootBox.Append(_credentialsBox);
        rootBox.Append(_reconnectBox);

        var clamp = Adw.Clamp.New();
        clamp.SetMaximumSize(420);
        clamp.SetChild(rootBox);

        toolbarView.SetContent(clamp);
        _window.SetContent(toolbarView);

        ApplyMode(mode, statusText);
    }

    public Adw.ApplicationWindow Window => _window;

    public void Present() => _window.Present();

    public void Close() => _window.Close();

    private async Task HandleSignInAsync()
    {
        var email = _emailRow.GetText()?.Trim() ?? string.Empty;
        var password = _passwordRow.GetText() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _statusLabel.SetText("请输入邮箱和密码。");
            return;
        }

        await RunActionAsync(() => _signIn(email, password));
    }

    private async Task RunActionAsync(Func<Task<SignInActionResult>> action)
    {
        SetBusy(true);
        try
        {
            var result = await action();
            if (!result.Authenticated)
            {
                ApplyMode(result.Mode, result.StatusText);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyMode(SignInSurfaceMode mode, string statusText)
    {
        _statusLabel.SetText(statusText);
        var isCredentials = mode == SignInSurfaceMode.Credentials;
        _credentialsBox.SetVisible(isCredentials);
        _reconnectBox.SetVisible(!isCredentials);

        if (isCredentials)
        {
            _passwordRow.SetText(string.Empty);
            _emailRow.GrabFocus();
        }
    }

    private void SetBusy(bool busy)
    {
        _emailRow.SetSensitive(!busy);
        _passwordRow.SetSensitive(!busy);
        _signInButton.SetSensitive(!busy);
        _retryButton.SetSensitive(!busy);
        _clearSessionButton.SetSensitive(!busy);
        if (busy)
        {
            _statusLabel.SetText("正在处理登录状态…");
        }
    }
}
