using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Windows;

public sealed partial class MainWindow : Window
{
    private bool _organizationWorkspace;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
        : this(new MainWindowViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        Title = "学情";
        RootGrid.DataContext = ViewModel;
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout(RootGrid.ActualWidth);
        ApplyWorkspace(isOrganization: false);
        await ViewModel.InitializeAsync();
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyLayout(e.NewSize.Width);
    }

    private void ApplyLayout(double width)
    {
        var mode = WindowLayoutPolicy.Resolve(width);

        // Keep top-level navigation compact by default and let NavigationView's
        // native pane toggle reveal labels on demand. Width still drives the
        // actual work-surface reflow through one centralized policy.
        Shell.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;

        StudentsView.ApplyLayout(mode);
        OrganizationManagementView.ApplyLayout(mode);
    }

    private void PersonalWorkspace_Click(object sender, RoutedEventArgs e)
    {
        ApplyWorkspace(isOrganization: false);
    }

    private void OrganizationWorkspace_Click(object sender, RoutedEventArgs e)
    {
        ApplyWorkspace(isOrganization: true);
    }

    private void ApplyWorkspace(bool isOrganization)
    {
        _organizationWorkspace = isOrganization;

        WorkspaceSwitcherButton.Content = isOrganization
            ? "机构工作区"
            : "个人教学";

        TodayNav.Visibility = isOrganization ? Visibility.Collapsed : Visibility.Visible;
        StudentsNav.Visibility = isOrganization ? Visibility.Collapsed : Visibility.Visible;
        LearningNav.Visibility = isOrganization ? Visibility.Collapsed : Visibility.Visible;
        OrganizationManagementNav.Visibility = isOrganization ? Visibility.Visible : Visibility.Collapsed;

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

    private void Shell_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.SelectedItemContainer?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        // A stale hidden item must never navigate across the active workspace.
        if (_organizationWorkspace && tag != "organization-management")
        {
            return;
        }

        if (!_organizationWorkspace && tag == "organization-management")
        {
            return;
        }

        ShowSurface(tag);
    }

    private void ShowSurface(string tag)
    {
        TodayView.Visibility = tag == "today" ? Visibility.Visible : Visibility.Collapsed;
        StudentsView.Visibility = tag == "students" ? Visibility.Visible : Visibility.Collapsed;
        LearningView.Visibility = tag == "learning" ? Visibility.Visible : Visibility.Collapsed;
        OrganizationManagementView.Visibility = tag == "organization-management" ? Visibility.Visible : Visibility.Collapsed;
    }
}
