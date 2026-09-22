using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Windows;

public sealed partial class MainWindow : Window
{
    private readonly Func<Task>? _signOutAction;
    private bool _organizationWorkspace;
    private bool _signingOut;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
        : this(new MainWindowViewModel())
    {
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        Func<Task>? signOutAction = null)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _signOutAction = signOutAction;
        InitializeComponent();
        Title = "学情";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        RootGrid.DataContext = ViewModel;
        SignOutNav.Visibility =
            _signOutAction is null
                ? Visibility.Collapsed
                : Visibility.Visible;
        ApplyOrganizationWorkspaceAccess();
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        Shell.IsPaneOpen = !Shell.IsPaneOpen;
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout(RootGrid.ActualWidth);
        ApplyWorkspace(isOrganization: false);
        await ViewModel.InitializeAsync();
        ApplyOrganizationWorkspaceAccess();
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyLayout(e.NewSize.Width);
    }

    private void ApplyLayout(double width)
    {
        var mode = WindowLayoutPolicy.Resolve(width);
        Shell.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
        StudentsView.ApplyLayout(mode);
        OrganizationManagementView.ApplyLayout(mode);
    }

    private void PersonalWorkspace_Click(object sender, RoutedEventArgs e)
    {
        ApplyWorkspace(isOrganization: false);
    }

    private async void OrganizationWorkspace_Click(object sender, RoutedEventArgs e)
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
        OrganizationWorkspaceMenuItem.Visibility = ViewModel.CanUseOrganizationWorkspace
            ? Visibility.Visible
            : Visibility.Collapsed;

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

        WorkspaceSwitcherButton.Content = isOrganization
            ? "机构工作区"
            : "个人教学";

        TodayNav.Visibility = isOrganization ? Visibility.Collapsed : Visibility.Visible;
        StudentsNav.Visibility = isOrganization ? Visibility.Collapsed : Visibility.Visible;
        LearningNav.Visibility = isOrganization ? Visibility.Collapsed : Visibility.Visible;
        OrganizationManagementNav.Visibility =
            isOrganization && ViewModel.CanUseOrganizationWorkspace
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (isOrganization)
        {
            Shell.SelectedItem = OrganizationManagementNav;
            ShowSurface("organization-management");
        }
        else
        {
            Shell.SelectedItem = TodayNav;
            ShowSurface("today");
        }

        Shell.IsPaneOpen = false;
    }

    private async void Shell_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.SelectedItemContainer?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        if (tag == "sign-out")
        {
            await SignOutAsync();
            return;
        }

        if (_organizationWorkspace && tag != "organization-management")
        {
            return;
        }

        if (!_organizationWorkspace && tag == "organization-management")
        {
            return;
        }

        ShowSurface(tag);

        if (tag == "learning" && ViewModel.IsAuthoritativeStudentWorkspace)
        {
            await ViewModel.LoadSelectedTeachingContextAsync();
        }
    }

    private async Task SignOutAsync()
    {
        if (_signOutAction is null || _signingOut)
        {
            return;
        }

        _signingOut = true;
        SignOutNav.IsEnabled = false;
        try
        {
            await _signOutAction();
        }
        finally
        {
            _signingOut = false;
        }
    }

    private void ShowSurface(string tag)
    {
        TodayView.Visibility = tag == "today" ? Visibility.Visible : Visibility.Collapsed;
        StudentsView.Visibility = tag == "students" ? Visibility.Visible : Visibility.Collapsed;
        LearningView.Visibility = tag == "learning" ? Visibility.Visible : Visibility.Collapsed;
        OrganizationManagementView.Visibility = tag == "organization-management" ? Visibility.Visible : Visibility.Collapsed;
    }
}
