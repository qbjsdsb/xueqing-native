using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Windows.Views;

internal static class CaseLifecycleDialogFlow
{
    public static async Task ShowAsync(
        XamlRoot xamlRoot,
        MainWindowViewModel viewModel,
        CaseLifecycleRecoveryLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(lookup);

        if (!lookup.IsAvailable || lookup.Target is null)
        {
            await ShowMessageAsync(
                xamlRoot,
                "操作不可用",
                "当前 Case 已不是可写的权威责任目标，或本机恢复状态暂时不可读取。请刷新后再试。");
            return;
        }

        if (lookup.PendingIntent is not null)
        {
            await ShowPendingRecoveryAsync(
                xamlRoot,
                viewModel,
                lookup.PendingIntent);
            return;
        }

        var target = lookup.Target;
        switch (target.CaseState)
        {
            case LearningCaseState.New:
                await ShowTransitionAsync(
                    xamlRoot,
                    viewModel,
                    target,
                    LearningCaseState.Confirmed,
                    "确认 Case",
                    "确认",
                    "确认后，这个 Case 将进入正式跟进。");
                break;
            case LearningCaseState.Confirmed:
                await ShowTransitionAsync(
                    xamlRoot,
                    viewModel,
                    target,
                    LearningCaseState.Intervening,
                    "开始干预",
                    "开始干预",
                    "开始干预会记录一次正式生命周期变更，当前主行动保持不变。");
                break;
            case LearningCaseState.Intervening:
                await ShowTransitionAsync(
                    xamlRoot,
                    viewModel,
                    target,
                    LearningCaseState.PendingVerification,
                    "进入待验证",
                    "进入待验证",
                    "进入待验证表示当前干预已经完成，下一步需要用验证事实判断效果。");
                break;
            case LearningCaseState.PendingVerification:
                await ShowTransitionAsync(
                    xamlRoot,
                    viewModel,
                    target,
                    LearningCaseState.Stable,
                    "标记稳定",
                    "标记稳定",
                    "稳定是显式教学判断，不会自动关闭 Case；当前主行动仍保留到关闭为止。");
                break;
            case LearningCaseState.Stable:
                await ShowCloseAsync(xamlRoot, viewModel, target);
                break;
            case LearningCaseState.Closed:
                await ShowReopenAsync(xamlRoot, viewModel, target);
                break;
            default:
                await ShowMessageAsync(
                    xamlRoot,
                    "操作不可用",
                    "当前 Case 状态没有可执行的生命周期动作。");
                break;
        }
    }

    public static async Task ShowPendingRecoveryAsync(
        XamlRoot xamlRoot,
        MainWindowViewModel viewModel,
        CaseLifecycleRecoveryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var confirm = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "重新确认 Case 操作",
            Content = new TextBlock
            {
                Text = RecoverySummary(intent) +
                    "\n\n这不是重新执行一个新操作。系统会使用原 operation_id 重新确认同一次正式命令的权威结果。",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
            },
            PrimaryButtonText = "重新确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var result = await viewModel.RetryPendingCaseLifecycleAsync(intent);
        await ShowResultAsync(
            xamlRoot,
            result.IsSuccess,
            result.Failure);
    }

