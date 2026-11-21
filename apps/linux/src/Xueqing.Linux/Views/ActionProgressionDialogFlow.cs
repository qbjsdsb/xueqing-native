using Xueqing.Linux.UI;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Linux.Views;

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
        Gtk.Widget parent,
        MainWindowViewModel viewModel,
        ActionProgressionRecoveryLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(lookup);

        if (!lookup.IsAvailable || lookup.Target is null)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(
                parent,
                "暂时无法继续",
                "暂时无法安全读取这条行动的权威状态。已停止操作，请刷新后再试。");
            return;
        }

        if (lookup.PendingIntent is not null &&
            lookup.PendingIntent is not ReschedulePrimaryActionRecoveryIntent)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(
                parent,
                "暂时无法继续",
                "这条行动已有另一项尚未确认的正式操作。请先在“今日”的待确认区域继续原操作，不能创建第二个操作。");
            return;
        }

        var target = lookup.Target;
        var pending = lookup.PendingIntent as ReschedulePrimaryActionRecoveryIntent;
        var initialDate = pending is not null ? pending.Request.NewDueOn : target.CurrentDueOn;

        var dialog = Adw.Dialog.New();
        dialog.SetTitle("调整行动日期");
        dialog.SetContentWidth(440);

        var toolbar = Adw.ToolbarView.New();
        toolbar.AddTopBar(Adw.HeaderBar.New());

        var content = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
        content.SetMarginStart(20);
        content.SetMarginEnd(20);
        content.SetMarginTop(16);
        content.SetMarginBottom(20);

        content.Append(GtkUiHelpers.CreateHeading(target.PrimaryActionText, "heading"));
        var dateRow = GtkUiHelpers.CreateDateEntryRow("新的行动日期", initialDate, out var dateEntry, out var quickButtons);
        content.Append(dateRow);

        var statusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        content.Append(statusLabel);

        var buttonRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        buttonRow.SetHalign(Gtk.Align.End);
        var cancelBtn = Gtk.Button.NewWithLabel("取消");
        var primaryBtn = Gtk.Button.NewWithLabel(pending is null ? "确认改期" : "重新确认");
        primaryBtn.AddCssClass("suggested-action");
        buttonRow.Append(cancelBtn);
        buttonRow.Append(primaryBtn);
        content.Append(buttonRow);

        toolbar.SetContent(content);
        dialog.SetChild(toolbar);

        var operationId = pending?.Request.OperationId ?? Guid.NewGuid();
        DateOnly? frozenDueOn = pending?.Request.NewDueOn;
        var frozen = pending is not null;
        ActionProgressionRecoveryIntent? retryIntent = pending;
        var firstSubmit = pending is null;

        if (frozen)
        {
            dateEntry.SetSensitive(false);
            quickButtons.SetSensitive(false);
            statusLabel.SetText("发现上次尚未确认的改期。此次只会继续原 operation_id，不会创建新的操作。");
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.OnClosed += (_, _) => tcs.TrySetResult();
        cancelBtn.OnClicked += (_, _) => dialog.Close();

        primaryBtn.OnClicked += async (_, _) =>
        {
            if (!frozen)
            {
                if (!GtkUiHelpers.TryParseOptionalDate(dateEntry.GetText(), out var parsedDate, out var parseError))
                {
                    statusLabel.SetText(parseError!);
                    return;
                }

                if (parsedDate == target.CurrentDueOn)
                {
                    statusLabel.SetText("新的日期必须与当前日期不同。");
                    return;
                }

                frozenDueOn = parsedDate;
                frozen = true;
                dateEntry.SetSensitive(false);
                quickButtons.SetSensitive(false);
                retryIntent = new ReschedulePrimaryActionRecoveryIntent(
                    target.CreateRescheduleRequest(operationId, frozenDueOn));
            }

            primaryBtn.SetSensitive(false);
            cancelBtn.SetSensitive(false);
            statusLabel.SetText(firstSubmit ? "正在提交改期…" : "正在重新确认原改期操作…");

            try
            {
                ActionProgressionRetryResult outcome;
                if (firstSubmit)
                {
                    firstSubmit = false;
                    var result = await viewModel.ReschedulePrimaryActionAsync(
                        target,
                        frozenDueOn,
                        operationId);
                    outcome = new ActionProgressionRetryResult(result.IsSuccess, result.Failure);
                }
                else
                {
                    outcome = await viewModel.RetryPendingActionProgressionAsync(retryIntent!);
                }

                if (outcome.IsSuccess)
                {
                    dialog.ForceClose();
                    return;
                }

                statusLabel.SetText(FailureText(outcome.Failure));
                if (RequiresSameOperationRetry(outcome.Failure))
                {
                    primaryBtn.SetLabel("重试确认");
                    primaryBtn.SetSensitive(true);
                }
                else
                {
                    primaryBtn.SetLabel("无法提交");
                    primaryBtn.SetSensitive(false);
                }
            }
            finally
            {
                cancelBtn.SetSensitive(true);
            }
        };

        dialog.Present(parent);
        await tcs.Task;
    }

    public static async Task ShowVerificationAsync(
        Gtk.Widget parent,
        MainWindowViewModel viewModel,
        ActionProgressionRecoveryLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(lookup);

        if (!lookup.IsAvailable || lookup.Target is null)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(
                parent,
                "暂时无法继续",
                "暂时无法安全读取这条行动的权威状态。已停止操作，请刷新后再试。");
            return;
        }

        if (lookup.PendingIntent is not null &&
            lookup.PendingIntent is not VerificationAndNextActionRecoveryIntent)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(
                parent,
                "暂时无法继续",
                "这条行动已有另一项尚未确认的正式操作。请先在“今日”的待确认区域继续原操作，不能创建第二个操作。");
            return;
        }

        var target = lookup.Target;
        var pending = lookup.PendingIntent as VerificationAndNextActionRecoveryIntent;
        var pendingRequest = pending?.Request;

        var dialog = Adw.Dialog.New();
        dialog.SetTitle("验证并安排下一步");
        dialog.SetContentWidth(480);

        var toolbar = Adw.ToolbarView.New();
        toolbar.AddTopBar(Adw.HeaderBar.New());

        var content = Gtk.Box.New(Gtk.Orientation.Vertical, 10);
        content.SetMarginStart(20);
        content.SetMarginEnd(20);
        content.SetMarginTop(16);
        content.SetMarginBottom(20);

        content.Append(GtkUiHelpers.CreateHeading(target.PrimaryActionText, "heading"));

        content.Append(GtkUiHelpers.CreateHeading("验证结果", "heading"));
        var outcomeLabels = OutcomeOptions.Select(o => o.Label).ToArray();
        var outcomeDropDown = Gtk.DropDown.NewFromStrings(outcomeLabels);
        var initialOutcomeIndex = pendingRequest is null
            ? 1u
            : (uint)Math.Max(0, Array.FindIndex(OutcomeOptions, o => o.Outcome == pendingRequest.Outcome));
        outcomeDropDown.SetSelected(initialOutcomeIndex);
        content.Append(outcomeDropDown);

        content.Append(GtkUiHelpers.CreateHeading("验证记录", "heading"));
        content.Append(GtkUiHelpers.CreateMultilineTextFrame(
            out var summaryView,
            minHeight: 76,
            initialText: pendingRequest?.VerificationSummary));

        content.Append(GtkUiHelpers.CreateHeading("下一步行动", "heading"));
        content.Append(GtkUiHelpers.CreateMultilineTextFrame(
            out var nextActionView,
            minHeight: 76,
            initialText: pendingRequest?.NextActionText));

        var dueRow = GtkUiHelpers.CreateDateEntryRow(
            "下一步日期",
            pendingRequest?.NextActionDueOn,
            out var dueEntry,
            out var quickButtons);
        content.Append(dueRow);

        var statusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        content.Append(statusLabel);

        var buttonRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        buttonRow.SetHalign(Gtk.Align.End);
        var cancelBtn = Gtk.Button.NewWithLabel("取消");
        var primaryBtn = Gtk.Button.NewWithLabel(pending is null ? "记录验证" : "重新确认");
        primaryBtn.AddCssClass("suggested-action");
        buttonRow.Append(cancelBtn);
        buttonRow.Append(primaryBtn);
        content.Append(buttonRow);

        toolbar.SetContent(GtkUiHelpers.WrapInScroll(content));
        dialog.SetChild(toolbar);

        var operationId = pendingRequest?.OperationId ?? Guid.NewGuid();
        var frozen = pending is not null;
        var firstSubmit = pending is null;
        ActionProgressionRecoveryIntent? retryIntent = pending;

        void FreezeInputs()
        {
            outcomeDropDown.SetSensitive(false);
            summaryView.SetSensitive(false);
            nextActionView.SetSensitive(false);
            dueEntry.SetSensitive(false);
            quickButtons.SetSensitive(false);
        }

        if (frozen)
        {
            FreezeInputs();
            statusLabel.SetText("发现上次尚未确认的验证。此次只会继续原 operation_id，不会改变原验证内容。");
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.OnClosed += (_, _) => tcs.TrySetResult();
        cancelBtn.OnClicked += (_, _) => dialog.Close();

        primaryBtn.OnClicked += async (_, _) =>
        {
            VerificationOutcome frozenOutcome;
            string frozenSummary;
            string frozenNextAction;
            DateOnly? frozenDueOn;

            if (!frozen)
            {
                var selectedIdx = (int)outcomeDropDown.GetSelected();
                if (selectedIdx < 0 || selectedIdx >= OutcomeOptions.Length)
                {
                    statusLabel.SetText("请选择验证结果。");
                    return;
                }

                frozenSummary = GtkUiHelpers.GetText(summaryView).Trim();
                frozenNextAction = GtkUiHelpers.GetText(nextActionView).Trim();
                if (frozenSummary.Length is < 1 or > 2000)
                {
                    statusLabel.SetText("请填写 1–2000 个字符的验证记录。");
                    summaryView.GrabFocus();
                    return;
                }

                if (frozenNextAction.Length is < 1 or > 1000)
                {
                    statusLabel.SetText("请填写 1–1000 个字符的下一步行动。");
                    nextActionView.GrabFocus();
                    return;
                }

                if (!GtkUiHelpers.TryParseOptionalDate(dueEntry.GetText(), out frozenDueOn, out var dateErr))
                {
                    statusLabel.SetText(dateErr!);
                    dueEntry.GrabFocus();
                    return;
                }

                frozenOutcome = OutcomeOptions[selectedIdx].Outcome;
                retryIntent = new VerificationAndNextActionRecoveryIntent(
                    target.CreateVerificationRequest(
                        operationId,
                        frozenOutcome,
                        frozenSummary,
                        frozenNextAction,
                        frozenDueOn));
                frozen = true;
                FreezeInputs();
            }
            else
            {
                var req = ((VerificationAndNextActionRecoveryIntent)retryIntent!).Request;
                frozenOutcome = req.Outcome;
                frozenSummary = req.VerificationSummary;
                frozenNextAction = req.NextActionText;
                frozenDueOn = req.NextActionDueOn;
            }

            primaryBtn.SetSensitive(false);
            cancelBtn.SetSensitive(false);
            statusLabel.SetText(firstSubmit ? "正在提交验证…" : "正在重新确认原验证操作…");

            try
            {
                ActionProgressionRetryResult outcome;
                if (firstSubmit)
                {
                    firstSubmit = false;
                    var submitted = await viewModel.RecordVerificationAndNextActionAsync(
                        target,
                        frozenOutcome,
                        frozenSummary,
                        frozenNextAction,
                        frozenDueOn,
                        operationId);
                    outcome = new ActionProgressionRetryResult(submitted.IsSuccess, submitted.Failure);
                }
                else
                {
                    outcome = await viewModel.RetryPendingActionProgressionAsync(retryIntent!);
                }

                if (outcome.IsSuccess)
                {
                    dialog.ForceClose();
                    return;
                }

                statusLabel.SetText(FailureText(outcome.Failure));
                if (RequiresSameOperationRetry(outcome.Failure))
                {
                    primaryBtn.SetLabel("重试确认");
                    primaryBtn.SetSensitive(true);
                }
                else
                {
                    primaryBtn.SetLabel("无法提交");
                    primaryBtn.SetSensitive(false);
                }
            }
            finally
            {
                cancelBtn.SetSensitive(true);
            }
        };

        dialog.Present(parent);
        await tcs.Task;
    }

    public static async Task ShowPendingRecoveryAsync(
        Gtk.Widget parent,
        MainWindowViewModel viewModel,
        ActionProgressionRecoveryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(parent);
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

        var confirmed = await GtkUiHelpers.ShowConfirmDialogAsync(
            parent,
            "重新确认原操作",
            detail + "\n\n系统会继续使用原 operation_id，不会创建替代操作。",
            "重新确认");
        if (!confirmed)
        {
            return;
        }

        var result = await viewModel.RetryPendingActionProgressionAsync(intent);
        await GtkUiHelpers.ShowMessageDialogAsync(
            parent,
            result.IsSuccess ? "操作已确认" : "仍未完成确认",
            result.IsSuccess
                ? "服务器已经确认原操作，今日行动与当前关注已重新读取。"
                : FailureText(result.Failure));
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
}
