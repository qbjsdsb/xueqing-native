using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Windows;

public sealed partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = "学情 Native · WinUI Architecture Spike";
        StudentList.SelectedItem = ViewModel.SelectedStudent;
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var mode = WindowLayoutPolicy.Resolve(e.NewSize.Width);

        Shell.PaneDisplayMode = mode == WindowLayoutMode.Compact
            ? NavigationViewPaneDisplayMode.LeftCompact
            : NavigationViewPaneDisplayMode.Left;

        var showDetail = mode == WindowLayoutMode.Expanded;
        DetailPanel.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        ListColumn.Width = showDetail
            ? new GridLength(360)
            : new GridLength(1, GridUnitType.Star);
        DetailColumn.Width = showDetail
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
    }

    private void StudentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StudentList.SelectedItem is StudentSummary student)
        {
            ViewModel.SelectedStudent = student;
        }
    }
}
