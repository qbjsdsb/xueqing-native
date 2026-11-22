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
    private readonly Gtk.Box _filterRow;
    private readonly Gtk.Box _recoverySection;
    private readonly Gtk.Label _learningCaseRecoveryStatus;
    private readonly Gtk.Box _learningCaseRecoveryList;
    private readonly Gtk.Label _actionRecoveryStatus;
    private readonly Gtk.Box _actionRecoveryList;
    private readonly Gtk.Label _caseLifecycleRecoveryStatus;
    private readonly Gtk.Box _caseLifecycleRecoveryList;
    private readonly Gtk.Box _actionsList;
    private readonly CoalescedUiAction _coalescedRefresh;
    private TodayActionBucket? _bucketFilter;

    public TodayView(MainWindowViewModel viewModel, Gtk.Widget dialogParent)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _dialogParent = dialogParent ?? throw new ArgumentNullException(nameof(dialogParent));

        var container = Gtk.Box.New(Gtk.Orientation.Vertical, 14);
        container.SetMarginStart(32);
        container.SetMarginEnd(32);
        container.SetMarginTop(26);
        container.SetMarginBottom(32);

        // Editorial header: Title on left, quiet inline filter tabs on right (Things 3 / Linear rhythm)
        var topBar = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);
        var heading = GtkUiHelpers.CreateHeading("今日", "title-1");
        heading.SetHexpand(true);
        GtkUiHelpers.SetAutomationId(heading, "TodaySurface");
        topBar.Append(heading);

        _filterRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 4);
        _filterRow.SetValign(Gtk.Align.End);
        topBar.Append(_filterRow);
        container.Append(topBar);

        _statusLabel = GtkUiHelpers.CreateDimLabel(string.Empty, caption: true);
        container.Append(_statusLabel);

        container.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));

        _recoverySection = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        _recoverySection.Append(GtkUiHelpers.CreateSectionKicker("待确认"));

        _learningCaseRecoveryStatus = GtkUiHelpers.CreateDimLabel(string.Empty, caption: true);
        GtkUiHelpers.SetAutomationId(_learningCaseRecoveryStatus, "PendingLearningCaseRecoveryStatus");
        _learningCaseRecoveryList = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        _recoverySection.Append(_learningCaseRecoveryStatus);
        _recoverySection.Append(_learningCaseRecoveryList);

        _actionRecoveryStatus = GtkUiHelpers.CreateDimLabel(string.Empty, caption: true);
        GtkUiHelpers.SetAutomationId(_actionRecoveryStatus, "PendingActionProgressionRecoveryStatus");
        _actionRecoveryList = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        _recoverySection.Append(_actionRecoveryStatus);
        _recoverySection.Append(_actionRecoveryList);

        _caseLifecycleRecoveryStatus = GtkUiHelpers.CreateDimLabel(string.Empty, caption: true);
        GtkUiHelpers.SetAutomationId(_caseLifecycleRecoveryStatus, "PendingCaseLifecycleRecoveryStatus");
        _caseLifecycleRecoveryList = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        _recoverySection.Append(_caseLifecycleRecoveryStatus);
        _recoverySection.Append(_caseLifecycleRecoveryList);

        _recoverySection.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        container.Append(_recoverySection);

        _actionsList = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        GtkUiHelpers.SetAutomationId(_actionsList, "TodayActionList");
        container.Append(_actionsList);

        var clamp = Adw.Clamp.New();
        clamp.SetMaximumSize(860);
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
        var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);
        row.AddCssClass("xq-callout");

        var left = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
        left.SetHexpand(true);
        left.Append(GtkUiHelpers.CreateHeading(headingText, "heading"));
        if (!string.IsNullOrWhiteSpace(detailText))
        {
            left.Append(GtkUiHelpers.CreateBodyLabel(detailText));
        }

        left.Append(GtkUiHelpers.CreateDimLabel(metaText, caption: true));

        var retryBtn = Gtk.Button.NewWithLabel("重试");
        retryBtn.SetValign(Gtk.Align.Center);
        retryBtn.AddCssClass("suggested-action");
        if (!string.IsNullOrWhiteSpace(automationId))
        {
            GtkUiHelpers.SetAutomationId(retryBtn, automationId);
        }

        retryBtn.OnClicked += async (_, _) => await onRetry();

        row.Append(left);
        row.Append(retryBtn);
        return row;
    }

    private void RebuildTodayActions()
    {
        var allItems = _viewModel.TodayActions;
        var overdueCount = allItems.Count(a => a.Bucket == TodayActionBucket.Overdue);
        var todayCount = allItems.Count(a => a.Bucket == TodayActionBucket.Today);
        var undatedCount = allItems.Count(a => a.Bucket == TodayActionBucket.Undated);

        // Quiet flat underline filter tabs
        GtkUiHelpers.ClearBoxChildren(_filterRow);
        AddFilterTab($"全部 {allItems.Count}", null);
        if (overdueCount > 0)
        {
            AddFilterTab($"逾期 {overdueCount}", TodayActionBucket.Overdue);
        }

        AddFilterTab($"今日 {todayCount}", TodayActionBucket.Today);
        if (undatedCount > 0)
        {
            AddFilterTab($"待定 {undatedCount}", TodayActionBucket.Undated);
        }

        GtkUiHelpers.ClearBoxChildren(_actionsList);

        var visibleItems = _bucketFilter is { } filter
            ? allItems.Where(a => a.Bucket == filter).ToArray()
            : allItems.ToArray();

        if (visibleItems.Length == 0 && string.IsNullOrWhiteSpace(_viewModel.TodayStatusText))
        {
            var empty = Gtk.Label.New(_bucketFilter is null ? "今日暂无待办" : "该分类下暂无待办");
            empty.SetHalign(Gtk.Align.Start);
            empty.AddCssClass("xq-empty-note");
            _actionsList.Append(empty);
            return;
        }

        foreach (var item in visibleItems)
        {
            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 18);
            row.AddCssClass("xq-list-row");

            // Left date & bucket column (plain typography, zero noisy pills)
            var leftCol = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
            leftCol.SetSizeRequest(72, -1);
            leftCol.SetValign(Gtk.Align.Start);

            var statusCss = item.Bucket switch
            {
                TodayActionBucket.Overdue => "xq-status-overdue",
                TodayActionBucket.Today => "xq-status-today",
                _ => "xq-status-muted",
            };
            var statusText = item.Bucket switch
            {
                TodayActionBucket.Overdue => "逾期",
                TodayActionBucket.Today => "今日",
                TodayActionBucket.Undated => "待定",
                _ => item.BucketLabel,
            };
            leftCol.Append(GtkUiHelpers.CreateStatusText(statusText, statusCss, Gtk.Align.Start));

            var dateText = item.DueDate is { } d ? d.ToString("MM/dd") : string.Empty;
            if (!string.IsNullOrEmpty(dateText))
            {
                var dateLabel = Gtk.Label.New(dateText);
                dateLabel.SetHalign(Gtk.Align.Start);
                dateLabel.AddCssClass("xq-date-col");
                leftCol.Append(dateLabel);
            }

            // Center title + quiet student/subject subtitle
            var bodyCol = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
            bodyCol.SetHexpand(true);
            bodyCol.Append(GtkUiHelpers.CreateHeading(item.Title, "heading"));

            var subtitleParts = new List<string> { item.StudentDisplayName, item.Subject };
            if (!string.IsNullOrWhiteSpace(item.InteractionStateLabel))
            {
                subtitleParts.Add(item.InteractionStateLabel);
            }

            bodyCol.Append(GtkUiHelpers.CreateDimLabel(string.Join(" · ", subtitleParts), caption: true));

            row.Append(leftCol);
            row.Append(bodyCol);

            if (_viewModel.SupportsActionProgression)
            {
                var actionButtons = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
                actionButtons.SetValign(Gtk.Align.Center);

                var rescheduleBtn = Gtk.Button.NewWithLabel("改期");
                rescheduleBtn.AddCssClass("flat");
                var verifyBtn = Gtk.Button.NewWithLabel("验证");

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
                row.Append(actionButtons);
            }

            _actionsList.Append(row);
        }
    }

    private void AddFilterTab(string label, TodayActionBucket? bucket)
    {
        var btn = Gtk.Button.NewWithLabel(label);
        btn.AddCssClass("flat");
        btn.AddCssClass("xq-tab");
        if (_bucketFilter == bucket)
        {
            btn.AddCssClass("xq-tab-active");
        }

        btn.OnClicked += (_, _) =>
        {
            _bucketFilter = bucket;
            RebuildTodayActions();
        };
        _filterRow.Append(btn);
    }
}
