using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Windows;

public sealed partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = "学情 Native";
        RootGrid.DataContext = ViewModel;
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout(RootGrid.ActualWidth);
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyLayout(e.NewSize.Width);
    }

    private void ApplyLayout(double width)
    {
        var mode = WindowLayoutPolicy.Resolve(width);

        Shell.PaneDisplayMode = mode == WindowLayoutMode.Compact
            ? NavigationViewPaneDisplayMode.LeftCompact
            : NavigationViewPaneDisplayMode.Left;

        StudentsView.ApplyLayout(mode);
        OrganizationManagementView.ApplyLayout(mode);
    }

    private void Shell_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.SelectedItemContainer?.Tag?.ToString();

        TodayView.Visibility = tag == "today" ? Visibility.Visible : Visibility.Collapsed;
        StudentsView.Visibility = tag == "students" ? Visibility.Visible : Visibility.Collapsed;
        LearningView.Visibility = tag == "learning" ? Visibility.Visible : Visibility.Collapsed;
        OrganizationManagementView.Visibility = tag == "organization-management" ? Visibility.Visible : Visibility.Collapsed;
    }
}
