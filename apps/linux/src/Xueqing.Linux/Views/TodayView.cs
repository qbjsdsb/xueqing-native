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
    private readonly Gtk.Box _kpiStrip;
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

        var container = Gtk.Box.New(Gtk.Orientation.Vertical, 16);
        container.SetMarginStart(28);
        container.SetMarginEnd(28);
        container.SetMarginTop(20);
        container.SetMarginBottom(24);

        var headerBox = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        var heading = GtkUiHelpers.CreateHeading("今日教学跟进", "title-1");
        GtkUiHelpers.SetAutomationId(heading, "TodaySurface");
        _statusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        headerBox.Append(heading);
        headerBox.Append(_statusLabel);
        container.Append(headerBox);

        _kpiStrip = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        _kpiStrip.SetHomogeneous(true);
        container.Append(_kpiStrip);

        _filterRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        container.Append(_filterRow);

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
        var allItems = _viewModel.TodayActions;
        var overdueCount = allItems.Count(a => a.Bucket == TodayActionBucket.Overdue);
        var todayCount = allItems.Count(a => a.Bucket == TodayActionBucket.Today);
        var undatedCount = allItems.Count(a => a.Bucket == TodayActionBucket.Undated);

        // 3-tile KPI Summary Strip
        GtkUiHelpers.ClearBoxChildren(_kpiStrip);
        _kpiStrip.Append(GtkUiHelpers.CreateKpiTile(
            $"{todayCount}",
            "今日待跟进",
            "按计划今日需完成",
            todayCount > 0 ? "xq-kpi-value-accent" : null));
        _kpiStrip.Append(GtkUiHelpers.CreateKpiTile(
            $"{overdueCount}",
            "已逾期优先处理",
            overdueCount > 0 ? "建议先核查最近一次干预" : "当前无逾期积压",
            overdueCount > 0 ? "xq-kpi-value-overdue" : null));
        _kpiStrip.Append(GtkUiHelpers.CreateKpiTile(
            $"{undatedCount}",
            "待安排日期",
            $"全部共 {allItems.Count} 项行动"));

        // Filter pills
        GtkUiHelpers.ClearBoxChildren(_filterRow);
        AddFilterButton($"全部 ({allItems.Count})", null);
        AddFilterButton($"已逾期 ({overdueCount})", TodayActionBucket.Overdue);
        AddFilterButton($"今日 ({todayCount})", TodayActionBucket.Today);
        AddFilterButton($"待安排 ({undatedCount})", TodayActionBucket.Undated);

        GtkUiHelpers.ClearBoxChildren(_actionsList);

        var visibleItems = _bucketFilter is { } filter
            ? allItems.Where(a => a.Bucket == filter).ToArray()
            : allItems.ToArray();

        if (visibleItems.Length == 0 && string.IsNullOrWhiteSpace(_viewModel.TodayStatusText))
        {
            var emptyCard = GtkUiHelpers.CreateCardBox(spacing: 4, cardCss: "xq-empty-card");
            emptyCard.Append(GtkUiHelpers.CreateHeading(
                _bucketFilter is null ? "今日暂无待处理的教学行动" : "当前筛选下暂无待办行动",
                "heading"));
            emptyCard.Append(GtkUiHelpers.CreateDimLabel("可以在「学生」页查看试卷趋势、生成阶段复盘并采纳新的跟进课题。"));
            _actionsList.Append(emptyCard);
            return;
        }

        foreach (var item in visibleItems)
        {
            var card = GtkUiHelpers.CreateCardBox(spacing: 6);
            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);

            var bucketCol = Gtk.Box.New(Gtk.Orientation.Vertical, 5);
            bucketCol.SetSizeRequest(88, -1);
            var pillCss = item.Bucket switch
            {
                TodayActionBucket.Overdue => "xq-pill-overdue",
                TodayActionBucket.Today => "xq-pill-ai",
                TodayActionBucket.Undated => "xq-pill-draft",
                _ => "xq-pill-neutral",
            };
            var bucketPill = GtkUiHelpers.CreateBadge(item.BucketLabel, pillCss);
            bucketPill.SetHalign(Gtk.Align.Start);
            bucketCol.Append(bucketPill);

            var dueLabel = GtkUiHelpers.CreateDimLabel(item.DueLabel, caption: true);
            dueLabel.SetWrap(false);
            dueLabel.SetSingleLineMode(true);
            bucketCol.Append(dueLabel);

            var bodyCol = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
            bodyCol.SetHexpand(true);
            bodyCol.Append(GtkUiHelpers.CreateHeading(item.Title, "heading"));

            var metaLine = string.IsNullOrWhiteSpace(item.InteractionStateLabel)
                ? item.StudentDisplayName
                : $"{item.StudentDisplayName}  ·  {item.InteractionStateLabel}";
            bodyCol.Append(GtkUiHelpers.CreateDimLabel(metaLine, caption: true));

            var rightCol = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
            rightCol.SetHalign(Gtk.Align.End);
            rightCol.SetValign(Gtk.Align.Center);
            rightCol.Append(GtkUiHelpers.CreateBadge(item.Subject, "xq-pill-neutral"));

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
            card.Append(row);

            _actionsList.Append(card);
        }
    }

    private void AddFilterButton(string label, TodayActionBucket? bucket)
    {
        var btn = Gtk.Button.NewWithLabel(label);
        btn.AddCssClass("pill");
        if (_bucketFilter == bucket)
        {
            btn.AddCssClass("xq-filter-active");
        }
        else
        {
            btn.AddCssClass("flat");
        }

        btn.OnClicked += (_, _) =>
        {
            _bucketFilter = bucket;
            RebuildTodayActions();
        };
        _filterRow.Append(btn);
    }
}
