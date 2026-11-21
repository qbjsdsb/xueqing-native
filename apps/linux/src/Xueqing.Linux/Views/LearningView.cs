using System.Collections.Specialized;
using System.ComponentModel;
using Xueqing.Linux.UI;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Linux.Views;

internal sealed class LearningView
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Gtk.Widget _root;

    private readonly Gtk.Box _authoritativePanel;
    private readonly Gtk.Label _studentNameLabel;
    private readonly Gtk.Label _teachingContextLabel;
    private readonly Gtk.Label _focusStatusLabel;
    private readonly Gtk.Box _focusList;
    private readonly Gtk.Label _observationsStatusLabel;
    private readonly Gtk.Box _observationsList;
    private readonly Gtk.Label _observationsMoreLabel;

    private readonly Gtk.Box _prototypePanel;
    private readonly Gtk.Box _unavailablePanel;
    private readonly Gtk.Label _unavailableStatusLabel;
    private readonly CoalescedUiAction _coalescedRefresh;

    public LearningView(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        var container = Gtk.Box.New(Gtk.Orientation.Vertical, 16);
        container.SetMarginStart(28);
        container.SetMarginEnd(28);
        container.SetMarginTop(20);
        container.SetMarginBottom(24);

        var heading = GtkUiHelpers.CreateHeading("学情", "title-1");
        GtkUiHelpers.SetAutomationId(heading, "LearningSurface");
        container.Append(heading);

        // 1. Authoritative mode panel
        _authoritativePanel = Gtk.Box.New(Gtk.Orientation.Vertical, 18);
        var studentHeader = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
        _studentNameLabel = GtkUiHelpers.CreateHeading(string.Empty, "title-2");
        _teachingContextLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        studentHeader.Append(_studentNameLabel);
        studentHeader.Append(_teachingContextLabel);
        _authoritativePanel.Append(studentHeader);

        var focusSection = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        focusSection.Append(GtkUiHelpers.CreateHeading("当前关注", "title-4"));
        _focusStatusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        _focusList = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        focusSection.Append(_focusStatusLabel);
        focusSection.Append(_focusList);
        _authoritativePanel.Append(focusSection);

        _authoritativePanel.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));

        var obsSection = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        obsSection.Append(GtkUiHelpers.CreateHeading("最近观察", "title-4"));
        _observationsStatusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        _observationsList = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        _observationsMoreLabel = GtkUiHelpers.CreateDimLabel(string.Empty, caption: true);
        obsSection.Append(_observationsStatusLabel);
        obsSection.Append(_observationsList);
        obsSection.Append(_observationsMoreLabel);
        _authoritativePanel.Append(obsSection);

        container.Append(_authoritativePanel);

        // 2. Prototype mode panel
        _prototypePanel = BuildPrototypePanel();
        container.Append(_prototypePanel);

        // 3. Startup unavailable panel
        _unavailablePanel = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        _unavailablePanel.Append(GtkUiHelpers.CreateHeading("教学工作区暂不可用", "title-3"));
        _unavailableStatusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        GtkUiHelpers.SetAutomationId(_unavailableStatusLabel, "LearningWorkspaceUnavailable");
        _unavailablePanel.Append(_unavailableStatusLabel);
        container.Append(_unavailablePanel);

        var clamp = Adw.Clamp.New();
        clamp.SetMaximumSize(880);
        clamp.SetChild(container);
        _root = GtkUiHelpers.WrapInScroll(clamp);

        _coalescedRefresh = new CoalescedUiAction(RefreshVisibilityAndContent);

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.CurrentFocus.CollectionChanged += (_, _) => _coalescedRefresh.Schedule();
        _viewModel.RecentObservations.CollectionChanged += (_, _) => _coalescedRefresh.Schedule();

        _coalescedRefresh.RunNow();
    }

    public Gtk.Widget Widget => _root;

    private Gtk.Box BuildPrototypePanel()
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
        var lc = _viewModel.LearningCase;

        box.Append(GtkUiHelpers.CreateDimLabel("测试案例", caption: true));
        box.Append(GtkUiHelpers.CreateHeading(lc.Title, "title-2"));
        box.Append(GtkUiHelpers.CreateBodyLabel($"{lc.StudentDisplayName} · {lc.Subject} · {lc.StateLabel}"));
        box.Append(GtkUiHelpers.CreateDimLabel(lc.ResponsibleTeacher));

        var nextBox = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        nextBox.SetMarginTop(8);
        nextBox.Append(GtkUiHelpers.CreateHeading("下一步", "title-4"));
        nextBox.Append(GtkUiHelpers.CreateBodyLabel(lc.NextAction));
        box.Append(nextBox);

        box.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));

        var timelineBox = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        GtkUiHelpers.SetAutomationId(timelineBox, "LearningTimeline");
        foreach (var entry in lc.Timeline)
        {
            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);
            row.SetMarginTop(4);
            row.SetMarginBottom(4);

            var left = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
            left.SetSizeRequest(116, -1);
            left.Append(GtkUiHelpers.CreateDimLabel(entry.DateLabel, caption: true));
            left.Append(GtkUiHelpers.CreateHeading(entry.Heading, "heading"));

            var right = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
            right.SetHexpand(true);
            right.Append(GtkUiHelpers.CreateBodyLabel(entry.Body));
            right.Append(GtkUiHelpers.CreateDimLabel(entry.ActorDisplayName, caption: true));

            row.Append(left);
            row.Append(right);
            timelineBox.Append(row);
            timelineBox.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }

        box.Append(timelineBox);
        return box;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(MainWindowViewModel.SelectedStudent) or
            nameof(MainWindowViewModel.SelectedTeachingContext) or
            nameof(MainWindowViewModel.CurrentFocusStatusText) or
            nameof(MainWindowViewModel.RecentObservationsStatusText) or
            nameof(MainWindowViewModel.RecentHistoryMoreText) or
            nameof(MainWindowViewModel.StartupStatusText))
        {
            RefreshVisibilityAndContent();
        }
    }

    private void RefreshVisibilityAndContent()
    {
        _authoritativePanel.SetVisible(_viewModel.IsAuthoritativeStudentWorkspace);
        _prototypePanel.SetVisible(_viewModel.IsPrototypeMode);
        _unavailablePanel.SetVisible(_viewModel.IsStartupUnavailable);
        _unavailableStatusLabel.SetText(_viewModel.StartupStatusText);

        if (_viewModel.IsAuthoritativeStudentWorkspace)
        {
            RefreshAuthoritative();
        }
    }

    private void RefreshAuthoritative()
    {
        _studentNameLabel.SetText(_viewModel.SelectedStudent?.DisplayName ?? "未选择学生");
        _teachingContextLabel.SetText(_viewModel.SelectedTeachingContext?.DisplayName ?? string.Empty);

        _focusStatusLabel.SetText(_viewModel.CurrentFocusStatusText);
        _focusStatusLabel.SetVisible(!string.IsNullOrWhiteSpace(_viewModel.CurrentFocusStatusText));

        GtkUiHelpers.ClearBoxChildren(_focusList);
        foreach (var item in _viewModel.CurrentFocus)
        {
            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);
            row.SetMarginTop(6);
            row.SetMarginBottom(6);

            var left = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
            left.SetHexpand(true);
            left.Append(GtkUiHelpers.CreateHeading(item.Title, "heading"));
            left.Append(GtkUiHelpers.CreateDimLabel(item.NextAction));

            var right = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
            right.SetHalign(Gtk.Align.End);
            var state = GtkUiHelpers.CreateDimLabel(item.StateLabel);
            state.SetHalign(Gtk.Align.End);
            var due = GtkUiHelpers.CreateDimLabel(item.DueLabel, caption: true);
            due.SetHalign(Gtk.Align.End);
            right.Append(state);
            right.Append(due);

            row.Append(left);
            row.Append(right);
            _focusList.Append(row);
            _focusList.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }

        _observationsStatusLabel.SetText(_viewModel.RecentObservationsStatusText);
        _observationsStatusLabel.SetVisible(!string.IsNullOrWhiteSpace(_viewModel.RecentObservationsStatusText));

        GtkUiHelpers.ClearBoxChildren(_observationsList);
        foreach (var obs in _viewModel.RecentObservations)
        {
            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
            row.SetMarginTop(6);
            row.SetMarginBottom(6);

            var bullet = GtkUiHelpers.CreateDimLabel("●", caption: true);
            bullet.SetValign(Gtk.Align.Start);
            var body = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
            body.SetHexpand(true);
            body.Append(GtkUiHelpers.CreateHeading("课堂观察", "heading"));
            body.Append(GtkUiHelpers.CreateBodyLabel(obs.RawText));
            body.Append(GtkUiHelpers.CreateDimLabel(obs.CreatedAtServer.ToString("yyyy-MM-dd HH:mm"), caption: true));

            row.Append(bullet);
            row.Append(body);
            _observationsList.Append(row);
            _observationsList.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }

        _observationsMoreLabel.SetText(_viewModel.RecentHistoryMoreText);
        _observationsMoreLabel.SetVisible(!string.IsNullOrWhiteSpace(_viewModel.RecentHistoryMoreText));
    }
}
