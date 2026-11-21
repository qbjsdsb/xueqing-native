using System.Collections.Specialized;
using System.ComponentModel;
using Xueqing.Linux.UI;
using Xueqing.Windows.Core.Agent;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Linux.Views;

internal sealed class TodayView
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Gtk.Widget _dialogParent;
    private readonly Gtk.Widget _root;

    private readonly Gtk.Label _statusLabel;
    private readonly Gtk.Box _recoverySection;
    private readonly Gtk.Label _learningCaseRecoveryStatus;
    private readonly Gtk.Box _learningCaseRecoveryList;
    private readonly Gtk.Label _actionRecoveryStatus;
    private readonly Gtk.Box _actionRecoveryList;
    private readonly Gtk.Label _caseLifecycleRecoveryStatus;
    private readonly Gtk.Box _caseLifecycleRecoveryList;
    private readonly Gtk.Box _actionsList;
    private readonly CoalescedUiAction _coalescedRefresh;

    public TodayView(MainWindowViewModel viewModel, Gtk.Widget dialogParent)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _dialogParent = dialogParent ?? throw new ArgumentNullException(nameof(dialogParent));

        var container = Gtk.Box.New(Gtk.Orientation.Vertical, 14);
        container.SetMarginStart(28);
        container.SetMarginEnd(28);
        container.SetMarginTop(20);
        container.SetMarginBottom(24);

        var headerBox = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        var heading = GtkUiHelpers.CreateHeading("今日", "title-1");
        GtkUiHelpers.SetAutomationId(heading, "TodaySurface");
        _statusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        headerBox.Append(heading);
        headerBox.Append(_statusLabel);
        container.Append(headerBox);

        _recoverySection = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        _recoverySection.Append(GtkUiHelpers.CreateHeading("需要确认的操作", "title-4"));

        _learningCaseRecoveryStatus = GtkUiHelpers.CreateDimLabel(string.Empty);
        GtkUiHelpers.SetAutomationId(_learningCaseRecoveryStatus, "PendingLearningCaseRecoveryStatus");
        _learningCaseRecoveryList = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        _recoverySection.Append(_learningCaseRecoveryStatus);
        _recoverySection.Append(_learningCaseRecoveryList);

        _actionRecoveryStatus = GtkUiHelpers.CreateDimLabel(string.Empty);
        GtkUiHelpers.SetAutomationId(_actionRecoveryStatus, "PendingActionProgressionRecoveryStatus");
        _actionRecoveryList = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        _recoverySection.Append(_actionRecoveryStatus);
        _recoverySection.Append(_actionRecoveryList);

        _caseLifecycleRecoveryStatus = GtkUiHelpers.CreateDimLabel(string.Empty);
        GtkUiHelpers.SetAutomationId(_caseLifecycleRecoveryStatus, "PendingCaseLifecycleRecoveryStatus");
        _caseLifecycleRecoveryList = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        _recoverySection.Append(_caseLifecycleRecoveryStatus);
        _recoverySection.Append(_caseLifecycleRecoveryList);

        _recoverySection.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        container.Append(_recoverySection);

        _actionsList = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        GtkUiHelpers.SetAutomationId(_actionsList, "TodayActionList");
        container.Append(_actionsList);

        var clamp = Adw.Clamp.New();
        clamp.SetMaximumSize(960);
        clamp.SetChild(container);
        _root = GtkUiHelpers.WrapInScroll(clamp);

        _coalescedRefresh = new CoalescedUiAction(RefreshAll);

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.TodayActions.CollectionChanged += Collection_Changed;
        _viewModel.PendingLearningCaseRecoveries.CollectionChanged += Collection_Changed;
        _viewModel.PendingActionProgressionRecoveries.CollectionChanged += Collection_Changed;
        _viewModel.PendingCaseLifecycleRecoveries.CollectionChanged += Collection_Changed;

        _coalescedRefresh.RunNow();
    }

    public Gtk.Widget Widget => _root;

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(MainWindowViewModel.TodayStatusText) or
            nameof(MainWindowViewModel.HasPendingRecoveryNotice) or
            nameof(MainWindowViewModel.PendingLearningCaseRecoveryStatusText) or
            nameof(MainWindowViewModel.PendingActionProgressionRecoveryStatusText) or
            nameof(MainWindowViewModel.PendingCaseLifecycleRecoveryStatusText) or
            nameof(MainWindowViewModel.SupportsActionProgression))
        {
            _coalescedRefresh.Schedule();
        }
    }

    private void Collection_Changed(object? sender, NotifyCollectionChangedEventArgs e) =>
        _coalescedRefresh.Schedule();

    public void RefreshAll()
    {
        _statusLabel.SetText(_viewModel.TodayStatusText);
        _statusLabel.SetVisible(!string.IsNullOrWhiteSpace(_viewModel.TodayStatusText));

        _recoverySection.SetVisible(_viewModel.HasPendingRecoveryNotice);

        _learningCaseRecoveryStatus.SetText(_viewModel.PendingLearningCaseRecoveryStatusText);
        _learningCaseRecoveryStatus.SetVisible(!string.IsNullOrWhiteSpace(_viewModel.PendingLearningCaseRecoveryStatusText));
        RebuildPendingLearningCases();

        _actionRecoveryStatus.SetText(_viewModel.PendingActionProgressionRecoveryStatusText);
        _actionRecoveryStatus.SetVisible(!string.IsNullOrWhiteSpace(_viewModel.PendingActionProgressionRecoveryStatusText));
        RebuildPendingActionProgressions();

        _caseLifecycleRecoveryStatus.SetText(_viewModel.PendingCaseLifecycleRecoveryStatusText);
        _caseLifecycleRecoveryStatus.SetVisible(!string.IsNullOrWhiteSpace(_viewModel.PendingCaseLifecycleRecoveryStatusText));
        RebuildPendingCaseLifecycles();

        RebuildTodayActions();
    }

    private void RebuildPendingLearningCases()
    {
        GtkUiHelpers.ClearBoxChildren(_learningCaseRecoveryList);
        foreach (var item in _viewModel.PendingLearningCaseRecoveries)
        {
            var row = CreateRecoveryRow(
                item.Title,
                item.PrimaryActionText,
                $"{item.StudentDisplayName} · {item.SubjectDisplayName} · {item.DueLabel}",
                item.Request.OperationId != Guid.Empty
                    ? AgentAutomationIds.RecoveryLearningCase(item.Request.OperationId)
                    : null,
                async () => await CreateLearningCaseDialogFlow.ShowPendingRecoveryAsync(
                    _dialogParent,
                    _viewModel,
                    item.Request));
            _learningCaseRecoveryList.Append(row);
        }
    }

    private void RebuildPendingActionProgressions()
    {
        GtkUiHelpers.ClearBoxChildren(_actionRecoveryList);
        foreach (var item in _viewModel.PendingActionProgressionRecoveries)
        {
            var row = CreateRecoveryRow(
                item.Heading,
                item.Detail,
                $"{item.StudentDisplayName} · {item.SubjectDisplayName}",
                item.Intent.OperationId != Guid.Empty
                    ? AgentAutomationIds.RecoveryAction(item.Intent.OperationId)
                    : null,
                async () => await ActionProgressionDialogFlow.ShowPendingRecoveryAsync(
                    _dialogParent,
                    _viewModel,
                    item.Intent));
            _actionRecoveryList.Append(row);
        }
    }

    private void RebuildPendingCaseLifecycles()
    {
        GtkUiHelpers.ClearBoxChildren(_caseLifecycleRecoveryList);
        foreach (var item in _viewModel.PendingCaseLifecycleRecoveries)
        {
            var row = CreateRecoveryRow(
                item.Heading,
                item.Detail,
                $"{item.StudentDisplayName} · {item.SubjectDisplayName}",
                item.Intent.OperationId != Guid.Empty
                    ? AgentAutomationIds.RecoveryCaseLifecycle(item.Intent.OperationId)
                    : null,
                async () => await CaseLifecycleDialogFlow.ShowPendingRecoveryAsync(
                    _dialogParent,
                    _viewModel,
                    item.Intent));
            _caseLifecycleRecoveryList.Append(row);
        }
    }

    private static Gtk.Widget CreateRecoveryRow(
        string headingText,
        string detailText,
        string metaText,
        string? automationId,
        Func<Task> onRetry)
    {
        var card = Gtk.Box.New(Gtk.Orientation.Horizontal, 14);
        card.AddCssClass("card");
        card.SetMarginTop(2);
        card.SetMarginBottom(2);

        var inner = Gtk.Box.New(Gtk.Orientation.Horizontal, 14);
        inner.SetMarginStart(14);
        inner.SetMarginEnd(14);
        inner.SetMarginTop(10);
        inner.SetMarginBottom(10);
        inner.SetHexpand(true);

        var left = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
        left.SetHexpand(true);
        left.Append(GtkUiHelpers.CreateHeading(headingText, "heading"));
        if (!string.IsNullOrWhiteSpace(detailText))
        {
            left.Append(GtkUiHelpers.CreateBodyLabel(detailText));
        }

        left.Append(GtkUiHelpers.CreateDimLabel(metaText, caption: true));

        var retryBtn = Gtk.Button.NewWithLabel("重新确认");
        retryBtn.SetValign(Gtk.Align.Center);
        retryBtn.AddCssClass("suggested-action");
        if (!string.IsNullOrWhiteSpace(automationId))
        {
            GtkUiHelpers.SetAutomationId(retryBtn, automationId);
        }

        retryBtn.OnClicked += async (_, _) => await onRetry();

        inner.Append(left);
        inner.Append(retryBtn);
        card.Append(inner);
        return card;
    }

    private void RebuildTodayActions()
    {
        GtkUiHelpers.ClearBoxChildren(_actionsList);

        if (_viewModel.TodayActions.Count == 0 && string.IsNullOrWhiteSpace(_viewModel.TodayStatusText))
        {
            _actionsList.Append(GtkUiHelpers.CreateDimLabel("今日暂无待处理的教学行动。"));
            return;
        }

        foreach (var item in _viewModel.TodayActions)
        {
            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);
            row.SetMarginStart(4);
            row.SetMarginEnd(4);
            row.SetMarginTop(8);
            row.SetMarginBottom(8);

            var bucketCol = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
            bucketCol.SetSizeRequest(92, -1);
            var bucketLabel = GtkUiHelpers.CreateHeading(item.BucketLabel, "heading");
            if (item.Bucket == TodayActionBucket.Overdue)
            {
                bucketLabel.AddCssClass("error");
            }
            else if (item.Bucket == TodayActionBucket.Today)
            {
                bucketLabel.AddCssClass("accent");
            }

            bucketCol.Append(bucketLabel);
            bucketCol.Append(GtkUiHelpers.CreateDimLabel(item.DueLabel, caption: true));

            var bodyCol = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
            bodyCol.SetHexpand(true);
            bodyCol.Append(GtkUiHelpers.CreateHeading(item.Title, "heading"));
            bodyCol.Append(GtkUiHelpers.CreateDimLabel(item.StudentDisplayName));
            if (!string.IsNullOrWhiteSpace(item.InteractionStateLabel))
            {
                bodyCol.Append(GtkUiHelpers.CreateDimLabel(item.InteractionStateLabel, caption: true));
            }

            var rightCol = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
            rightCol.SetHalign(Gtk.Align.End);
            rightCol.SetValign(Gtk.Align.Start);
            var subjectLabel = GtkUiHelpers.CreateDimLabel(item.Subject);
            subjectLabel.SetHalign(Gtk.Align.End);
            rightCol.Append(subjectLabel);

            if (_viewModel.SupportsActionProgression)
            {
                var actionButtons = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
                actionButtons.SetHalign(Gtk.Align.End);

                var rescheduleBtn = Gtk.Button.NewWithLabel("改期");
                var verifyBtn = Gtk.Button.NewWithLabel("验证");
                verifyBtn.AddCssClass("suggested-action");

                if (Guid.TryParse(item.Id, out var actionId) && actionId != Guid.Empty)
                {
                    GtkUiHelpers.SetAutomationId(rescheduleBtn, AgentAutomationIds.TodayReschedule(actionId));
                    GtkUiHelpers.SetAutomationId(verifyBtn, AgentAutomationIds.TodayVerify(actionId));
                }

                var capturedItem = item;
                rescheduleBtn.OnClicked += async (_, _) =>
                {
                    var lookup = await _viewModel.FindTodayActionProgressionAsync(capturedItem);
                    await ActionProgressionDialogFlow.ShowRescheduleAsync(_dialogParent, _viewModel, lookup);
                };
                verifyBtn.OnClicked += async (_, _) =>
                {
                    var lookup = await _viewModel.FindTodayActionProgressionAsync(capturedItem);
                    await ActionProgressionDialogFlow.ShowVerificationAsync(_dialogParent, _viewModel, lookup);
                };

                actionButtons.Append(rescheduleBtn);
                actionButtons.Append(verifyBtn);
                rightCol.Append(actionButtons);
            }

            row.Append(bucketCol);
            row.Append(bodyCol);
            row.Append(rightCol);

            _actionsList.Append(row);
            _actionsList.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }
    }
}
