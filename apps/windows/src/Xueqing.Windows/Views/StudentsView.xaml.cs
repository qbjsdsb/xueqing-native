using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xueqing.Windows.Core.Layout;

namespace Xueqing.Windows.Views;

public sealed partial class StudentsView : UserControl
{
    private WindowLayoutMode _layoutMode = WindowLayoutMode.Compact;

    public StudentsView()
    {
        InitializeComponent();
    }

    public void ApplyLayout(WindowLayoutMode mode)
    {
        _layoutMode = mode;

        if (mode == WindowLayoutMode.Expanded)
        {
            ListColumn.Width = new GridLength(360);
            DetailColumn.Width = new GridLength(1, GridUnitType.Star);
            ListPane.Visibility = Visibility.Visible;
            DetailPane.Visibility = Visibility.Visible;
            BackToListButton.Visibility = Visibility.Collapsed;
            return;
        }

        ListColumn.Width = new GridLength(1, GridUnitType.Star);
        DetailColumn.Width = new GridLength(1, GridUnitType.Star);
        ShowListOnly();
    }

    private void StudentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_layoutMode != WindowLayoutMode.Expanded && StudentList.SelectedItem is not null)
        {
            ShowDetailOnly();
        }
    }

    private void BackToListButton_Click(object sender, RoutedEventArgs e)
    {
        ShowListOnly();
        StudentList.Focus(FocusState.Programmatic);
    }

    private void ShowListOnly()
    {
        ListPane.Visibility = Visibility.Visible;
        DetailPane.Visibility = Visibility.Collapsed;
        Grid.SetColumn(ListPane, 0);
    }

    private void ShowDetailOnly()
    {
        ListPane.Visibility = Visibility.Collapsed;
        DetailPane.Visibility = Visibility.Visible;
        Grid.SetColumn(DetailPane, 0);
        BackToListButton.Visibility = Visibility.Visible;
    }
}
