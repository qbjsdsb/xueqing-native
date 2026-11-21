using System.ComponentModel;
using Xueqing.Linux.Infrastructure.Agent;
using Xueqing.Linux.Infrastructure.Security;
using Xueqing.Linux.UI;
using Xueqing.Windows.Core.Agent;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Linux.Views;

internal sealed class MainWindow
{
    private readonly Func<Task>? _signOutAction;
    private readonly Func<byte[]>? _createDiagnosticsArchive;
    private AgentNavigationRequest? _initialAgentNavigation;

    private readonly Adw.ApplicationWindow _window;
    private readonly Adw.ToastOverlay _toastOverlay;
    private readonly Adw.ViewStack _viewStack;
    private readonly Adw.ViewStackPage _todayPage;
    private readonly Adw.ViewStackPage _studentsPage;
    private readonly Adw.ViewStackPage _learningPage;
    private readonly Adw.ViewStackPage _orgPage;

    private readonly Gtk.MenuButton _workspaceButton;
    private readonly Gtk.Button _orgWorkspaceMenuItem;
    private readonly Gtk.Button _diagnosticsMenuItem;
    private readonly Gtk.Button _signOutMenuItem;

    private readonly TodayView _todayView;
    private readonly StudentsView _studentsView;
    private readonly LearningView _learningView;
    private readonly OrganizationManagementView _organizationView;

    private bool _organizationWorkspace;
    private bool _signingOut;
    private bool _exportingDiagnostics;
    private int _colorSchemeIndex;
    private bool _initialized;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(
        Adw.Application application,
        MainWindowViewModel viewModel,
        Func<Task>? signOutAction = null,
        AgentNavigationRequest? initialAgentNavigation = null,
        Func<byte[]>? createDiagnosticsArchive = null)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _signOutAction = signOutAction;
        _initialAgentNavigation = initialAgentNavigation;
        _createDiagnosticsArchive = createDiagnosticsArchive;

        _window = Adw.ApplicationWindow.New(application);
        _window.SetTitle(ViewModel.IsPrototypeMode ? "学情（测试原型模式）" : "学情");
        _window.SetDefaultSize(1320, 840);

        _todayView = new TodayView(ViewModel, _window);
        _studentsView = new StudentsView(ViewModel, _window);
        _learningView = new LearningView(ViewModel);
        _organizationView = new OrganizationManagementView(ViewModel, _window);

        _viewStack = Adw.ViewStack.New();
        _todayPage = _viewStack.AddTitledWithIcon(_todayView.Widget, "today", "今日", "view-list-symbolic");
        _studentsPage = _viewStack.AddTitledWithIcon(_studentsView.Widget, "students", "学生", "system-users-symbolic");
        _learningPage = _viewStack.AddTitledWithIcon(_learningView.Widget, "learning", "学情", "document-edit-symbolic");
        _orgPage = _viewStack.AddTitledWithIcon(_organizationView.Widget, "organization-management", "机构管理", "emblem-system-symbolic");

        var switcher = Adw.ViewSwitcher.New();
        switcher.SetStack(_viewStack);
        switcher.SetPolicy(Adw.ViewSwitcherPolicy.Wide);
        GtkUiHelpers.SetAutomationId(switcher, "ShellNavigation");

        var headerBar = Adw.HeaderBar.New();
        headerBar.SetTitleWidget(switcher);
        GtkUiHelpers.SetAutomationId(headerBar, "AppTitleBar");

        // Workspace switcher popover
        _workspaceButton = Gtk.MenuButton.New();
        _workspaceButton.SetLabel("个人教学");
        GtkUiHelpers.SetAutomationId(_workspaceButton, "WorkspaceSwitcher");

        var wsPopover = Gtk.Popover.New();
        var wsMenuBox = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        wsMenuBox.SetMarginStart(6);
        wsMenuBox.SetMarginEnd(6);
        wsMenuBox.SetMarginTop(6);
        wsMenuBox.SetMarginBottom(6);

