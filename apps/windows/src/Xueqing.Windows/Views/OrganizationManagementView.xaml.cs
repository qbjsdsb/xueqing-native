using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Windows.Views;

public sealed partial class OrganizationManagementView : UserControl
{
    public OrganizationManagementView()
    {
        InitializeComponent();
    }

    public void ApplyLayout(WindowLayoutMode mode)
    {
        var useCompactRows = mode != WindowLayoutMode.Expanded;
        WideList.Visibility = useCompactRows ? Visibility.Collapsed : Visibility.Visible;
        CompactList.Visibility = useCompactRows ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void InviteMember_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !viewModel.CanInviteOrganizationMembers)
        {
            return;
        }

        var recovery = await viewModel.FindOrganizationInvitationRecoveryAsync();
        if (!recovery.IsAvailable)
        {
            await ShowMessageAsync(
                "暂时无法安全读取邀请状态",
                "为避免重复创建邀请，本次不会提交。请稍后重新打开机构管理后再试。");
            return;
        }

        if (recovery.HasMultiple)
        {
            await ShowMessageAsync(
                "存在多条待确认邀请",
                "检测到多个本机待确认邀请。为避免误发，本版本不会自动选择其中一条；请先完成恢复处理。");
            return;
        }

        if (recovery.Intent is { Stage: OrganizationInvitationRecoveryStage.DeliveryFailed } failed)
        {
            await ShowMessageAsync(
                "上次邀请已创建，但投递失败",
                $"邮箱：{failed.InvitedEmail}\n"
                + "邮件提供方已经明确拒绝这次投递。为避免重复发送，本版本不会自动重新投递；"
                + "后续 Resend/Reissue Gate 会提供显式处理入口。"
                + FailureCodeSuffix(failed.TerminalFailureCode));
            return;
        }

        var pending = recovery.Intent;
        var roleOptions = viewModel.OrganizationInvitationRoleOptions.ToList();
        if (pending is not null &&
            roleOptions.All(option => option.Role != pending.TargetRole))
        {
            roleOptions.Add(new OrganizationInvitationRoleOption(
                pending.TargetRole,
                RoleLabel(pending.TargetRole)));
        }

        if (roleOptions.Count == 0)
        {
            await ShowMessageAsync(
                "当前没有可邀请角色",
                "机构权限已经变化。请刷新机构管理后再试。");
            return;
        }

        var emailBox = new TextBox
        {
            Header = "邮箱",
            PlaceholderText = "name@example.com",
            MaxLength = 320,
            Text = pending?.InvitedEmail ?? string.Empty,
            IsEnabled = pending is null,
        };
        var roleBox = new ComboBox
        {
            Header = "角色",
            ItemsSource = roleOptions,
            DisplayMemberPath = nameof(OrganizationInvitationRoleOption.DisplayName),
            SelectedItem = pending is null
                ? roleOptions[0]
                : roleOptions.First(option => option.Role == pending.TargetRole),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = pending is null,
        };
        var canTeachBox = new CheckBox
        {
            Content = "允许作为老师参与任教",
            IsChecked = pending?.TargetCanTeach ?? true,
            IsEnabled = pending is null,
        };
        var explanation = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            Text = pending is null
                ? "邀请只创建机构成员意图，不会自动创建任何学生任课关系。"
                : pending.Stage == OrganizationInvitationRecoveryStage.CreatePending
                    ? "检测到上次邀请创建结果尚未确认。本次只会重新确认同一个创建操作，不会新建第二份邀请。"
                    : "检测到上次邮件投递结果尚未确认。本次只会确认同一个投递操作，不会再次发送第二封邮件。",
        };

        var content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 420,
        };
        content.Children.Add(emailBox);
        content.Children.Add(roleBox);
        content.Children.Add(canTeachBox);
        content.Children.Add(explanation);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = pending is null ? "邀请成员" : "重新确认邀请",
            Content = content,
            PrimaryButtonText = pending is null ? "发送邀请" : "重新确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        var choice = await dialog.ShowAsync();
        if (choice != ContentDialogResult.Primary)
        {
            return;
        }

        OrganizationInvitationWorkflowResult result;
        if (pending is not null)
        {
            result = await viewModel.ResumeOrganizationInvitationAsync(pending);
        }
        else
        {
            var email = emailBox.Text.Trim().ToLowerInvariant();
            if (!IsNarrowValidEmail(email))
            {
                await ShowMessageAsync(
                    "邮箱格式不正确",
                    "请填写一个有效的邮箱地址后重新提交。");
                return;
            }

            if (roleBox.SelectedItem is not OrganizationInvitationRoleOption role)
            {
                await ShowMessageAsync(
                    "请选择角色",
                    "当前邀请没有可提交的目标角色。");
                return;
            }

            result = await viewModel.StartOrganizationInvitationAsync(
                email,
                role.Role,
                canTeachBox.IsChecked == true);
        }

        await ShowInvitationResultAsync(result);
    }

    private async Task ShowInvitationResultAsync(
        OrganizationInvitationWorkflowResult result)
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
        await ShowMessageAsync(title, message + FailureCodeSuffix(code));
    }

    private static string CreateFailureMessage(
        CreateOrganizationInvitationFailure? failure) =>
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

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
            },
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private static bool IsNarrowValidEmail(string value) =>
        value.Length is >= 3 and <= 320 &&
        !value.Any(char.IsWhiteSpace) &&
        value.Count(ch => ch == '@') == 1 &&
        !value.StartsWith('@') &&
        !value.EndsWith('@');

    private static string RoleLabel(OrganizationInvitationTargetRole role) =>
        role switch
        {
            OrganizationInvitationTargetRole.Owner => "负责人",
            OrganizationInvitationTargetRole.Admin => "管理员",
            OrganizationInvitationTargetRole.Teacher => "老师",
            _ => "成员",
        };

    private static string FailureCodeSuffix(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : $"\n\n错误码：{code}";
}
