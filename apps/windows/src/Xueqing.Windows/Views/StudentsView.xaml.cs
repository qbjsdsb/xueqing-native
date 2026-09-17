using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.ViewModels;

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
            Grid.SetColumn(ListPane, 0);
            Grid.SetColumn(DetailPane, 1);
            ListColumn.Width = new GridLength(360);
            DetailColumn.Width = new GridLength(1, GridUnitType.Star);
            ListPane.Visibility = Visibility.Visible;
            DetailPane.Visibility = Visibility.Visible;
            BackToListButton.Visibility = Visibility.Collapsed;
            return;
        }

        ListColumn.Width = new GridLength(1, GridUnitType.Star);
        DetailColumn.Width = new GridLength(0);
        ShowListOnly();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.FilterStudents(SearchBox.Text);
        }
    }

    private void StudentList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StudentSummary student && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectedStudent = student;
        }

        if (_layoutMode != WindowLayoutMode.Expanded && e.ClickedItem is not null)
        {
            ShowDetailOnly();
        }
    }

    private async void StudentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.IsAuthoritativeStudentWorkspace &&
            viewModel.SelectedTeachingContexts.Count == 1)
        {
            await viewModel.LoadSelectedTeachingContextAsync();
        }
    }

    private async void SubjectContextBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.SelectedTeachingContexts.Count > 1 &&
            viewModel.SelectedTeachingContext is not null)
        {
            await viewModel.LoadSelectedTeachingContextAsync();
        }
    }

    private async void RefreshAuthoritativeStudents_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.RefreshAuthoritativeStudentsAsync();
        }
    }

    private void StudentList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_layoutMode == WindowLayoutMode.Expanded || StudentList.SelectedItem is null)
        {
            return;
        }

        if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            ShowDetailOnly();
            e.Handled = true;
        }
    }

    private void BackToListButton_Click(object sender, RoutedEventArgs e)
    {
        ShowListOnly();
        StudentList.Focus(FocusState.Programmatic);
    }

    private void ShowListOnly()
    {
        Grid.SetColumn(ListPane, 0);
        Grid.SetColumn(DetailPane, 1);
        ListPane.Visibility = Visibility.Visible;
        DetailPane.Visibility = Visibility.Collapsed;
        BackToListButton.Visibility = Visibility.Collapsed;
    }

    private void ShowDetailOnly()
    {
        Grid.SetColumn(DetailPane, 0);
        ListPane.Visibility = Visibility.Collapsed;
        DetailPane.Visibility = Visibility.Visible;
        BackToListButton.Visibility = Visibility.Visible;
    }
}