        var personalWsBtn = CreateMenuButton("个人教学", "PersonalWorkspaceMenuItem");
        personalWsBtn.OnClicked += (_, _) =>
        {
            wsPopover.Popdown();
            ApplyWorkspace(isOrganization: false);
        };

        _orgWorkspaceMenuItem = CreateMenuButton("机构工作区", "OrganizationWorkspaceMenuItem");
        _orgWorkspaceMenuItem.OnClicked += async (_, _) =>
        {
            wsPopover.Popdown();
            await EnterOrganizationWorkspaceAsync();
        };

        wsMenuBox.Append(personalWsBtn);
        wsMenuBox.Append(_orgWorkspaceMenuItem);
        wsPopover.SetChild(wsMenuBox);
        _workspaceButton.SetPopover(wsPopover);
        headerBar.PackStart(_workspaceButton);

        // App menu popover (Theme, Security self-check, Diagnostics export, Sign out)
        var appMenuButton = Gtk.MenuButton.New();
        appMenuButton.SetIconName("open-menu-symbolic");
        var appPopover = Gtk.Popover.New();
        var appMenuBox = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        appMenuBox.SetMarginStart(6);
        appMenuBox.SetMarginEnd(6);
        appMenuBox.SetMarginTop(6);
        appMenuBox.SetMarginBottom(6);

        var themeMenuItem = CreateMenuButton("切换浅色 / 深色主题", "ThemeToggleMenuItem");
        themeMenuItem.OnClicked += (_, _) =>
        {
            appPopover.Popdown();
            CycleColorScheme();
        };
        appMenuBox.Append(themeMenuItem);

        var securityCheckMenuItem = CreateMenuButton("验证本机加密安全链", "SecurityCheckMenuItem");
        securityCheckMenuItem.OnClicked += async (_, _) =>
        {
            appPopover.Popdown();
            await RunSecurityCheckAsync();
        };
        appMenuBox.Append(securityCheckMenuItem);

        _diagnosticsMenuItem = CreateMenuButton("导出诊断包…", "DiagnosticsNavigation");
        _diagnosticsMenuItem.SetVisible(_createDiagnosticsArchive is not null);
        _diagnosticsMenuItem.OnClicked += async (_, _) =>
        {
            appPopover.Popdown();
            await ExportDiagnosticsArchiveAsync();
        };
        appMenuBox.Append(_diagnosticsMenuItem);

        _signOutMenuItem = CreateMenuButton("退出登录", "SignOutNavigation");
        _signOutMenuItem.AddCssClass("destructive-action");
        _signOutMenuItem.SetVisible(_signOutAction is not null);
        _signOutMenuItem.OnClicked += async (_, _) =>
        {
            appPopover.Popdown();
            await SignOutAsync();
        };
        appMenuBox.Append(_signOutMenuItem);

        appPopover.SetChild(appMenuBox);
        appMenuButton.SetPopover(appPopover);
        headerBar.PackEnd(appMenuButton);

        var toolbarView = Adw.ToolbarView.New();
        toolbarView.AddTopBar(headerBar);
        toolbarView.SetContent(_viewStack);

        _toastOverlay = Adw.ToastOverlay.New();
        _toastOverlay.SetChild(toolbarView);
        _window.SetContent(_toastOverlay);

        _viewStack.OnNotify += async (_, args) =>
        {
            if (args.Pspec.GetName() == "visible-child-name" &&
                string.Equals(_viewStack.GetVisibleChildName(), "learning", StringComparison.Ordinal) &&
                ViewModel.IsAuthoritativeStudentWorkspace)
            {
                await ViewModel.LoadSelectedTeachingContextAsync();
            }
        };

