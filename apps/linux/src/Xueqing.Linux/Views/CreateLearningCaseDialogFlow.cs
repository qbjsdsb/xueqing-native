using Xueqing.Linux.UI;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Linux.Views;

internal static class CreateLearningCaseDialogFlow
{
    public static async Task ShowFromObservationAsync(
        Gtk.Widget parent,
        MainWindowViewModel viewModel,
        StudentRecentObservation observation,
        string? prefillTitle = null,
        string? prefillAction = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(observation);

        var recovery = await viewModel.FindPendingLearningCaseForObservationAsync(observation);
        if (!recovery.IsAvailable)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(
                parent,
                "暂时无法创建学情问题",
                "无法安全读取本机待确认操作。为避免重复创建，本次不会提交。");
            return;
        }

        var pendingRequest = recovery.Request;

        var dialog = Adw.Dialog.New();
        dialog.SetTitle("形成学情问题");
        dialog.SetContentWidth(480);

        var toolbar = Adw.ToolbarView.New();
        toolbar.AddTopBar(Adw.HeaderBar.New());

        var content = Gtk.Box.New(Gtk.Orientation.Vertical, 10);
        content.SetMarginStart(20);
        content.SetMarginEnd(20);
        content.SetMarginTop(16);
        content.SetMarginBottom(20);

        content.Append(GtkUiHelpers.CreateHeading("关注问题", "heading"));
        var titleEntry = Gtk.Entry.New();
        titleEntry.SetPlaceholderText("例如：概括题压缩仍不稳定");
        titleEntry.SetMaxLength(500);
        if (pendingRequest is not null)
        {
            titleEntry.SetText(pendingRequest.Title);
        }
        else if (!string.IsNullOrWhiteSpace(prefillTitle))
        {
            titleEntry.SetText(prefillTitle.Trim());
        }

        content.Append(titleEntry);

        content.Append(GtkUiHelpers.CreateHeading("下一步行动", "heading"));
        content.Append(GtkUiHelpers.CreateMultilineTextFrame(
            out var actionView,
            minHeight: 88,
            initialText: pendingRequest?.PrimaryActionText ?? prefillAction));

        var dueRow = GtkUiHelpers.CreateDateEntryRow(
            "行动日期",
            pendingRequest?.PrimaryActionDueOn,
            out var dueEntry,
            out var quickButtons);
        content.Append(dueRow);

        var statusLabel = GtkUiHelpers.CreateDimLabel(
            pendingRequest is null
                ? string.Empty
                : "发现上次尚未确认的提交。请重新确认结果。");
        content.Append(statusLabel);

        var buttonRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        buttonRow.SetHalign(Gtk.Align.End);
        var cancelBtn = Gtk.Button.NewWithLabel("取消");
        var primaryBtn = Gtk.Button.NewWithLabel(pendingRequest is null ? "创建" : "重新确认");
        primaryBtn.AddCssClass("suggested-action");
        buttonRow.Append(cancelBtn);
        buttonRow.Append(primaryBtn);
        content.Append(buttonRow);

        toolbar.SetContent(GtkUiHelpers.WrapInScroll(content));
        dialog.SetChild(toolbar);

        var operationId = pendingRequest?.OperationId ?? Guid.NewGuid();
        string? submittedTitle = pendingRequest?.Title;
        string? submittedAction = pendingRequest?.PrimaryActionText;
        DateOnly? submittedDueOn = pendingRequest?.PrimaryActionDueOn;
        var intentFrozen = pendingRequest is not null;

        void FreezeInputs()
        {
            titleEntry.SetSensitive(false);
            actionView.SetSensitive(false);
            dueEntry.SetSensitive(false);
            quickButtons.SetSensitive(false);
        }

        if (intentFrozen)
        {
            FreezeInputs();
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.OnClosed += (_, _) => tcs.TrySetResult();
        cancelBtn.OnClicked += (_, _) => dialog.Close();

        primaryBtn.OnClicked += async (_, _) =>
        {
            if (!intentFrozen)
            {
                var title = titleEntry.GetText()?.Trim() ?? string.Empty;
                var primaryAction = GtkUiHelpers.GetText(actionView).Trim();
                if (title.Length is < 1 or > 500)
                {
                    statusLabel.SetText("请填写 1–500 个字符的关注问题。");
                    titleEntry.GrabFocus();
                    return;
                }

                if (primaryAction.Length is < 1 or > 1000)
                {
                    statusLabel.SetText("请填写 1–1000 个字符的下一步行动。");
                    actionView.GrabFocus();
                    return;
                }

                if (!GtkUiHelpers.TryParseOptionalDate(dueEntry.GetText(), out var dueOn, out var dateErr))
                {
                    statusLabel.SetText(dateErr!);
                    dueEntry.GrabFocus();
                    return;
                }

                submittedTitle = title;
                submittedAction = primaryAction;
                submittedDueOn = dueOn;
                intentFrozen = true;
                FreezeInputs();
            }

            primaryBtn.SetSensitive(false);
            cancelBtn.SetSensitive(false);
            statusLabel.SetText(pendingRequest is null ? "正在提交…" : "正在重新确认上次提交…");

            try
            {
                var result = await viewModel.CreateLearningCaseFromObservationAsync(
                    observation,
                    submittedTitle!,
                    submittedAction!,
                    submittedDueOn,
                    operationId);

                if (result.IsSuccess)
                {
                    dialog.ForceClose();
                    return;
                }

                statusLabel.SetText(FailureText(result));
                if (result.Failure?.Kind is
                    CreateLearningCaseFailureKind.ResultUnknown or
                    CreateLearningCaseFailureKind.Transient)
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
        CreateLearningCaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(request);

        var dueText = request.PrimaryActionDueOn is { } dueOn
            ? $"行动日期：{dueOn:yyyy年MM月dd日}"
            : "行动日期：待安排";
        var confirmed = await GtkUiHelpers.ShowConfirmDialogAsync(
            parent,
            "重新确认学情问题提交",
            $"{request.Title}\n{request.PrimaryActionText}\n{dueText}\n\n这不是重新创建。系统会使用原 operation_id 重新确认同一次提交的权威结果。",
            "重新确认");
        if (!confirmed)
        {
            return;
        }

        var result = await viewModel.RetryPendingLearningCaseAsync(request);
        await GtkUiHelpers.ShowMessageDialogAsync(
            parent,
            result.IsSuccess ? "提交已确认" : "仍未完成确认",
            result.IsSuccess
                ? "服务器已经确认这次学情问题提交，当前关注与今日行动已刷新。"
                : RecoveryFailureText(result));
    }

    private static string FailureText(CreateLearningCaseResult result) =>
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

    private static string RecoveryFailureText(CreateLearningCaseResult result) =>
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
