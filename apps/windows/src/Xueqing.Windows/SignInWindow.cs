using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Xueqing.Windows;

internal enum SignInSurfaceMode
{
    Credentials,
    ReconnectRequired,
}

internal sealed record SignInActionResult(
    bool Authenticated,
    SignInSurfaceMode Mode,
    string StatusText);

internal sealed class SignInWindow : Window
{
    private readonly Func<string, string, Task<SignInActionResult>> _signIn;
    private readonly Func<Task<SignInActionResult>> _retry;
    private readonly Func<Task<SignInActionResult>> _clearSession;
    private readonly StackPanel _credentialsPanel;
    private readonly StackPanel _reconnectPanel;
    private readonly TextBox _emailBox;
    private readonly PasswordBox _passwordBox;
    private readonly Button _signInButton;
    private readonly Button _retryButton;
    private readonly Button _clearSessionButton;
    private readonly TextBlock _statusText;

    public SignInWindow(
        SignInSurfaceMode mode,
        string statusText,
        Func<string, string, Task<SignInActionResult>> signIn,
        Func<Task<SignInActionResult>> retry,
        Func<Task<SignInActionResult>> clearSession)
    {
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        _retry = retry ?? throw new ArgumentNullException(nameof(retry));
        _clearSession = clearSession ?? throw new ArgumentNullException(nameof(clearSession));

        Title = "学情 · 登录";

        _emailBox = new TextBox
        {
            Header = "邮箱",
            PlaceholderText = "name@example.com",
        };
        AutomationProperties.SetAutomationId(_emailBox, "AuthEmail");
        AutomationProperties.SetName(_emailBox, "登录邮箱");

        _passwordBox = new PasswordBox
        {
            Header = "密码",
            PasswordRevealMode = PasswordRevealMode.Peek,
        };
        AutomationProperties.SetAutomationId(_passwordBox, "AuthPassword");
        AutomationProperties.SetName(_passwordBox, "登录密码");

        _signInButton = new Button
        {
            Content = "登录",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(_signInButton, "AuthSubmit");
        _signInButton.Click += SignInButton_Click;

        _retryButton = new Button
        {
            Content = "重试连接",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(_retryButton, "AuthRetry");
        _retryButton.Click += RetryButton_Click;

        _clearSessionButton = new Button
        {
            Content = "退出此设备账号",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(_clearSessionButton, "AuthClearSession");
        _clearSessionButton.Click += ClearSessionButton_Click;

        _statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
        };
        AutomationProperties.SetAutomationId(_statusText, "AuthStatus");

        _credentialsPanel = new StackPanel
        {
            Spacing = 12,
        };
        _credentialsPanel.Children.Add(_emailBox);
        _credentialsPanel.Children.Add(_passwordBox);
        _credentialsPanel.Children.Add(_signInButton);

        _reconnectPanel = new StackPanel
        {
            Spacing = 10,
        };
        _reconnectPanel.Children.Add(_retryButton);
        _reconnectPanel.Children.Add(_clearSessionButton);

        var panel = new StackPanel
        {
            MaxWidth = 420,
            Spacing = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(new TextBlock
        {
            Text = "学情",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "登录后进入你的教学工作区。",
            Opacity = 0.72,
        });
        panel.Children.Add(_statusText);
        panel.Children.Add(_credentialsPanel);
        panel.Children.Add(_reconnectPanel);

        var root = new Grid
        {
            Padding = new Thickness(32),
        };
        root.Children.Add(panel);
        Content = root;

        ApplyMode(mode, statusText);
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_emailBox.Text) ||
            string.IsNullOrWhiteSpace(_passwordBox.Password))
        {
            _statusText.Text = "请输入邮箱和密码。";
            return;
        }

        await RunAsync(() => _signIn(_emailBox.Text.Trim(), _passwordBox.Password));
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_retry);

    private async void ClearSessionButton_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_clearSession);

    private async Task RunAsync(Func<Task<SignInActionResult>> action)
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
        _statusText.Text = statusText;
        _credentialsPanel.Visibility =
            mode == SignInSurfaceMode.Credentials
                ? Visibility.Visible
                : Visibility.Collapsed;
        _reconnectPanel.Visibility =
            mode == SignInSurfaceMode.ReconnectRequired
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (mode == SignInSurfaceMode.Credentials)
        {
            _passwordBox.Password = string.Empty;
            _emailBox.Focus(FocusState.Programmatic);
        }
    }

    private void SetBusy(bool busy)
    {
        _emailBox.IsEnabled = !busy;
        _passwordBox.IsEnabled = !busy;
        _signInButton.IsEnabled = !busy;
        _retryButton.IsEnabled = !busy;
        _clearSessionButton.IsEnabled = !busy;
        if (busy)
        {
            _statusText.Text = "正在处理登录状态…";
        }
    }
}