    private static async Task ShowTransitionAsync(
        XamlRoot xamlRoot,
        MainWindowViewModel viewModel,
        CaseLifecycleTarget target,
        LearningCaseState targetState,
        string title,
        string primaryText,
        string explanation)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = $"{target.CaseTitle}\n\n{explanation}\n\n这是正式状态命令，不提供装饰性的“撤销”。",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
            },
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var result = await viewModel.TransitionLearningCaseAsync(
            target,
            targetState,
            Guid.NewGuid());
        await ShowResultAsync(xamlRoot, result.IsSuccess, result.Failure);
    }

    private static async Task ShowCloseAsync(
        XamlRoot xamlRoot,
        MainWindowViewModel viewModel,
        CaseLifecycleTarget target)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "关闭 Case",
            Content = new TextBlock
            {
                Text = $"{target.CaseTitle}\n\n关闭后，当前 pending primary Action 会被服务器原子取消；历史行动、验证与生命周期记录不会删除。\n\n如以后再次出现问题，应使用“重新打开”创建新的下一步行动。",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 540,
            },
            PrimaryButtonText = "关闭 Case",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var result = await viewModel.CloseLearningCaseAsync(
            target,
            Guid.NewGuid());
        await ShowResultAsync(xamlRoot, result.IsSuccess, result.Failure);
    }

    private static async Task ShowReopenAsync(
        XamlRoot xamlRoot,
        MainWindowViewModel viewModel,
        CaseLifecycleTarget target)
    {
        var actionBox = new TextBox
        {
            Header = "新的下一步行动",
            PlaceholderText = "例如：重新检查两道陌生材料中的限制条件",
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MaxLength = 1000,
        };
        var duePicker = new CalendarDatePicker
        {
            Header = "行动日期（可选）",
            PlaceholderText = "选择日期",
        };
        var content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 460,
        };
        content.Children.Add(new TextBlock
        {
            Text = target.CaseTitle,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = "重新打开不是撤销关闭。服务器会追加新的 reopen 事件，并创建一条新的 pending primary Action。",
            Opacity = 0.68,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(actionBox);
        content.Children.Add(duePicker);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "重新打开 Case",
            Content = content,
            PrimaryButtonText = "重新打开",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        actionBox.TextChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled =
                !string.IsNullOrWhiteSpace(actionBox.Text);
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        DateOnly? dueOn = duePicker.Date is { } selectedDate
            ? DateOnly.FromDateTime(selectedDate.DateTime)
            : null;
        var result = await viewModel.ReopenLearningCaseAsync(
            target,
            actionBox.Text,
            dueOn,
            Guid.NewGuid());
        await ShowResultAsync(xamlRoot, result.IsSuccess, result.Failure);
    }

    private static async Task ShowResultAsync(
        XamlRoot xamlRoot,
        bool isSuccess,
        CaseLifecycleFailure? failure)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = isSuccess ? "Case 已更新" : "Case 操作未确认",
            Content = isSuccess
                ? "服务器已经确认这次正式生命周期操作；Case 历史、当前关注和今日行动已重新读取。"
                : FailureText(failure),
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private static async Task ShowMessageAsync(
        XamlRoot xamlRoot,
        string title,
        string text)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = text,
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private static string RecoverySummary(CaseLifecycleRecoveryIntent intent) =>
        intent switch
        {
            TransitionCaseRecoveryIntent transition =>
                $"继续确认 Case 状态变更：{StateLabel(transition.Request.TargetState)}",
            CloseCaseRecoveryIntent =>
                "继续确认关闭 Case 的权威结果",
            ReopenCaseRecoveryIntent reopen =>
                $"继续确认重新打开 Case；原下一步行动：{reopen.Request.NewPrimaryActionText}",
            _ => "继续确认原 Case 生命周期操作",
        };

    private static string StateLabel(LearningCaseState state) => state switch
    {
        LearningCaseState.Confirmed => "已确认",
        LearningCaseState.Intervening => "跟进中",
        LearningCaseState.PendingVerification => "待验证",
        LearningCaseState.Stable => "稳定",
        LearningCaseState.Closed => "已关闭",
        _ => state.ToString(),
    };

    private static string FailureText(CaseLifecycleFailure? failure) =>
        failure?.Kind switch
        {
            CaseLifecycleFailureKind.AuthenticationRequired =>
                "登录状态已失效。工作区已重新验证，请重新登录后处理。",
            CaseLifecycleFailureKind.AuthorityChanged =>
                "任课关系或当前责任已经变化。历史仍可查看，但服务器没有接受新的生命周期副作用。",
            CaseLifecycleFailureKind.VersionConflict =>
                "这个 Case 已在其他位置更新。系统已重新读取权威状态，请根据最新状态重新判断。",
            CaseLifecycleFailureKind.InvalidTransition =>
                "服务器明确拒绝了这次状态变化。系统已刷新 Case，请根据当前状态选择下一步。",
            CaseLifecycleFailureKind.ServerInvariant =>
                "服务器发现 Case/主行动不满足生命周期不变量，已停止继续修改并重新读取。",
            CaseLifecycleFailureKind.Validation =>
                "这次正式操作内容不完整或不合法，本机恢复记录已停止继续提交。",
            CaseLifecycleFailureKind.OperationConflict =>
                "operation_id 已绑定到不同内容，本机已停止继续提交这条恢复记录。",
            CaseLifecycleFailureKind.LocalDurabilityFailure =>
                "本机无法先安全保存恢复意图，因此没有发送新的服务器副作用。",
            CaseLifecycleFailureKind.ResultUnknown or
            CaseLifecycleFailureKind.Transient or
            CaseLifecycleFailureKind.InvalidResponse =>
                "服务器结果暂时无法确认。原操作已安全保留在“今日”，下次必须使用同一个 operation_id 继续确认，不能重新创建一条新操作。",
            _ =>
                "服务器结果无法确认。为避免重复状态变更，本次没有创建替代操作。",
        };
}
