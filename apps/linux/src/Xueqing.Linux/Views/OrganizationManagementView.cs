using System.Collections.Specialized;
using System.ComponentModel;
using Xueqing.Linux.UI;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Linux.Views;

internal sealed class OrganizationManagementView
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Gtk.Widget _dialogParent;
    private readonly Gtk.Widget _root;

    private readonly Gtk.Label _statusLabel;
    private readonly Gtk.Button _inviteButton;
    private readonly Gtk.SearchEntry _searchEntry;
    private readonly Gtk.Box _membersList;
    private readonly CoalescedUiAction _coalescedRebuild;
    private WindowLayoutMode _layoutMode = WindowLayoutMode.Expanded;
    private string _searchQuery = string.Empty;

    public OrganizationManagementView(MainWindowViewModel viewModel, Gtk.Widget dialogParent)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _dialogParent = dialogParent ?? throw new ArgumentNullException(nameof(dialogParent));

        var container = Gtk.Box.New(Gtk.Orientation.Vertical, 14);
        container.SetMarginStart(28);
        container.SetMarginEnd(28);
        container.SetMarginTop(20);
        container.SetMarginBottom(24);

        var topRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);
        var titleBox = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        titleBox.SetHexpand(true);
        var heading = GtkUiHelpers.CreateHeading("机构管理", "title-1");
        GtkUiHelpers.SetAutomationId(heading, "OrganizationManagementHeading");
        _statusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        GtkUiHelpers.SetAutomationId(_statusLabel, "OrganizationManagementSurface");
        titleBox.Append(heading);
        titleBox.Append(_statusLabel);

        _inviteButton = Gtk.Button.NewWithLabel("邀请成员");
        _inviteButton.SetValign(Gtk.Align.End);
        _inviteButton.AddCssClass("suggested-action");
        GtkUiHelpers.SetAutomationId(_inviteButton, "InviteOrganizationMemberButton");
        _inviteButton.OnClicked += async (_, _) => await InviteMemberAsync();

        topRow.Append(titleBox);
        topRow.Append(_inviteButton);
        container.Append(topRow);

        _searchEntry = Gtk.SearchEntry.New();
        _searchEntry.SetPlaceholderText("搜索姓名、角色或状态");
        _searchEntry.SetSizeRequest(340, -1);
        _searchEntry.SetHalign(Gtk.Align.Start);
        GtkUiHelpers.SetAutomationId(_searchEntry, "OrganizationSearchBox");
        _searchEntry.OnSearchChanged += (_, _) =>
        {
            _searchQuery = _searchEntry.GetText()?.Trim() ?? string.Empty;
            RebuildMembers();
        };
        container.Append(_searchEntry);

        _membersList = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        GtkUiHelpers.SetAutomationId(_membersList, "OrganizationWideList");
        container.Append(_membersList);

        var clamp = Adw.Clamp.New();
        clamp.SetMaximumSize(980);
        clamp.SetChild(container);
        _root = GtkUiHelpers.WrapInScroll(clamp);

        _coalescedRebuild = new CoalescedUiAction(RefreshAll);

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.OrganizationMembers.CollectionChanged += (_, _) => _coalescedRebuild.Schedule();

        _coalescedRebuild.RunNow();
    }

    public Gtk.Widget Widget => _root;

    public void ApplyLayout(WindowLayoutMode mode)
    {
        if (_layoutMode == mode)
        {
            return;
        }

        _layoutMode = mode;
        RebuildMembers();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(MainWindowViewModel.OrganizationManagementStatusText) or
            nameof(MainWindowViewModel.CanInviteOrganizationMembers))
        {
            RefreshAll();
        }
    }

    public void RefreshAll()
    {
        _statusLabel.SetText(_viewModel.OrganizationManagementStatusText);
        _inviteButton.SetSensitive(_viewModel.CanInviteOrganizationMembers);
        RebuildMembers();
    }

    private void RebuildMembers()
    {
        GtkUiHelpers.ClearBoxChildren(_membersList);
        var useCompact = _layoutMode != WindowLayoutMode.Expanded;
        GtkUiHelpers.SetAutomationId(_membersList, useCompact ? "OrganizationCompactList" : "OrganizationWideList");

        var filtered = _viewModel.OrganizationMembers
            .Where(m => string.IsNullOrEmpty(_searchQuery) ||
                        m.DisplayName.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                        m.RoleLabel.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                        m.StatusLabel.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                        m.RecentActivity.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (!useCompact)
        {
            var header = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);
            header.SetMarginTop(6);
            header.SetMarginBottom(6);
            header.Append(CreateFixedColumn(GtkUiHelpers.CreateDimLabel("姓名", caption: true), 200));
            header.Append(CreateFixedColumn(GtkUiHelpers.CreateDimLabel("角色", caption: true), 130));
            header.Append(CreateFixedColumn(GtkUiHelpers.CreateDimLabel("状态", caption: true), 110));
            var capHeader = GtkUiHelpers.CreateDimLabel("任教能力", caption: true);
            capHeader.SetHexpand(true);
            header.Append(capHeader);
            _membersList.Append(header);
            _membersList.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }

        foreach (var member in filtered)
        {
            if (useCompact)
            {
                var row = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
                row.SetMarginTop(8);
                row.SetMarginBottom(8);
                row.Append(GtkUiHelpers.CreateHeading(member.DisplayName, "heading"));
                row.Append(GtkUiHelpers.CreateDimLabel($"{member.RoleLabel} · {member.StatusLabel}"));
                row.Append(GtkUiHelpers.CreateDimLabel(member.RecentActivity, caption: true));
                _membersList.Append(row);
            }
            else
            {
                var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);
                row.SetMarginTop(8);
                row.SetMarginBottom(8);
                row.Append(CreateFixedColumn(GtkUiHelpers.CreateHeading(member.DisplayName, "heading"), 200));
                row.Append(CreateFixedColumn(GtkUiHelpers.CreateBodyLabel(member.RoleLabel), 130));
                row.Append(CreateFixedColumn(GtkUiHelpers.CreateBodyLabel(member.StatusLabel), 110));
                var activity = GtkUiHelpers.CreateDimLabel(member.RecentActivity);
                activity.SetHexpand(true);
                row.Append(activity);
                _membersList.Append(row);
            }

            _membersList.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }
    }

    private static Gtk.Widget CreateFixedColumn(Gtk.Widget child, int width)
    {
        child.SetSizeRequest(width, -1);
        return child;
    }

    private async Task InviteMemberAsync()
    {
        if (!_viewModel.CanInviteOrganizationMembers)
        {
            return;
        }

        var recovery = await _viewModel.FindOrganizationInvitationRecoveryAsync();
        if (!recovery.IsAvailable)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(
                _dialogParent,
                "暂时无法安全读取邀请状态",
                "为避免重复创建邀请，本次不会提交。请稍后重新打开机构管理后再试。");
            return;
        }

        if (recovery.HasMultiple)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(
                _dialogParent,
                "存在多条待确认邀请",
                "检测到多个本机待确认邀请。为避免误发，本版本不会自动选择其中一条；请先完成恢复处理。");
            return;
        }

        if (recovery.Intent is { Stage: OrganizationInvitationRecoveryStage.DeliveryFailed } failed)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(
                _dialogParent,
                "上次邀请已创建，但投递失败",
                $"邮箱：{failed.InvitedEmail}\n" +
                "邮件提供方已经明确拒绝这次投递。为避免重复发送，本版本不会自动重新投递；" +
                "后续 Resend/Reissue Gate 会提供显式处理入口。" +
                FailureCodeSuffix(failed.TerminalFailureCode));
            return;
        }

        var pending = recovery.Intent;
        var roleOptions = _viewModel.OrganizationInvitationRoleOptions.ToList();
        if (pending is not null && roleOptions.All(o => o.Role != pending.TargetRole))
        {
            roleOptions.Add(new OrganizationInvitationRoleOption(pending.TargetRole, RoleLabel(pending.TargetRole)));
        }

        if (roleOptions.Count == 0)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(
                _dialogParent,
                "当前没有可邀请角色",
                "机构权限已经变化。请刷新机构管理后再试。");
            return;
        }

        var dialog = Adw.Dialog.New();
        dialog.SetTitle(pending is null ? "邀请成员" : "重新确认邀请");
        dialog.SetContentWidth(460);

        var toolbar = Adw.ToolbarView.New();
        toolbar.AddTopBar(Adw.HeaderBar.New());

        var content = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
        content.SetMarginStart(20);
        content.SetMarginEnd(20);
        content.SetMarginTop(16);
        content.SetMarginBottom(20);

        content.Append(GtkUiHelpers.CreateHeading("邮箱", "heading"));
        var emailEntry = Gtk.Entry.New();
        emailEntry.SetPlaceholderText("name@example.com");
        emailEntry.SetMaxLength(320);
        if (pending is not null)
        {
            emailEntry.SetText(pending.InvitedEmail);
            emailEntry.SetSensitive(false);
        }

        content.Append(emailEntry);

        content.Append(GtkUiHelpers.CreateHeading("角色", "heading"));
        var roleDropDown = Gtk.DropDown.NewFromStrings(roleOptions.Select(o => o.DisplayName).ToArray());
        var selectedRoleIndex = pending is null
            ? 0u
            : (uint)Math.Max(0, roleOptions.FindIndex(o => o.Role == pending.TargetRole));
        roleDropDown.SetSelected(selectedRoleIndex);
        roleDropDown.SetSensitive(pending is null);
        content.Append(roleDropDown);

        var canTeachCheck = Gtk.CheckButton.NewWithLabel("允许作为老师参与任教");
        canTeachCheck.SetActive(pending?.TargetCanTeach ?? true);
        canTeachCheck.SetSensitive(pending is null);
        content.Append(canTeachCheck);

        var explanation = GtkUiHelpers.CreateDimLabel(
            pending is null
                ? "邀请只创建机构成员意图，不会自动创建任何学生任课关系。"
                : pending.Stage == OrganizationInvitationRecoveryStage.CreatePending
                    ? "检测到上次邀请创建结果尚未确认。本次只会重新确认同一个创建操作，不会新建第二份邀请。"
                    : "检测到上次邮件投递结果尚未确认。本次只会确认同一个投递操作，不会再次发送第二封邮件。");
        content.Append(explanation);

        var validationLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        content.Append(validationLabel);

        var buttonRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        buttonRow.SetHalign(Gtk.Align.End);
        var cancelBtn = Gtk.Button.NewWithLabel("取消");
        var primaryBtn = Gtk.Button.NewWithLabel(pending is null ? "发送邀请" : "重新确认");
        primaryBtn.AddCssClass("suggested-action");
        buttonRow.Append(cancelBtn);
        buttonRow.Append(primaryBtn);
        content.Append(buttonRow);

        toolbar.SetContent(content);
        dialog.SetChild(toolbar);

        OrganizationInvitationWorkflowResult? submittedResult = null;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.OnClosed += (_, _) => tcs.TrySetResult();
        cancelBtn.OnClicked += (_, _) => dialog.Close();

        primaryBtn.OnClicked += async (_, _) =>
        {
            if (pending is null)
            {
                var email = emailEntry.GetText()?.Trim().ToLowerInvariant() ?? string.Empty;
                if (!IsNarrowValidEmail(email))
                {
                    validationLabel.SetText("请填写一个有效的邮箱地址后重新提交。");
                    emailEntry.GrabFocus();
                    return;
                }

                var roleIdx = (int)roleDropDown.GetSelected();
                if (roleIdx < 0 || roleIdx >= roleOptions.Count)
                {
                    validationLabel.SetText("当前邀请没有可提交的目标角色。");
                    return;
                }

                primaryBtn.SetSensitive(false);
                cancelBtn.SetSensitive(false);
                validationLabel.SetText("正在提交邀请…");
                try
                {
                    submittedResult = await _viewModel.StartOrganizationInvitationAsync(
                        email,
                        roleOptions[roleIdx].Role,
                        canTeachCheck.GetActive());
                    dialog.ForceClose();
                }
                finally
                {
                    cancelBtn.SetSensitive(true);
                }
            }
            else
            {
                primaryBtn.SetSensitive(false);
                cancelBtn.SetSensitive(false);
                validationLabel.SetText("正在重新确认邀请…");
                try
                {
                    submittedResult = await _viewModel.ResumeOrganizationInvitationAsync(pending);
                    dialog.ForceClose();
                }
                finally
                {
                    cancelBtn.SetSensitive(true);
                }
            }
        };

        dialog.Present(_dialogParent);
        await tcs.Task;

        if (submittedResult is not null)
        {
            await ShowInvitationResultAsync(submittedResult);
        }
    }

    private Task ShowInvitationResultAsync(OrganizationInvitationWorkflowResult result)
    {
        var (title, message) = result.Outcome switch
        {
            OrganizationInvitationWorkflowOutcome.Sent => (
                "邀请已发送",
                "权威邀请已经创建，邮件投递也已确认。成员关系只会在受邀者完成正式接受后创建。"),
            OrganizationInvitationWorkflowOutcome.CreatePendingConfirmation => (
                "邀请创建结果待确认",
                "网络结果不确定，但原始操作已经安全保存在本机。再次点击“邀请成员”只会重新确认同一个创建操作，不会新建第二份邀请。"),
            OrganizationInvitationWorkflowOutcome.DeliveryPendingConfirmation => (
                "邮件投递结果待确认",
                "邀请已经创建，但邮件发送结果无法安全确认。原始投递操作已保留；再次点击“邀请成员”只会确认同一次投递，不会重复发送。"),
            OrganizationInvitationWorkflowOutcome.DeliveryBlocked => (
                "当前无法继续投递",
                "登录状态或机构管理权限已经变化。原始邀请操作仍安全保留；刷新并重新验证权限后可继续确认。"),
            OrganizationInvitationWorkflowOutcome.DeliveryFailed => (
                "邀请已创建，但邮件投递失败",
                "邮件提供方已经明确拒绝本次投递。为避免重复发送，本版本不会自动重试；后续将通过显式 Resend/Reissue 操作处理。"),
            OrganizationInvitationWorkflowOutcome.Rejected => (
                "邀请未创建",
                CreateFailureMessage(result.CreateFailure)),
            OrganizationInvitationWorkflowOutcome.LocalDurabilityFailure => (
                "本机安全状态无法确认",
                "为避免重复创建或重复投递，本次不会生成新的操作。请保留当前应用数据并重新打开机构管理后确认原操作。"),
            _ => (
                "邀请状态无法确认",
                "当前返回无法安全解释。不会自动创建第二份邀请或重复发送邮件。"),
        };

        var code = result.CreateFailure?.Code ?? result.DeliveryFailure?.Code;
        return GtkUiHelpers.ShowMessageDialogAsync(_dialogParent, title, message + FailureCodeSuffix(code));
    }

    private static string CreateFailureMessage(CreateOrganizationInvitationFailure? failure) =>
        failure?.Kind switch
        {
            CreateOrganizationInvitationFailureKind.AlreadyPending =>
                "该邮箱已经存在仍有效的待接受邀请。本版本不会自动新建第二份邀请。",
            CreateOrganizationInvitationFailureKind.AuthorityChanged =>
                "机构管理权限已经变化，本次未创建邀请。",
            CreateOrganizationInvitationFailureKind.AuthenticationRequired =>
                "登录状态已经失效，本次未创建邀请。",
            CreateOrganizationInvitationFailureKind.Validation =>
                "邀请内容未通过服务端校验，本次未创建邀请。",
            CreateOrganizationInvitationFailureKind.OperationConflict =>
                "本机操作标识与另一份邀请意图冲突，本次已拒绝提交。",
            _ => "服务端明确拒绝本次邀请，本次未创建成员关系或教学关系。",
        };

    private static bool IsNarrowValidEmail(string value) =>
        value.Length is >= 3 and <= 320 &&
        !value.Any(char.IsWhiteSpace) &&
        value.Count(ch => ch == '@') == 1 &&
        !value.StartsWith('@') &&
        !value.EndsWith('@');

    private static string RoleLabel(OrganizationInvitationTargetRole role) => role switch
    {
        OrganizationInvitationTargetRole.Owner => "负责人",
        OrganizationInvitationTargetRole.Admin => "管理员",
        OrganizationInvitationTargetRole.Teacher => "老师",
        _ => "成员",
    };

    private static string FailureCodeSuffix(string? code) =>
        string.IsNullOrWhiteSpace(code) ? string.Empty : $"\n\n错误码：{code}";
}
