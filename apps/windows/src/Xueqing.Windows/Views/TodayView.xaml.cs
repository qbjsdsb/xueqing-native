using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Windows.Views;

public sealed partial class TodayView : UserControl
{
    public TodayView()
    {
        InitializeComponent();
    }

    private async void RetryPendingLearningCase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not CreateLearningCaseRequest request ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var summary = new StackPanel
        {
            Spacing = 8,
            MinWidth = 420,
        };
        summary.Children.Add(new TextBlock
        {
            Text = request.Title,
            TextWrapping = TextWrapping.Wrap,
        });
        summary.Children.Add(new TextBlock
        {
            Text = request.PrimaryActionText,
            TextWrapping = TextWrapping.Wrap,
        });
        summary.Children.Add(new TextBlock
        {
            Opacity = 0.62,
            Text = request.PrimaryActionDueOn is { } dueOn
                ? $"行动日期：{dueOn:yyyy年MM月dd日}"
                : "行动日期：待安排",
        });
        summary.Children.Add(new TextBlock
        {
            Opacity = 0.66,
            Text = "这不是重新创建。系统会使用原 operation_id 重新确认同一次提交的权威结果。",
            TextWrapping = TextWrapping.Wrap,
        });

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "重新确认学情问题提交",
            Content = summary,
            PrimaryButtonText = "重新确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var result = await viewModel.RetryPendingLearningCaseAsync(request);
        var resultDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = result.IsSuccess ? "提交已确认" : "仍未完成确认",
            Content = result.IsSuccess
                ? "服务器已经确认这次学情问题提交，当前关注与今日行动已刷新。"
                : FailureText(result),
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close,
        };
        await resultDialog.ShowAsync();
    }

    private void ActionProgressionButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            DataContext is MainWindowViewModel viewModel)
        {
            button.Visibility = viewModel.SupportsActionProgression
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private async void RescheduleAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not TodayActionItem item ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var lookup = await viewModel.FindTodayActionProgressionAsync(item);
        await ActionProgressionDialogFlow.ShowRescheduleAsync(
            XamlRoot,
            viewModel,
            lookup);
    }

    private async void VerifyAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not TodayActionItem item ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var lookup = await viewModel.FindTodayActionProgressionAsync(item);
        await ActionProgressionDialogFlow.ShowVerificationAsync(
            XamlRoot,
            viewModel,
            lookup);
    }

    private async void RetryPendingActionProgression_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not ActionProgressionRecoveryIntent intent ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await ActionProgressionDialogFlow.ShowPendingRecoveryAsync(
            XamlRoot,
            viewModel,
            intent);
    }

    private static string FailureText(CreateLearningCaseResult result) =>
        result.Failure?.Kind switch
        {
            CreateLearningCaseFailureKind.AuthenticationRequired =>
                "登录状态已失效。工作区已重新验证，请重新登录后处理。",
            CreateLearningCaseFailureKind.AuthorityChanged =>
                "任课关系或教学权限已经变化。工作区已刷新，服务器未接受新的副作用。",
            CreateLearningCaseFailureKind.Validation =>
                "服务器明确拒绝了原提交内容；本机已将这条恢复记录隔离，不会继续把它当成可重试提交。",
            CreateLearningCaseFailureKind.OperationConflict =>
                "服务器检测到 operation_id 与不同内容冲突；本机已停止继续提交这条恢复记录。",
            CreateLearningCaseFailureKind.LocalDurabilityFailure =>
                "本机无法安全更新恢复状态。为避免重复或覆盖，本次停止继续处理。",
            CreateLearningCaseFailureKind.ResultUnknown =>
                "服务器结果仍无法确认。该操作会继续保留在“今日”，下次仍使用同一个 operation_id 重新确认。",
            CreateLearningCaseFailureKind.Transient =>
                "服务暂时不可用。该操作仍安全保留，可稍后继续确认。",
            _ =>
                "服务器返回无法验证。为避免制造新的操作，本次没有创建替代提交。",
        };
}