        _window.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "default-width")
            {
                ApplyLayout();
            }
        };

        GLib.Functions.TimeoutAdd(0, 300, () =>
        {
            if (!_window.GetVisible())
            {
                return false;
            }

            ApplyLayout();
            return true;
        });

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        ApplyOrganizationWorkspaceAccess();
        ApplyWorkspace(isOrganization: false);
        ApplyLayout();
    }

    public Adw.ApplicationWindow Window => _window;

    public StudentsView StudentsView => _studentsView;

    public string CurrentSurfaceName => _viewStack.GetVisibleChildName() ?? string.Empty;

    public void Present()
    {
        _window.Present();
        if (!_initialized)
        {
            _initialized = true;
            _ = InitializeWorkspaceAsync();
        }
    }

    public void Close() => _window.Close();

    public async Task ApplyAgentNavigationAsync(AgentNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApplyWorkspace(isOrganization: false);

        switch (request.Kind)
        {
            case AgentNavigationTargetKind.Today:
                _viewStack.SetVisibleChildName("today");
                return;

            case AgentNavigationTargetKind.Student
                when request.EntityId is { } studentId:
                if (ViewModel.IsAuthoritativeStudentWorkspace &&
                    ViewModel.TrySelectAuthoritativeStudent(studentId))
                {
                    _viewStack.SetVisibleChildName("students");
                    _studentsView.SyncSelectedStudentHighlight(revealDetailIfCollapsed: true);
                    if (ViewModel.SelectedTeachingContexts.Count == 1 &&
                        ViewModel.SelectedTeachingContext is not null)
                    {
                        await ViewModel.LoadSelectedTeachingContextAsync();
                    }

                    return;
                }

                if (ViewModel.IsPrototypeMode)
                {
                    ViewModel.FilterStudents(string.Empty);
                    var protoStudent = LinuxProtocolActivation.FindPrototypeStudentByUuid(studentId, ViewModel.Students);
                    if (protoStudent is not null)
                    {
                        ViewModel.SelectedStudent = protoStudent;
                        _viewStack.SetVisibleChildName("students");
                        _studentsView.SyncSelectedStudentHighlight(revealDetailIfCollapsed: true);
                        ShowToast($"已通过 xueqing:// 打开学生：{protoStudent.DisplayName}");
                        return;
                    }
                }

                FailClosedToStudentsSurface();
                return;

            case AgentNavigationTargetKind.LearningCase
                when request.EntityId is { } caseId &&
                     ViewModel.TrySelectAuthoritativeLearningCase(caseId):
                _viewStack.SetVisibleChildName("learning");
                await ViewModel.LoadSelectedTeachingContextAsync();
                return;

            default:
                FailClosedToStudentsSurface();
                return;
        }
    }

    private void FailClosedToStudentsSurface()
    {
        // Unknown, unauthorized, or ambiguous application-owned IDs fail closed
        // to the ordinary Students surface without manufacturing authority/context.
        ViewModel.SelectedStudent = null;
        _viewStack.SetVisibleChildName("students");
        _studentsView.SyncSelectedStudentHighlight();
    }

    private async Task InitializeWorkspaceAsync()
    {
        ApplyLayout();
        await ViewModel.InitializeAsync();
        ApplyOrganizationWorkspaceAccess();

        if (_initialAgentNavigation is { } pending)
        {
            _initialAgentNavigation = null;
            await ApplyAgentNavigationAsync(pending);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CanUseOrganizationWorkspace))
        {
            ApplyOrganizationWorkspaceAccess();
        }
    }

    private async Task EnterOrganizationWorkspaceAsync()
    {
        if (ViewModel.IsOrganizationManagementAuthoritative)
        {
            await ViewModel.RefreshOrganizationManagementAsync();
            ApplyOrganizationWorkspaceAccess();
        }

        if (ViewModel.CanUseOrganizationWorkspace)
        {
            ApplyWorkspace(isOrganization: true);
        }
    }

    private void ApplyOrganizationWorkspaceAccess()
    {
        _orgWorkspaceMenuItem.SetVisible(ViewModel.CanUseOrganizationWorkspace);
        if (_organizationWorkspace && !ViewModel.CanUseOrganizationWorkspace)
        {
            ApplyWorkspace(isOrganization: false);
        }
    }

    private void ApplyWorkspace(bool isOrganization)
    {
        if (isOrganization && !ViewModel.CanUseOrganizationWorkspace)
        {
            isOrganization = false;
        }

        _organizationWorkspace = isOrganization;
        _workspaceButton.SetLabel(isOrganization ? "机构工作区" : "个人教学");

        _todayPage.SetVisible(!isOrganization);
        _studentsPage.SetVisible(!isOrganization);
        _learningPage.SetVisible(!isOrganization);
        _orgPage.SetVisible(isOrganization && ViewModel.CanUseOrganizationWorkspace);

        _viewStack.SetVisibleChildName(isOrganization ? "organization-management" : "today");
    }

    private void ApplyLayout()
    {
        var width = _window.GetWidth();
        if (width <= 0)
        {
            width = 1320;
        }

        var mode = WindowLayoutPolicy.Resolve(width);
        _studentsView.ApplyLayout(mode);
        _organizationView.ApplyLayout(mode);
    }

    private void CycleColorScheme()
    {
        _colorSchemeIndex = (_colorSchemeIndex + 1) % 3;
        var (scheme, label) = _colorSchemeIndex switch
        {
            1 => (Adw.ColorScheme.ForceDark, "已切换为深色主题"),
            2 => (Adw.ColorScheme.ForceLight, "已切换为浅色主题"),
            _ => (Adw.ColorScheme.Default, "已恢复跟随系统主题"),
        };
        Adw.StyleManager.GetDefault().SetColorScheme(scheme);
        ShowToast(label);
    }

    private async Task RunSecurityCheckAsync()
    {
        ShowToast("正在验证 libsecret + SQLite3MC 2.4.0 + boot_id…");
        try
        {
            var report = await Task.Run(() => LinuxSecuritySelfTest.RunAsync());
            await GtkUiHelpers.ShowMessageDialogAsync(
                _window,
                "本机安全链验证通过",
                $"SQLite3MC：{report.Sqlite3McVersion}\nboot_id：{report.BootSessionId}\n\n" +
                string.Join("\n", report.Checks));
        }
        catch (Exception ex)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(
                _window,
                "本机安全链验证失败（Fail-closed）",
                ex.Message);
        }
    }

    private async Task ExportDiagnosticsArchiveAsync()
    {
        if (_createDiagnosticsArchive is null || _exportingDiagnostics)
        {
            return;
        }

        _exportingDiagnostics = true;
        _diagnosticsMenuItem.SetSensitive(false);
        try
        {
            var fileDialog = Gtk.FileDialog.New();
            fileDialog.SetTitle("导出隐私安全诊断包");
            fileDialog.SetInitialName($"Xueqing-Linux-Diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip");

            Gio.File? chosenFile;
            try
            {
                chosenFile = await fileDialog.SaveAsync(_window);
            }
            catch
            {
                // User cancelled the native file dialog.
                return;
            }

            var path = chosenFile?.GetPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var archiveBytes = _createDiagnosticsArchive();
            await File.WriteAllBytesAsync(path, archiveBytes);
            ShowToast($"诊断包已导出：{Path.GetFileName(path)}");
        }
        finally
        {
            _exportingDiagnostics = false;
            _diagnosticsMenuItem.SetSensitive(true);
        }
    }

    private async Task SignOutAsync()
    {
        if (_signOutAction is null || _signingOut)
        {
            return;
        }

        _signingOut = true;
        _signOutMenuItem.SetSensitive(false);
        try
        {
            await _signOutAction();
        }
        finally
        {
            _signingOut = false;
        }
    }

    private void ShowToast(string message)
    {
        var toast = Adw.Toast.New(message);
        toast.SetTimeout(3);
        _toastOverlay.AddToast(toast);
    }

    private static Gtk.Button CreateMenuButton(string label, string automationId)
    {
        var button = Gtk.Button.NewWithLabel(label);
        button.AddCssClass("flat");
        button.SetHalign(Gtk.Align.Fill);
        GtkUiHelpers.SetAutomationId(button, automationId);
        return button;
    }
}
