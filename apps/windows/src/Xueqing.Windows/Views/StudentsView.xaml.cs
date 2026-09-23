using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Agent;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Windows.Views;

public sealed partial class StudentsView : UserControl
{
    private WindowLayoutMode _layoutMode = WindowLayoutMode.Compact;

    public StudentsView()
    {
        InitializeComponent();
    }

    private void FocusStudentSearch_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        SearchBox.SelectAll();
        args.Handled = true;
    }

    private void BackToStudentList_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_layoutMode == WindowLayoutMode.Expanded ||
            DetailPane.Visibility != Visibility.Visible)
        {
            return;
        }

        ShowListOnly();
        StudentList.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    public void ApplyLayout(WindowLayoutMode mode)
    {
        _layoutMode = mode;

        if (mode == WindowLayoutMode.Expanded)
        {
            Grid.SetColumn(ListPane, 0);
            Grid.SetColumn(DetailPane, 1);
            ListColumn.Width = new GridLength(320);
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

    private async void ObservationDraftBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox ||
            DataContext is not MainWindowViewModel viewModel ||
            !viewModel.SupportsObservationCapture ||
            string.Equals(
                textBox.Text,
                viewModel.ObservationDraftText,
                StringComparison.Ordinal))
        {
            return;
        }

        await viewModel.UpdateObservationDraftAsync(textBox.Text);
    }

    private async void ObservationSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.SubmitObservationDraftAsync();
        }
    }

    private async void RefreshAuthoritativeStudents_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.RefreshAuthoritativeStudentsAsync();
        }
    }

    private async void LoadCaseHistory_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.LoadSelectedCaseHistoryAsync();
        }
    }

    private void RescheduleFocusActionButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        ApplyActionProgressionVisibility(button);
        if (button.Tag is LearningFocusDisplayItem item && item.CaseId != Guid.Empty)
        {
            AutomationProperties.SetAutomationId(
                button,
                AgentAutomationIds.FocusReschedule(item.CaseId));
        }
    }

    private void VerifyFocusActionButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        ApplyActionProgressionVisibility(button);
        if (button.Tag is LearningFocusDisplayItem item && item.CaseId != Guid.Empty)
        {
            AutomationProperties.SetAutomationId(
                button,
                AgentAutomationIds.FocusVerify(item.CaseId));
        }
    }

    private void ApplyActionProgressionVisibility(Button button)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            button.Visibility = viewModel.SupportsActionProgression
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void CaseLifecycleButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.DataContext is LearningCaseHistoryDisplayItem item &&
            DataContext is MainWindowViewModel viewModel)
        {
            button.Visibility =
                viewModel.SupportsCaseLifecycle && item.CanRunLifecycleAction
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (item.CaseId != Guid.Empty)
            {
                AutomationProperties.SetAutomationId(
                    button,
                    AgentAutomationIds.CaseLifecycle(item.CaseId));
            }
        }
    }

    private async void CaseLifecycle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not LearningCaseHistoryDisplayItem item ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var lookup = await viewModel.FindCaseLifecycleAsync(item);
        await CaseLifecycleDialogFlow.ShowAsync(
            XamlRoot,
            viewModel,
            lookup);
    }

    private async void RescheduleFocusAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not LearningFocusDisplayItem item ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var lookup = await viewModel.FindFocusActionProgressionAsync(item);
        await ActionProgressionDialogFlow.ShowRescheduleAsync(
            XamlRoot,
            viewModel,
            lookup);
    }

    private async void VerifyFocusAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not LearningFocusDisplayItem item ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var lookup = await viewModel.FindFocusActionProgressionAsync(item);
        await ActionProgressionDialogFlow.ShowVerificationAsync(
            XamlRoot,
            viewModel,
            lookup);
    }


    private void CreateLearningCaseButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is StudentRecentObservation observation &&
            observation.ObservationId != Guid.Empty)
        {
            AutomationProperties.SetAutomationId(
                button,
                AgentAutomationIds.ObservationCreateLearningCase(observation.ObservationId));
        }
    }

    private async void CreateLearningCase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not StudentRecentObservation observation ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var recovery = await viewModel.FindPendingLearningCaseForObservationAsync(observation);
        if (!recovery.IsAvailable)
        {
            var unavailableDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "暂时无法创建学情问题",
                Content = "无法安全读取本机待确认操作。为避免重复创建，本次不会提交。",
                CloseButtonText = "知道了",
                DefaultButton = ContentDialogButton.Close,
            };
            await unavailableDialog.ShowAsync();
            return;
        }

        var pendingRequest = recovery.Request;
        var titleBox = new TextBox
        {
            Header = "关注问题",
            PlaceholderText = "例如：概括题压缩仍不稳定",
            MaxLength = 500,
            Text = pendingRequest?.Title ?? string.Empty,
        };
        var actionBox = new TextBox
        {
            Header = "下一步行动",
            PlaceholderText = "写下下一次要做的具体教学动作",
            MaxLength = 1000,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 88,
            Text = pendingRequest?.PrimaryActionText ?? string.Empty,
        };
        var duePicker = new CalendarDatePicker
        {
            Header = "行动日期",
            PlaceholderText = "可选",
            Date = pendingRequest?.PrimaryActionDueOn is { } pendingDueOn
                ? ToCalendarDate(pendingDueOn)
                : null,
        };
        var statusText = new TextBlock
        {
            Opacity = 0.70,
            TextWrapping = TextWrapping.Wrap,
            Text = pendingRequest is null
                ? string.Empty
                : "发现上次尚未确认的提交。请重新确认结果。",
        };
        var content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 420,
        };
        content.Children.Add(titleBox);
        content.Children.Add(actionBox);
        content.Children.Add(duePicker);
        content.Children.Add(statusText);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "形成学情问题",
            Content = content,
            PrimaryButtonText = pendingRequest is null ? "创建" : "重新确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        var operationId = pendingRequest?.OperationId ?? Guid.NewGuid();
        string? submittedTitle = pendingRequest?.Title;
        string? submittedAction = pendingRequest?.PrimaryActionText;
        DateOnly? submittedDueOn = pendingRequest?.PrimaryActionDueOn;
        var intentFrozen = pendingRequest is not null;

        if (intentFrozen)
        {
            titleBox.IsEnabled = false;
            actionBox.IsEnabled = false;
            duePicker.IsEnabled = false;
        }

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;

            try
            {
                if (!intentFrozen)
                {
                    var title = titleBox.Text.Trim();
                    var primaryAction = actionBox.Text.Trim();
                    if (title.Length is < 1 or > 500)
                    {
                        statusText.Text = "请填写 1–500 个字符的关注问题。";
                        titleBox.Focus(FocusState.Programmatic);
                        return;
                    }

                    if (primaryAction.Length is < 1 or > 1000)
                    {
                        statusText.Text = "请填写 1–1000 个字符的下一步行动。";
                        actionBox.Focus(FocusState.Programmatic);
                        return;
                    }

                    submittedTitle = title;
                    submittedAction = primaryAction;
                    submittedDueOn = duePicker.Date is { } selectedDate
                        ? DateOnly.FromDateTime(selectedDate.DateTime)
                        : null;
                    intentFrozen = true;
                    titleBox.IsEnabled = false;
                    actionBox.IsEnabled = false;
                    duePicker.IsEnabled = false;
                }

                dialog.IsPrimaryButtonEnabled = false;
                statusText.Text = pendingRequest is null
                    ? "正在提交…"
                    : "正在重新确认上次提交…";

                var result = await viewModel.CreateLearningCaseFromObservationAsync(
                    observation,
                    submittedTitle!,
                    submittedAction!,
                    submittedDueOn,
                    operationId);

                if (result.IsSuccess)
                {
                    statusText.Text = string.Empty;
                    args.Cancel = false;
                    return;
                }

                statusText.Text = CreateLearningCaseFailureText(result);
                if (result.Failure?.Kind is
                    CreateLearningCaseFailureKind.ResultUnknown or
                    CreateLearningCaseFailureKind.Transient)
                {
                    dialog.PrimaryButtonText = "重试确认";
                    dialog.IsPrimaryButtonEnabled = true;
                }
                else
                {
                    dialog.PrimaryButtonText = "无法提交";
                    dialog.IsPrimaryButtonEnabled = false;
                }
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private static DateTimeOffset ToCalendarDate(DateOnly date)
    {
        var localDate = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
    }

    private static string CreateLearningCaseFailureText(CreateLearningCaseResult result) =>
        result.Failure?.Kind switch
        {
            CreateLearningCaseFailureKind.AuthenticationRequired =>
                "登录状态已失效。已停止提交，请重新登录后再试。",
            CreateLearningCaseFailureKind.AuthorityChanged =>
                "当前教学权限或任课关系已经变化。已刷新工作区，请重新确认后再试。",
            CreateLearningCaseFailureKind.Validation =>
                "当前输入未通过服务器校验。请取消后重新发起。",
            CreateLearningCaseFailureKind.OperationConflict =>
                "这次操作标识发生冲突。为避免重复创建，已停止提交。",
            CreateLearningCaseFailureKind.LocalDurabilityFailure =>
                "本机恢复状态无法安全更新。为避免重复或覆盖，本次停止继续处理。",
            CreateLearningCaseFailureKind.ResultUnknown =>
                "提交结果尚未确认。请使用“重试确认”继续同一次操作，不要重复创建。",
            CreateLearningCaseFailureKind.Transient =>
                "服务暂时不可用。可以使用“重试确认”继续同一次操作。",
            _ =>
                "服务器返回无法验证。为避免重复创建，已停止提交。",
        };

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
