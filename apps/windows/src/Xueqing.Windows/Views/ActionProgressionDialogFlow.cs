using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Windows.Views;

internal static class ActionProgressionDialogFlow
{
    private sealed record OutcomeOption(
        VerificationOutcome Outcome,
        string Label);

    private static readonly OutcomeOption[] OutcomeOptions =
    [
        new(VerificationOutcome.Met, "达到预期"),
        new(VerificationOutcome.PartiallyMet, "部分达到"),
        new(VerificationOutcome.NotMet, "未达到"),
        new(VerificationOutcome.Uncertain, "暂不确定"),
    ];

    public static async Task ShowRescheduleAsync(
        XamlRoot xamlRoot,
        MainWindowViewModel viewModel,
        ActionProgressionRecoveryLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(lookup);

        if (!lookup.IsAvailable || lookup.Target is null)
        {
            await ShowUnavailableAsync(
                xamlRoot,
                "暂时无法安全读取这条行动的权威状态。已停止操作，请刷新后再试。");
            return;
        }

        if (lookup.PendingIntent is not null &&
            lookup.PendingIntent is not ReschedulePrimaryActionRecoveryIntent)
        {
            await ShowUnavailableAsync(
                xamlRoot,
                "这条行动已有另一项尚未确认的正式操作。请先在“今日”的待确认区域继续原操作，不能创建第二个操作。");
            return;
        }

        var target = lookup.Target;
        var pending = lookup.PendingIntent as ReschedulePrimaryActionRecoveryIntent;
        var picker = new CalendarDatePicker
        {
            Header = "新的行动日期",
            PlaceholderText = "留空表示待安排",
            Date = pending?.Request.NewDueOn is { } pendingDue
                ? ToCalendarDate(pendingDue)
                : target.CurrentDueOn is { } currentDue
                    ? ToCalendarDate(currentDue)
                    : null,
        };
        var status = StatusText();
        var content = new StackPanel
        {
            Spacing = 10,
            MinWidth = 400,
        };
        content.Children.Add(new TextBlock
        {
            Text = target.PrimaryActionText,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(picker);
        content.Children.Add(status);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "调整行动日期",
            Content = content,
            PrimaryButtonText = pending is null ? "确认改期" : "重新确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        var operationId = pending?.Request.OperationId ?? Guid.NewGuid();
        DateOnly? frozenDueOn = pending?.Request.NewDueOn;
        var frozen = pending is not null;
        ActionProgressionRecoveryIntent? retryIntent = pending;
        if (frozen)
        {
            picker.IsEnabled = false;
            status.Text = "发现上次尚未确认的改期。此次只会继续原 operation_id，不会创建新的操作。";
        }

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;
            try
            {
                if (!frozen)
                {
                    frozenDueOn = picker.Date is { } selectedDate
                        ? DateOnly.FromDateTime(selectedDate.DateTime)
                        : null;
                    if (frozenDueOn == target.CurrentDueOn)
                    {
                        status.Text = "新的日期必须与当前日期不同。";
                        return;
                    }

                    frozen = true;
                    picker.IsEnabled = false;
                    retryIntent = new ReschedulePrimaryActionRecoveryIntent(
                        target.CreateRescheduleRequest(operationId, frozenDueOn));
                }

                dialog.IsPrimaryButtonEnabled = false;
                status.Text = retryIntent == pending && pending is not null
                    ? "正在重新确认原改期操作…"
                    : "正在提交改期…";

                ActionProgressionRetryResult outcome;
                if (pending is null && dialog.PrimaryButtonText == "确认改期")
                {
                    var result = await viewModel.ReschedulePrimaryActionAsync(
                        target,
                        frozenDueOn,
                        operationId);
                    outcome = new ActionProgressionRetryResult(
                        result.IsSuccess,
                        result.Failure);
                }
                else
                {
                    outcome = await viewModel.RetryPendingActionProgressionAsync(
                        retryIntent!);
                }

                if (outcome.IsSuccess)
                {
                    args.Cancel = false;
                    return;
                }

                status.Text = FailureText(outcome.Failure);
                if (RequiresSameOperationRetry(outcome.Failure))
                {
                    dialog.PrimaryButtonText = "重试确认";
                    dialog.IsPrimaryButtonEnabled = true;
                }
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    public static async Task ShowVerificationAsync(
        XamlRoot xamlRoot,
        MainWindowViewModel viewModel,
        ActionProgressionRecoveryLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(lookup);

        if (!lookup.IsAvailable || lookup.Target is null)
        {
            await ShowUnavailableAsync(
                xamlRoot,
                "暂时无法安全读取这条行动的权威状态。已停止操作，请刷新后再试。");
            return;
        }

        if (lookup.PendingIntent is not null &&
            lookup.PendingIntent is not VerificationAndNextActionRecoveryIntent)
        {
            await ShowUnavailableAsync(
                xamlRoot,
                "这条行动已有另一项尚未确认的正式操作。请先在“今日”的待确认区域继续原操作，不能创建第二个操作。");
            return;
        }

        var target = lookup.Target;
        var pending = lookup.PendingIntent as VerificationAndNextActionRecoveryIntent;
        var pendingRequest = pending?.Request;

        var outcomeBox = new ComboBox
        {
            Header = "验证结果",
            ItemsSource = OutcomeOptions,
            DisplayMemberPath = nameof(OutcomeOption.Label),
            SelectedItem = pendingRequest is null
                ? OutcomeOptions[1]
                : OutcomeOptions.First(option => option.Outcome == pendingRequest.Outcome),
        };
        var summaryBox = new TextBox
        {
            Header = "验证记录",
            MaxLength = 2000,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            Text = pendingRequest?.VerificationSummary ?? string.Empty,
        };
        var nextActionBox = new TextBox
        {
            Header = "下一步行动",
            MaxLength = 1000,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            Text = pendingRequest?.NextActionText ?? string.Empty,
        };
        var duePicker = new CalendarDatePicker
        {
            Header = "下一步日期",
            PlaceholderText = "可选",
            Date = pendingRequest?.NextActionDueOn is { } due
                ? ToCalendarDate(due)
                : null,
        };
        var status = StatusText();
        var content = new StackPanel
        {
            Spacing = 10,
            MinWidth = 420,
        };
        content.Children.Add(new TextBlock
        {
            Text = target.PrimaryActionText,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(outcomeBox);
        content.Children.Add(summaryBox);
        content.Children.Add(nextActionBox);
        content.Children.Add(duePicker);
        content.Children.Add(status);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "验证并安排下一步",
            Content = content,
            PrimaryButtonText = pending is null ? "记录验证" : "重新确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        var operationId = pendingRequest?.OperationId ?? Guid.NewGuid();
        var frozen = pending is not null;
        ActionProgressionRecoveryIntent? retryIntent = pending;
        if (frozen)
        {
            FreezeVerificationInputs(
                outcomeBox,
                summaryBox,
                nextActionBox,
                duePicker);
            status.Text = "发现上次尚未确认的验证。此次只会继续原 operation_id，不会改变原验证内容。";
        }

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;
            try
            {
                VerificationOutcome frozenOutcome;
                string frozenSummary;
                string frozenNextAction;
                DateOnly? frozenDueOn;

                if (!frozen)
                {
                    if (outcomeBox.SelectedItem is not OutcomeOption selectedOutcome)
                    {
                        status.Text = "请选择验证结果。";
                        return;
                    }

                    frozenSummary = summaryBox.Text.Trim();
                    frozenNextAction = nextActionBox.Text.Trim();
                    if (frozenSummary.Length is < 1 or > 2000)
                    {
                        status.Text = "请填写 1–2000 个字符的验证记录。";
                        summaryBox.Focus(FocusState.Programmatic);
                        return;
                    }

                    if (frozenNextAction.Length is < 1 or > 1000)
                    {
                        status.Text = "请填写 1–1000 个字符的下一步行动。";
                        nextActionBox.Focus(FocusState.Programmatic);
                        return;
                    }

                    frozenOutcome = selectedOutcome.Outcome;
                    frozenDueOn = duePicker.Date is { } selectedDate
                        ? DateOnly.FromDateTime(selectedDate.DateTime)
                        : null;
                    retryIntent = new VerificationAndNextActionRecoveryIntent(
                        target.CreateVerificationRequest(
                            operationId,
                            frozenOutcome,
                            frozenSummary,
                            frozenNextAction,
                            frozenDueOn));
                    frozen = true;
                    FreezeVerificationInputs(
                        outcomeBox,
                        summaryBox,
                        nextActionBox,
                        duePicker);
                }
                else
                {
                    var request = ((VerificationAndNextActionRecoveryIntent)retryIntent!).Request;
                    frozenOutcome = request.Outcome;
                    frozenSummary = request.VerificationSummary;
                    frozenNextAction = request.NextActionText;
                    frozenDueOn = request.NextActionDueOn;
                }

                dialog.IsPrimaryButtonEnabled = false;
                status.Text = pending is not null
                    ? "正在重新确认原验证操作…"
                    : "正在提交验证…";

                ActionProgressionRetryResult result;
                if (pending is null && dialog.PrimaryButtonText == "记录验证")
                {
                    var submitted = await viewModel.RecordVerificationAndNextActionAsync(
                        target,
                        frozenOutcome,
                        frozenSummary,
                        frozenNextAction,
                        frozenDueOn,
                        operationId);
                    result = new ActionProgressionRetryResult(
                        submitted.IsSuccess,
                        submitted.Failure);
                }
                else
                {
                    result = await viewModel.RetryPendingActionProgressionAsync(
                        retryIntent!);
                }

                if (result.IsSuccess)
                {
                    args.Cancel = false;
                    return;
                }

                status.Text = FailureText(result.Failure);
                if (RequiresSameOperationRetry(result.Failure))
                {
                    dialog.PrimaryButtonText = "重试确认";
                    dialog.IsPrimaryButtonEnabled = true;
                }
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    public static async Task ShowPendingRecoveryAsync(
        XamlRoot xamlRoot,
        MainWindowViewModel viewModel,
        ActionProgressionRecoveryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(intent);

        var detail = intent switch
        {
            ReschedulePrimaryActionRecoveryIntent reschedule =>
                reschedule.Request.NewDueOn is { } dueOn
                    ? $"原操作：将行动改期到 {dueOn:yyyy年MM月dd日}"
                    : "原操作：将行动改为待安排日期",
            VerificationAndNextActionRecoveryIntent verification =>
                $"原验证：{verification.Request.VerificationSummary}\n原下一步：{verification.Request.NextActionText}",
            _ => "原操作内容无法识别。",
        };

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "重新确认原操作",
            Content = new TextBlock
            {
                Text = detail + "\n\n系统会继续使用原 operation_id，不会创建替代操作。",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "重新确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var result = await viewModel.RetryPendingActionProgressionAsync(intent);
        var resultDialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = result.IsSuccess ? "操作已确认" : "仍未完成确认",
            Content = result.IsSuccess
                ? "服务器已经确认原操作，今日行动与当前关注已重新读取。"
                : FailureText(result.Failure),
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close,
        };
        await resultDialog.ShowAsync();
    }

    public static string FailureText(ActionProgressionFailure? failure) =>
        failure?.Kind switch
        {
            ActionProgressionFailureKind.AuthenticationRequired =>
                "登录状态已失效。已停止提交，请重新登录后处理。",
            ActionProgressionFailureKind.AuthorityChanged =>
                "任课关系或教学权限已经变化。工作区已重新验证，不会创建替代操作。",
            ActionProgressionFailureKind.VersionConflict =>
                "这条行动已经发生变化。权威数据已刷新，请基于最新状态重新决定。",
            ActionProgressionFailureKind.Validation =>
                "服务器明确拒绝了这次操作内容。原恢复记录已隔离，不会继续重试。",
            ActionProgressionFailureKind.OperationConflict =>
                "服务器检测到 operation_id 与不同内容冲突。已停止继续提交。",
            ActionProgressionFailureKind.LocalDurabilityFailure =>
                "本机无法安全保存或隔离恢复状态。为避免重复副作用，本次停止继续处理。",
            ActionProgressionFailureKind.ResultUnknown =>
                "服务器结果仍无法确认。原操作已安全保留，只能继续同一个 operation_id。",
            ActionProgressionFailureKind.Transient =>
                "服务暂时不可用。原操作仍安全保留，可稍后继续确认。",
            ActionProgressionFailureKind.InvalidResponse =>
                "服务器返回无法验证。原操作仍安全保留，只能继续同一个 operation_id。",
            _ =>
                "操作结果无法确认。为避免重复副作用，已停止创建新的操作。",
        };

    private static bool RequiresSameOperationRetry(ActionProgressionFailure? failure) =>
        failure?.Kind is
            ActionProgressionFailureKind.ResultUnknown or
            ActionProgressionFailureKind.Transient or
            ActionProgressionFailureKind.InvalidResponse;

    private static TextBlock StatusText() =>
        new()
        {
            Opacity = 0.68,
            TextWrapping = TextWrapping.Wrap,
        };

    private static void FreezeVerificationInputs(
        ComboBox outcome,
        TextBox summary,
        TextBox nextAction,
        CalendarDatePicker due)
    {
        outcome.IsEnabled = false;
        summary.IsEnabled = false;
        nextAction.IsEnabled = false;
        due.IsEnabled = false;
    }

    private static DateTimeOffset ToCalendarDate(DateOnly date)
    {
        var localDate = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
    }

    private static async Task ShowUnavailableAsync(
        XamlRoot xamlRoot,
        string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "暂时无法继续",
            Content = message,
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }
}
