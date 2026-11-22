using System.Collections.Specialized;
using System.ComponentModel;
using Xueqing.Linux.Infrastructure.LocalData;
using Xueqing.Linux.Infrastructure.Security;
using Xueqing.Linux.UI;
using Xueqing.Windows.Core.Agent;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.LocalData;
using Xueqing.Windows.Infrastructure.Sync;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Linux.Views;

internal sealed class StudentsView
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Gtk.Widget _dialogParent;
    private readonly Adw.NavigationSplitView _splitView;

    // Sidebar
    private readonly Gtk.Label _studentCountLabel;
    private readonly Gtk.SearchEntry _searchEntry;
    private readonly Gtk.StringList _studentIdsModel;
    private readonly Gtk.SingleSelection _studentSelection;
    private readonly Gtk.ListView _studentListView;
    private readonly Gtk.Widget _studentListScroll;
    private readonly Gtk.Label _unavailableLabel;
    private readonly Dictionary<string, StudentSummary> _studentsById = new(StringComparer.Ordinal);

    // Detail header
    private readonly Gtk.Label _detailNameLabel;
    private readonly Gtk.Label _detailSubjectLabel;
    private readonly Gtk.Button _refreshButton;

    // Prototype detail panel + real encrypted MCP-interoperable local draft
    private readonly Gtk.Box _emptySelectionCard;
    private readonly Gtk.Box _prototypeDetailPanel;
    private readonly Gtk.TextView _protoDraftTextView;
    private readonly Gtk.Label _protoDraftStatusLabel;
    private readonly Gtk.Button _protoDraftSaveButton;
    private readonly Gtk.Button _protoDraftReloadButton;
    private readonly Gtk.Label _protoDeepLinkLabel;
    private readonly PaperLongitudinalPanel _protoPaperPanel;
    private readonly PaperLongitudinalPanel _authPaperPanel;
    private SqliteObservationDraftStore? _prototypeDraftStore;
    private ObservationDraftScope? _prototypeDraftScope;
    private string? _lastLoadedProtoStudentId;
    private long _prototypeDraftEpoch;
    private long _prototypeDraftLoadVersion;
    private bool _suppressProtoDraftSync;
    private bool _protoImeComposing;
    private readonly CoalescedUiAction _coalescedSyncStudents;
    private readonly CoalescedUiAction _coalescedSyncContexts;
    private readonly CoalescedUiAction _coalescedRebuildFocus;
    private readonly CoalescedUiAction _coalescedRebuildCases;
    private readonly CoalescedUiAction _coalescedRebuildObservations;

    // Authoritative detail panel
    private readonly Gtk.Box _authoritativeDetailPanel;
    private readonly Gtk.DropDown _subjectContextDropDown;
    private readonly Gtk.Box _observationCaptureSection;
    private readonly Gtk.TextView _observationDraftTextView;
    private readonly Gtk.Label _observationDraftStatusLabel;
    private readonly Gtk.Button _observationSubmitButton;
    private readonly Gtk.Label _currentFocusStatusLabel;
    private readonly Gtk.Box _currentFocusList;
    private readonly Gtk.Button _loadCaseHistoryButton;
    private readonly Gtk.Label _caseHistoryStatusLabel;
    private readonly Gtk.Box _caseHistoryList;
    private readonly Gtk.Label _caseHistoryMoreLabel;
    private readonly Gtk.Label _recentObservationsStatusLabel;
    private readonly Gtk.Box _recentObservationsList;
    private readonly Gtk.Label _recentHistoryMoreLabel;

    private readonly List<TeachingContextOption> _subjectContextOptions = new();
    private bool _suppressSelectionSync;
    private bool _suppressContextSync;
    private bool _suppressDraftSync;
    private bool _isImeComposing;

    public StudentsView(MainWindowViewModel viewModel, Gtk.Widget dialogParent)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _dialogParent = dialogParent ?? throw new ArgumentNullException(nameof(dialogParent));
        _coalescedSyncStudents = new CoalescedUiAction(SyncStudentsListFromViewModel);
        _coalescedSyncContexts = new CoalescedUiAction(SyncTeachingContextsFromViewModel);
        _coalescedRebuildFocus = new CoalescedUiAction(RebuildCurrentFocus);
        _coalescedRebuildCases = new CoalescedUiAction(RebuildCaseHistory);
        _coalescedRebuildObservations = new CoalescedUiAction(RebuildRecentObservations);

        _splitView = Adw.NavigationSplitView.New();
        _splitView.SetMinSidebarWidth(300);
        _splitView.SetMaxSidebarWidth(380);

        // --- Sidebar ---
        var sidebarBox = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        sidebarBox.SetMarginStart(14);
        sidebarBox.SetMarginEnd(14);
        sidebarBox.SetMarginTop(14);
        sidebarBox.SetMarginBottom(14);

        var headerRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        var heading = GtkUiHelpers.CreateHeading("学生", "title-3");
        heading.SetHexpand(true);
        GtkUiHelpers.SetAutomationId(heading, "StudentsSurface");
        _studentCountLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        headerRow.Append(heading);
        headerRow.Append(_studentCountLabel);
        sidebarBox.Append(headerRow);

        _searchEntry = Gtk.SearchEntry.New();
        _searchEntry.SetPlaceholderText(_viewModel.SearchPlaceholderText);
        GtkUiHelpers.SetAutomationId(_searchEntry, "StudentSearchBox");
        _searchEntry.OnSearchChanged += (_, _) =>
        {
            _viewModel.FilterStudents(_searchEntry.GetText());
            _coalescedSyncStudents.RunNow();
        };
        sidebarBox.Append(_searchEntry);

        _studentIdsModel = Gtk.StringList.New(null);
        _studentSelection = Gtk.SingleSelection.New(_studentIdsModel);
        _studentSelection.SetAutoselect(false);
        _studentSelection.SetCanUnselect(true);

        var factory = Gtk.SignalListItemFactory.New();
        factory.OnSetup += (_, args) =>
        {
            if (args.Object is not Gtk.ListItem listItem)
            {
                return;
            }

            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            row.SetMarginStart(8);
            row.SetMarginEnd(8);
            row.SetMarginTop(6);
            row.SetMarginBottom(6);

            var left = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
            left.SetHexpand(true);
            var nameLabel = Gtk.Label.New(string.Empty);
            nameLabel.SetHalign(Gtk.Align.Start);
            nameLabel.SetXalign(0f);
            nameLabel.AddCssClass("heading");
            var secondaryLabel = Gtk.Label.New(string.Empty);
            secondaryLabel.SetHalign(Gtk.Align.Start);
            secondaryLabel.SetXalign(0f);
            secondaryLabel.AddCssClass("dim-label");
            secondaryLabel.AddCssClass("caption");
            left.Append(nameLabel);
            left.Append(secondaryLabel);

            var right = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
            var subjectLabel = Gtk.Label.New(string.Empty);
            subjectLabel.SetHalign(Gtk.Align.End);
            var casesLabel = Gtk.Label.New(string.Empty);
            casesLabel.SetHalign(Gtk.Align.End);
            casesLabel.AddCssClass("dim-label");
            casesLabel.AddCssClass("caption");
            right.Append(subjectLabel);
            right.Append(casesLabel);

            row.Append(left);
            row.Append(right);
            listItem.SetChild(row);
        };

        factory.OnBind += (_, args) =>
        {
            if (args.Object is not Gtk.ListItem listItem ||
                listItem.GetItem() is not Gtk.StringObject idObj ||
                listItem.GetChild() is not Gtk.Box row ||
                !_studentsById.TryGetValue(idObj.GetString(), out var student))
            {
                return;
            }

            if (row.GetFirstChild() is Gtk.Box left &&
                left.GetFirstChild() is Gtk.Label nameLabel &&
                nameLabel.GetNextSibling() is Gtk.Label secondaryLabel)
            {
                nameLabel.SetText(student.DisplayName);
                secondaryLabel.SetText(student.SecondaryLabel);
            }

            if (row.GetLastChild() is Gtk.Box right &&
                right.GetFirstChild() is Gtk.Label subjectLabel &&
                subjectLabel.GetNextSibling() is Gtk.Label casesLabel)
            {
                subjectLabel.SetText(student.PrimarySubject);
                casesLabel.SetText(student.ActiveCaseCountLabel);
            }
        };

        _studentListView = Gtk.ListView.New(_studentSelection, factory);
        _studentListView.AddCssClass("navigation-sidebar");
        _studentListView.SetSingleClickActivate(false);
        GtkUiHelpers.SetAutomationId(_studentListView, "StudentList");

        _studentSelection.OnSelectionChanged += async (_, _) =>
        {
            if (_suppressSelectionSync)
            {
                return;
            }

            await HandleStudentSelectionChangedAsync();
        };

        _studentListView.OnActivate += (_, _) =>
        {
            if (_splitView.GetCollapsed())
            {
                _splitView.SetShowContent(true);
            }
        };

        _studentListScroll = GtkUiHelpers.WrapInScroll(_studentListView);
        sidebarBox.Append(_studentListScroll);

        _unavailableLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        GtkUiHelpers.SetAutomationId(_unavailableLabel, "StudentWorkspaceUnavailable");
        sidebarBox.Append(_unavailableLabel);

        // --- Detail Pane ---
        var detailBox = Gtk.Box.New(Gtk.Orientation.Vertical, 18);
        detailBox.SetMarginStart(24);
        detailBox.SetMarginEnd(24);
        detailBox.SetMarginTop(20);
        detailBox.SetMarginBottom(24);

        var detailHeaderRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
        var detailTitleBox = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
        detailTitleBox.SetHexpand(true);
        _detailNameLabel = GtkUiHelpers.CreateHeading(string.Empty, "title-2");
        GtkUiHelpers.SetAutomationId(_detailNameLabel, "StudentDetailHeading");
        _detailSubjectLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        detailTitleBox.Append(_detailNameLabel);
        detailTitleBox.Append(_detailSubjectLabel);

        _refreshButton = Gtk.Button.NewWithLabel("刷新");
        _refreshButton.SetValign(Gtk.Align.Start);
        GtkUiHelpers.SetAutomationId(_refreshButton, "RefreshAuthoritativeStudents");
        _refreshButton.OnClicked += async (_, _) => await _viewModel.RefreshAuthoritativeStudentsAsync();

        detailHeaderRow.Append(detailTitleBox);
        detailHeaderRow.Append(_refreshButton);
        detailBox.Append(detailHeaderRow);

        // Empty selection state card (prevents stale panels showing when SelectedStudent is null)
        _emptySelectionCard = GtkUiHelpers.CreateCardBox(spacing: 8, cardCss: "xq-empty-card");
        _emptySelectionCard.Append(GtkUiHelpers.CreateHeading("请选择一位学生", "title-4"));
        _emptySelectionCard.Append(GtkUiHelpers.CreateDimLabel("从左侧名单中选择学生，或使用顶部搜索框快速定位，即可查看历次试卷纵向趋势、AI 阶段复盘与课堂跟进记录。"));
        _emptySelectionCard.SetVisible(false);
        detailBox.Append(_emptySelectionCard);

        // Prototype Detail Panel (Hero: Paper Longitudinal Panel at the top, followed by Focus & Quick Observation)
        _protoPaperPanel = new PaperLongitudinalPanel(_dialogParent, HandleAdoptProposedActionAsync);
        _prototypeDetailPanel = BuildPrototypeDetailPanel(
            _protoPaperPanel,
            out _protoDraftTextView,
            out _protoDraftStatusLabel,
            out _protoDraftSaveButton,
            out _protoDraftReloadButton,
            out _protoDeepLinkLabel);
        detailBox.Append(_prototypeDetailPanel);

        // Authoritative Detail Panel (Production mode with real Paper Longitudinal Panel at primary position!)
        _authoritativeDetailPanel = Gtk.Box.New(Gtk.Orientation.Vertical, 18);

        var contextRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        contextRow.Append(GtkUiHelpers.CreateHeading("任教学科", "heading"));
        _subjectContextDropDown = Gtk.DropDown.NewFromStrings(Array.Empty<string>());
        _subjectContextDropDown.SetHexpand(true);
        GtkUiHelpers.SetAutomationId(_subjectContextDropDown, "StudentSubjectContext");
        _subjectContextDropDown.OnNotify += async (_, args) =>
        {
            if (_suppressContextSync || args.Pspec.GetName() != "selected")
            {
                return;
            }

            await HandleSubjectContextChangedAsync();
        };
        contextRow.Append(_subjectContextDropDown);
        _authoritativeDetailPanel.Append(contextRow);

        _authPaperPanel = new PaperLongitudinalPanel(_dialogParent, HandleAdoptProposedActionAsync);
        _authoritativeDetailPanel.Append(_authPaperPanel.Widget);
        _authoritativeDetailPanel.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));

        // Quick Capture (Durable Encrypted Draft + Explicit Submit)
        _observationCaptureSection = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        _observationCaptureSection.Append(GtkUiHelpers.CreateHeading("课堂观察", "title-4"));
        _observationCaptureSection.Append(
            GtkUiHelpers.CreateMultilineTextFrame(out _observationDraftTextView, minHeight: 104));
        GtkUiHelpers.SetAutomationId(_observationDraftTextView, "ObservationDraftText");
        WireAuthoritativeObservationDraftEvents();

        var captureFooter = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
        _observationDraftStatusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        _observationDraftStatusLabel.SetHexpand(true);
        GtkUiHelpers.SetAutomationId(_observationDraftStatusLabel, "ObservationDraftStatus");

        _observationSubmitButton = Gtk.Button.NewWithLabel("提交");
        _observationSubmitButton.AddCssClass("suggested-action");
        GtkUiHelpers.SetAutomationId(_observationSubmitButton, "ObservationSubmit");
        _observationSubmitButton.OnClicked += async (_, _) => await _viewModel.SubmitObservationDraftAsync();

        captureFooter.Append(_observationDraftStatusLabel);
        captureFooter.Append(_observationSubmitButton);
        _observationCaptureSection.Append(captureFooter);
        _observationCaptureSection.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        _authoritativeDetailPanel.Append(_observationCaptureSection);

        // Current Focus
        var focusSection = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        focusSection.Append(GtkUiHelpers.CreateHeading("正在关注", "title-4"));
        _currentFocusStatusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        GtkUiHelpers.SetAutomationId(_currentFocusStatusLabel, "CurrentFocusStatus");
        _currentFocusList = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        focusSection.Append(_currentFocusStatusLabel);
        focusSection.Append(_currentFocusList);
        focusSection.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        _authoritativeDetailPanel.Append(focusSection);

        // Case History (with explicit CaseLifecycle buttons!)
        var caseSection = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        var caseHeader = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
        var caseHeading = GtkUiHelpers.CreateHeading("全部跟进课题", "title-4");
        caseHeading.SetHexpand(true);
        _loadCaseHistoryButton = Gtk.Button.NewWithLabel("查看全部");
        GtkUiHelpers.SetAutomationId(_loadCaseHistoryButton, "LoadCaseHistory");
        _loadCaseHistoryButton.OnClicked += async (_, _) => await _viewModel.LoadSelectedCaseHistoryAsync();
        caseHeader.Append(caseHeading);
        caseHeader.Append(_loadCaseHistoryButton);
        caseSection.Append(caseHeader);

        _caseHistoryStatusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        GtkUiHelpers.SetAutomationId(_caseHistoryStatusLabel, "CaseHistoryStatus");
        _caseHistoryList = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        _caseHistoryMoreLabel = GtkUiHelpers.CreateDimLabel(string.Empty, caption: true);
        caseSection.Append(_caseHistoryStatusLabel);
        caseSection.Append(_caseHistoryList);
        caseSection.Append(_caseHistoryMoreLabel);
        caseSection.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        _authoritativeDetailPanel.Append(caseSection);

        // Recent Observations
        var obsSection = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        obsSection.Append(GtkUiHelpers.CreateHeading("最近观察", "title-4"));
        _recentObservationsStatusLabel = GtkUiHelpers.CreateDimLabel(string.Empty);
        GtkUiHelpers.SetAutomationId(_recentObservationsStatusLabel, "RecentObservationStatus");
        _recentObservationsList = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        _recentHistoryMoreLabel = GtkUiHelpers.CreateDimLabel(string.Empty, caption: true);
        obsSection.Append(_recentObservationsStatusLabel);
        obsSection.Append(_recentObservationsList);
        obsSection.Append(_recentHistoryMoreLabel);
        _authoritativeDetailPanel.Append(obsSection);

        detailBox.Append(_authoritativeDetailPanel);

        var detailClamp = Adw.Clamp.New();
        detailClamp.SetMaximumSize(880);
        detailClamp.SetChild(detailBox);

        var sidebarPage = Adw.NavigationPage.New(sidebarBox, "学生列表");
        var detailPage = Adw.NavigationPage.New(GtkUiHelpers.WrapInScroll(detailClamp), "学生详情");
        _splitView.SetSidebar(sidebarPage);
        _splitView.SetContent(detailPage);

        // Keyboard accelerators: Ctrl+F focuses search; Alt+Left returns to list
        var keyController = Gtk.EventControllerKey.New();
        keyController.OnKeyPressed += (_, args) =>
        {
            var ctrl = (args.State & Gdk.ModifierType.ControlMask) != 0;
            var alt = (args.State & Gdk.ModifierType.AltMask) != 0;
            if (ctrl && (args.Keyval == Gdk.Constants.KEY_f || args.Keyval == Gdk.Constants.KEY_F))
            {
                if (_splitView.GetCollapsed())
                {
                    _splitView.SetShowContent(false);
                }

                _searchEntry.GrabFocus();
                return true;
            }

            if (alt && args.Keyval == Gdk.Constants.KEY_Left && _splitView.GetCollapsed())
            {
                _splitView.SetShowContent(false);
                _studentListView.GrabFocus();
                return true;
            }

            return false;
        };
        _splitView.AddController(keyController);

        // Subscribe to ViewModel changes
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Students.CollectionChanged += (_, _) => _coalescedSyncStudents.Schedule();
        _viewModel.SelectedTeachingContexts.CollectionChanged += (_, _) => _coalescedSyncContexts.Schedule();
        _viewModel.CurrentFocus.CollectionChanged += (_, _) => _coalescedRebuildFocus.Schedule();
        _viewModel.CaseHistory.CollectionChanged += (_, _) => _coalescedRebuildCases.Schedule();
        _viewModel.RecentObservations.CollectionChanged += (_, _) => _coalescedRebuildObservations.Schedule();

        _coalescedSyncStudents.RunNow();
        _coalescedSyncContexts.RunNow();
        RefreshAllDetailBindings();
    }

    public Gtk.Widget Widget => _splitView;

    public void ApplyLayout(WindowLayoutMode mode)
    {
        var collapsed = mode == WindowLayoutMode.Compact;
        if (_splitView.GetCollapsed() != collapsed)
        {
            _splitView.SetCollapsed(collapsed);
            if (collapsed && _viewModel.SelectedStudent is not null)
            {
                _splitView.SetShowContent(true);
            }
        }
    }

    public void FilterStudentsProgrammatically(string query)
    {
        _searchEntry.SetText(query ?? string.Empty);
        _viewModel.FilterStudents(query);
        _coalescedSyncStudents.RunNow();
    }

    public void SyncSelectedStudentHighlight(bool revealDetailIfCollapsed = false)
    {
        _coalescedSyncStudents.RunNow();
        SyncSelectionFromViewModel();
        RefreshDetailHeader();
        if (_viewModel.IsPrototypeMode)
        {
            _ = LoadPrototypeDraftForSelectedStudentAsync(forceReload: false);
        }

        if (revealDetailIfCollapsed && _splitView.GetCollapsed() && _viewModel.SelectedStudent is not null)
        {
            _splitView.SetShowContent(true);
        }
    }

    private Gtk.Box BuildPrototypeDetailPanel(
        PaperLongitudinalPanel paperPanel,
        out Gtk.TextView draftTextView,
        out Gtk.Label draftStatusLabel,
        out Gtk.Button saveBtn,
        out Gtk.Button reloadBtn,
        out Gtk.Label deepLinkLabel)
    {
        var panel = Gtk.Box.New(Gtk.Orientation.Vertical, 18);

        // Hero section: Longitudinal Exam Paper Timeline & AI Stage Review at the top!
        panel.Append(paperPanel.Widget);
        panel.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));

        // Focus & Next Action Cards (crisp non-wrapping pill badges!)
        var focusBox = Gtk.Box.New(Gtk.Orientation.Vertical, 10);
        focusBox.Append(GtkUiHelpers.CreateHeading("当前重点跟进", "title-4"));
        focusBox.Append(CreateProtoRow("阅读材料的信息关系组织仍需验证", "最近观察中，限制条件能够定位，但因果关系仍需要提示。", "待验证", "xq-pill-draft"));
        focusBox.Append(CreateProtoRow("相同策略在短文本中能够独立使用", "迁移到更长的陌生材料时仍需继续观察。", "跟进中", "xq-pill-ai"));
        panel.Append(focusBox);

        var nextBox = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        nextBox.Append(GtkUiHelpers.CreateHeading("下一步行动", "title-4"));
        nextBox.Append(CreateProtoRow("使用新的陌生材料验证迁移", "验证是否能够在新的阅读材料中独立组织信息关系。", "待安排", "xq-pill-neutral"));
        panel.Append(nextBox);

        // Calm Quick Observation Draft Card (de-jargonized title!)
        var draftSection = GtkUiHelpers.CreateCardBox(spacing: 8);
        var draftHeader = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        var draftHeading = GtkUiHelpers.CreateHeading("随手课堂观察", "title-4");
        draftHeading.SetHexpand(true);
        draftHeader.Append(draftHeading);
        draftHeader.Append(GtkUiHelpers.CreateBadge("本机安全保存", "xq-pill-confirmed"));
        draftSection.Append(draftHeader);

        draftSection.Append(GtkUiHelpers.CreateMultilineTextFrame(out draftTextView, minHeight: 88));
        GtkUiHelpers.SetAutomationId(draftTextView, "ObservationDraftText");

        var textViewRef = draftTextView;
        draftTextView.OnPreeditChanged += (_, args) =>
        {
            _protoImeComposing = !string.IsNullOrEmpty(args.Preedit);
        };

        var draftFooter = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        draftStatusLabel = GtkUiHelpers.CreateDimLabel("正在读取本机草稿…", caption: true);
        draftStatusLabel.SetHexpand(true);
        GtkUiHelpers.SetAutomationId(draftStatusLabel, "ObservationDraftStatus");

        reloadBtn = Gtk.Button.NewWithLabel("重新读取");
        reloadBtn.OnClicked += async (_, _) => await LoadPrototypeDraftForSelectedStudentAsync(forceReload: true);

        saveBtn = Gtk.Button.NewWithLabel("保存草稿");
        saveBtn.AddCssClass("suggested-action");
        saveBtn.OnClicked += async (_, _) => await SavePrototypeDraftAsync(GtkUiHelpers.GetText(textViewRef));

        draftFooter.Append(draftStatusLabel);
        draftFooter.Append(reloadBtn);
        draftFooter.Append(saveBtn);
        draftSection.Append(draftFooter);

        deepLinkLabel = GtkUiHelpers.CreateDimLabel(string.Empty, caption: true);
        draftSection.Append(deepLinkLabel);
        panel.Append(draftSection);

        var timelineBox = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        timelineBox.Append(GtkUiHelpers.CreateHeading("近期教学脉络", "title-4"));
        timelineBox.Append(CreateProtoRow("9 月 17 日 · 课堂观察", "能主动定位限制条件，但因果关系组织仍需要提示。", string.Empty, "xq-pill-neutral"));
        timelineBox.Append(CreateProtoRow("9 月 14 日 · 干预", "重新示范信息之间的因果连接方式，并使用短文本练习。", string.Empty, "xq-pill-neutral"));
        timelineBox.Append(CreateProtoRow("9 月 10 日 · 验证", "相同策略在短文本中已经能够独立使用。", string.Empty, "xq-pill-neutral"));
        panel.Append(timelineBox);

        return panel;
    }

    private static Gtk.Widget CreateProtoRow(string heading, string body, string badge, string badgeCss)
    {
        var card = GtkUiHelpers.CreateCardBox(spacing: 4);
        var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 14);
        var left = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
        left.SetHexpand(true);
        left.Append(GtkUiHelpers.CreateHeading(heading, "heading"));
        left.Append(GtkUiHelpers.CreateDimLabel(body));
        row.Append(left);
        if (!string.IsNullOrEmpty(badge))
        {
            row.Append(GtkUiHelpers.CreateBadge(badge, badgeCss));
        }

        card.Append(row);
        return card;
    }

    private void WireAuthoritativeObservationDraftEvents()
    {
        var buffer = _observationDraftTextView.GetBuffer();
        _observationDraftTextView.OnPreeditChanged += async (_, args) =>
        {
            var wasComposing = _isImeComposing;
            _isImeComposing = !string.IsNullOrEmpty(args.Preedit);
            if (wasComposing && !_isImeComposing)
            {
                await PushAuthoritativeDraftIfChangedAsync();
            }
        };

        if (buffer is not null)
        {
            buffer.OnChanged += async (_, _) =>
            {
                if (_suppressDraftSync || _isImeComposing)
                {
                    return;
                }

                await PushAuthoritativeDraftIfChangedAsync();
            };
        }
    }

    private async Task PushAuthoritativeDraftIfChangedAsync()
    {
        if (!_viewModel.SupportsObservationCapture)
        {
            return;
        }

        var current = GtkUiHelpers.GetText(_observationDraftTextView);
        if (string.Equals(current, _viewModel.ObservationDraftText, StringComparison.Ordinal))
        {
            return;
        }

        await _viewModel.UpdateObservationDraftAsync(current);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.SelectedStudent):
                SyncSelectionFromViewModel();
                RefreshDetailHeader();
                if (_viewModel.IsPrototypeMode)
                {
                    _ = LoadPrototypeDraftForSelectedStudentAsync();
                }

                break;

            case nameof(MainWindowViewModel.SelectedTeachingContext):
                SyncSelectedContextFromViewModel();
                if (_viewModel.IsAuthoritativeStudentWorkspace)
                {
                    _ = LoadAuthoritativePaperPanelAsync();
                }

                break;

            case nameof(MainWindowViewModel.ObservationDraftText):
                SyncObservationDraftTextFromViewModel();
                break;

            case nameof(MainWindowViewModel.ObservationDraftStatusText):
            case nameof(MainWindowViewModel.CanEditObservationDraft):
            case nameof(MainWindowViewModel.CanSubmitObservationDraft):
            case nameof(MainWindowViewModel.ObservationSubmitLabel):
            case nameof(MainWindowViewModel.SupportsObservationCapture):
                RefreshObservationCaptureControls();
                break;

            case nameof(MainWindowViewModel.CurrentFocusStatusText):
            case nameof(MainWindowViewModel.SupportsActionProgression):
                _currentFocusStatusLabel.SetText(_viewModel.CurrentFocusStatusText);
                RebuildCurrentFocus();
                break;

            case nameof(MainWindowViewModel.CaseHistoryStatusText):
            case nameof(MainWindowViewModel.CaseHistoryMoreText):
            case nameof(MainWindowViewModel.SupportsCaseLifecycle):
                _caseHistoryStatusLabel.SetText(_viewModel.CaseHistoryStatusText);
                _caseHistoryMoreLabel.SetText(_viewModel.CaseHistoryMoreText);
                RebuildCaseHistory();
                break;

            case nameof(MainWindowViewModel.RecentObservationsStatusText):
            case nameof(MainWindowViewModel.RecentHistoryMoreText):
                _recentObservationsStatusLabel.SetText(_viewModel.RecentObservationsStatusText);
                _recentHistoryMoreLabel.SetText(_viewModel.RecentHistoryMoreText);
                break;

            case nameof(MainWindowViewModel.StartupStatusText):
            case nameof(MainWindowViewModel.IsStartupUnavailable):
                RefreshAllDetailBindings();
                break;
        }
    }

    private void SyncStudentsListFromViewModel()
    {
        _suppressSelectionSync = true;
        try
        {
            _studentsById.Clear();
            var ids = new string[_viewModel.Students.Count];
            for (var i = 0; i < _viewModel.Students.Count; i++)
            {
                var student = _viewModel.Students[i];
                _studentsById[student.Id] = student;
                ids[i] = student.Id;
            }

            _studentIdsModel.Splice(0, _studentIdsModel.GetNItems(), ids);
            _studentCountLabel.SetText($"{ids.Length}");
        }
        finally
        {
            _suppressSelectionSync = false;
        }

        SyncSelectionFromViewModel();
        RefreshDetailHeader();
        if (_viewModel.IsPrototypeMode)
        {
            _ = LoadPrototypeDraftForSelectedStudentAsync(forceReload: false);
        }
    }

    private void SyncSelectionFromViewModel()
    {
        _suppressSelectionSync = true;
        try
        {
            var selected = _viewModel.SelectedStudent;
            if (selected is null)
            {
                _studentSelection.SetSelected(Gtk.Constants.INVALID_LIST_POSITION);
                return;
            }

            var index = -1;
            for (var i = 0; i < _viewModel.Students.Count; i++)
            {
                if (string.Equals(_viewModel.Students[i].Id, selected.Id, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            _studentSelection.SetSelected(index >= 0 ? (uint)index : Gtk.Constants.INVALID_LIST_POSITION);
        }
        finally
        {
            _suppressSelectionSync = false;
        }
    }

    private async Task HandleStudentSelectionChangedAsync()
    {
        if (_studentSelection.GetSelectedItem() is Gtk.StringObject idObj &&
            _studentsById.TryGetValue(idObj.GetString(), out var student))
        {
            _viewModel.SelectedStudent = student;
            RefreshDetailHeader();

            if (_splitView.GetCollapsed())
            {
                _splitView.SetShowContent(true);
            }

            if (_viewModel.IsAuthoritativeStudentWorkspace &&
                _viewModel.SelectedTeachingContexts.Count == 1 &&
                _viewModel.SelectedTeachingContext is not null)
            {
                await _viewModel.LoadSelectedTeachingContextAsync();
            }
            else if (_viewModel.IsPrototypeMode)
            {
                await LoadPrototypeDraftForSelectedStudentAsync();
            }
        }
        else
        {
            _viewModel.SelectedStudent = null;
            RefreshDetailHeader();
        }
    }

    private void SyncTeachingContextsFromViewModel()
    {
        _suppressContextSync = true;
        try
        {
            _subjectContextOptions.Clear();
            _subjectContextOptions.AddRange(_viewModel.SelectedTeachingContexts);
            var labels = _subjectContextOptions.Select(o => o.DisplayName).ToArray();
            _subjectContextDropDown.SetModel(Gtk.StringList.New(labels));
            _subjectContextDropDown.SetSensitive(labels.Length > 0);
            SyncSelectedContextFromViewModelCore();
        }
        finally
        {
            _suppressContextSync = false;
        }
    }

    private void SyncSelectedContextFromViewModel()
    {
        _suppressContextSync = true;
        try
        {
            SyncSelectedContextFromViewModelCore();
        }
        finally
        {
            _suppressContextSync = false;
        }
    }

    private void SyncSelectedContextFromViewModelCore()
    {
        if (_viewModel.SelectedTeachingContext is not { } selected)
        {
            return;
        }

        var idx = _subjectContextOptions.FindIndex(o => ReferenceEquals(o, selected) || o.Context.Equals(selected.Context));
        if (idx >= 0)
        {
            _subjectContextDropDown.SetSelected((uint)idx);
        }
    }

    private async Task HandleSubjectContextChangedAsync()
    {
        var idx = (int)_subjectContextDropDown.GetSelected();
        if (idx < 0 || idx >= _subjectContextOptions.Count)
        {
            return;
        }

        _viewModel.SelectedTeachingContext = _subjectContextOptions[idx];
        if (_viewModel.IsAuthoritativeStudentWorkspace &&
            _viewModel.SelectedTeachingContexts.Count > 1 &&
            _viewModel.SelectedTeachingContext is not null)
        {
            await _viewModel.LoadSelectedTeachingContextAsync();
        }
    }

    private void RefreshAllDetailBindings()
    {
        var unavailable = _viewModel.IsStartupUnavailable;
        _searchEntry.SetSensitive(!unavailable);
        _studentListScroll.SetVisible(!unavailable);
        _unavailableLabel.SetVisible(unavailable);
        _unavailableLabel.SetText(_viewModel.StartupStatusText);

        _refreshButton.SetVisible(_viewModel.IsAuthoritativeStudentWorkspace);
        RefreshDetailHeader();
        RefreshObservationCaptureControls();
        SyncObservationDraftTextFromViewModel();
        RebuildCurrentFocus();
        RebuildCaseHistory();
        RebuildRecentObservations();
    }

    private void RefreshDetailHeader()
    {
        var hasSelection = _viewModel.SelectedStudent is not null;
        if (_viewModel.SelectedStudent is { } student)
        {
            _detailNameLabel.SetText(student.DisplayName);
            _detailSubjectLabel.SetText(student.PrimarySubject);
        }
        else
        {
            _detailNameLabel.SetText(_viewModel.IsStartupUnavailable ? "教学工作区暂不可用" : "未选择学生");
            _detailSubjectLabel.SetText(_viewModel.IsStartupUnavailable ? _viewModel.StartupStatusText : "请在左侧选择或搜索学生。");
        }

        _emptySelectionCard.SetVisible(!hasSelection && !_viewModel.IsStartupUnavailable);
        _prototypeDetailPanel.SetVisible(hasSelection && _viewModel.IsPrototypeMode);
        _authoritativeDetailPanel.SetVisible(hasSelection && _viewModel.IsAuthoritativeStudentWorkspace);
    }

    private async Task LoadAuthoritativePaperPanelAsync()
    {
        if (!_viewModel.IsAuthoritativeStudentWorkspace ||
            _viewModel.SelectedTeachingContext is not { } ctx ||
            _viewModel.CurrentActorAppUserId is not { } actorId)
        {
            _authPaperPanel.RenderEmpty("选择学科后可查看历次试卷纵向分析与阶段复盘。");
            return;
        }

        await _authPaperPanel.LoadAuthoritativeAsync(
            new StudentLearningScope(
                ctx.Context.OrganizationId,
                ctx.Context.StudentId,
                ctx.Context.SubjectProfileId),
            actorId,
            _viewModel.PaperLongitudinalReader,
            _viewModel.PaperAttachmentDownloader,
            _viewModel.PaperExtractionTrigger,
            _viewModel.PaperExtractionConfirmer,
            _viewModel.StudyPlanDraftStatusUpdater);
    }

    private async Task<bool> HandleAdoptProposedActionAsync(StudyPlanProposedAction action)
    {
        if (_viewModel.IsPrototypeMode)
        {
            return await GtkUiHelpers.ShowConfirmDialogAsync(
                _dialogParent,
                "采纳为正式跟进课题",
                $"确定采纳建议「{action.Title}」？\n\n下一步行动：{action.NextAction}",
                "确认采纳",
                "取消");
        }

        var latestObs = _viewModel.RecentObservations.FirstOrDefault();
        if (latestObs is not null)
        {
            await CreateLearningCaseDialogFlow.ShowFromObservationAsync(
                _dialogParent,
                _viewModel,
                latestObs,
                prefillTitle: action.Title,
                prefillAction: action.NextAction);
            return true;
        }

        if (_viewModel.SupportsObservationCapture)
        {
            var draftText = $"[阶段复盘采纳] {action.Title}：{action.NextAction}";
            await _viewModel.UpdateObservationDraftAsync(draftText);
            SyncObservationDraftTextFromViewModel();
            await GtkUiHelpers.ShowMessageDialogAsync(
                _dialogParent,
                "已填入课堂观察草稿",
                "该学科下暂无已提交观察记录，已将建议内容预填至「课堂观察」草稿区，提交后即可直接转为正式跟进课题。");
            return true;
        }

        return false;
    }

    private void RefreshObservationCaptureControls()
    {
        _observationCaptureSection.SetVisible(_viewModel.SupportsObservationCapture);
        _observationDraftTextView.SetEditable(_viewModel.CanEditObservationDraft);
        _observationDraftTextView.SetSensitive(_viewModel.CanEditObservationDraft);
        _observationDraftStatusLabel.SetText(_viewModel.ObservationDraftStatusText);
        _observationSubmitButton.SetLabel(_viewModel.ObservationSubmitLabel);
        _observationSubmitButton.SetSensitive(_viewModel.CanSubmitObservationDraft);
    }

    private void SyncObservationDraftTextFromViewModel()
    {
        var current = GtkUiHelpers.GetText(_observationDraftTextView);
        if (string.Equals(current, _viewModel.ObservationDraftText, StringComparison.Ordinal))
        {
            return;
        }

        _suppressDraftSync = true;
        try
        {
            GtkUiHelpers.SetText(_observationDraftTextView, _viewModel.ObservationDraftText);
        }
        finally
        {
            _suppressDraftSync = false;
        }
    }

    private void RebuildCurrentFocus()
    {
        _currentFocusStatusLabel.SetText(_viewModel.CurrentFocusStatusText);
        _currentFocusStatusLabel.SetVisible(!string.IsNullOrWhiteSpace(_viewModel.CurrentFocusStatusText));

        GtkUiHelpers.ClearBoxChildren(_currentFocusList);
        foreach (var item in _viewModel.CurrentFocus)
        {
            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);
            row.SetMarginTop(6);
            row.SetMarginBottom(6);

            var left = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
            left.SetHexpand(true);
            left.Append(GtkUiHelpers.CreateHeading(item.Title, "heading"));
            left.Append(GtkUiHelpers.CreateDimLabel(item.NextAction));

            var right = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
            right.SetHalign(Gtk.Align.End);
            right.Append(GtkUiHelpers.CreateBadge(item.StateLabel, "xq-pill-draft"));
            var due = GtkUiHelpers.CreateDimLabel(item.DueLabel, caption: true);
            due.SetWrap(false);
            due.SetSingleLineMode(true);
            due.SetHalign(Gtk.Align.End);
            right.Append(due);

            if (_viewModel.SupportsActionProgression)
            {
                var btns = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
                btns.SetHalign(Gtk.Align.End);
                var rescheduleBtn = Gtk.Button.NewWithLabel("改期");
                var verifyBtn = Gtk.Button.NewWithLabel("验证");
                verifyBtn.AddCssClass("suggested-action");

                if (item.CaseId != Guid.Empty)
                {
                    GtkUiHelpers.SetAutomationId(rescheduleBtn, AgentAutomationIds.FocusReschedule(item.CaseId));
                    GtkUiHelpers.SetAutomationId(verifyBtn, AgentAutomationIds.FocusVerify(item.CaseId));
                }

                var captured = item;
                rescheduleBtn.OnClicked += async (_, _) =>
                {
                    var lookup = await _viewModel.FindFocusActionProgressionAsync(captured);
                    await ActionProgressionDialogFlow.ShowRescheduleAsync(_dialogParent, _viewModel, lookup);
                };
                verifyBtn.OnClicked += async (_, _) =>
                {
                    var lookup = await _viewModel.FindFocusActionProgressionAsync(captured);
                    await ActionProgressionDialogFlow.ShowVerificationAsync(_dialogParent, _viewModel, lookup);
                };

                btns.Append(rescheduleBtn);
                btns.Append(verifyBtn);
                right.Append(btns);
            }

            row.Append(left);
            row.Append(right);
            _currentFocusList.Append(row);
            _currentFocusList.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }
    }

    private void RebuildCaseHistory()
    {
        _caseHistoryStatusLabel.SetText(_viewModel.CaseHistoryStatusText);
        _caseHistoryStatusLabel.SetVisible(!string.IsNullOrWhiteSpace(_viewModel.CaseHistoryStatusText));
        _caseHistoryMoreLabel.SetText(_viewModel.CaseHistoryMoreText);
        _caseHistoryMoreLabel.SetVisible(!string.IsNullOrWhiteSpace(_viewModel.CaseHistoryMoreText));

        GtkUiHelpers.ClearBoxChildren(_caseHistoryList);
        foreach (var item in _viewModel.CaseHistory)
        {
            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);
            row.SetMarginTop(6);
            row.SetMarginBottom(6);

            var left = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
            left.SetHexpand(true);
            left.Append(GtkUiHelpers.CreateHeading(item.Title, "heading"));
            left.Append(GtkUiHelpers.CreateDimLabel(item.NextAction));

            var right = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
            right.SetHalign(Gtk.Align.End);
            right.Append(GtkUiHelpers.CreateBadge(
                item.StateLabel,
                item.IsClosed ? "xq-pill-neutral" : "xq-pill-ai"));
            var resp = GtkUiHelpers.CreateDimLabel(item.ResponsibilityLabel, caption: true);
            resp.SetWrap(false);
            resp.SetSingleLineMode(true);
            resp.SetHalign(Gtk.Align.End);
            var due = GtkUiHelpers.CreateDimLabel(item.DueLabel, caption: true);
            due.SetWrap(false);
            due.SetSingleLineMode(true);
            due.SetHalign(Gtk.Align.End);
            right.Append(resp);
            right.Append(due);

            if (_viewModel.SupportsCaseLifecycle && item.CanRunLifecycleAction)
            {
                var lifecycleBtn = Gtk.Button.NewWithLabel(item.LifecycleActionLabel);
                lifecycleBtn.SetHalign(Gtk.Align.End);
                if (item.CaseId != Guid.Empty)
                {
                    GtkUiHelpers.SetAutomationId(lifecycleBtn, AgentAutomationIds.CaseLifecycle(item.CaseId));
                }

                var captured = item;
                lifecycleBtn.OnClicked += async (_, _) =>
                {
                    var lookup = await _viewModel.FindCaseLifecycleAsync(captured);
                    await CaseLifecycleDialogFlow.ShowAsync(_dialogParent, _viewModel, lookup);
                };
                right.Append(lifecycleBtn);
            }

            row.Append(left);
            row.Append(right);
            _caseHistoryList.Append(row);
            _caseHistoryList.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }
    }

    private void RebuildRecentObservations()
    {
        _recentObservationsStatusLabel.SetText(_viewModel.RecentObservationsStatusText);
        _recentObservationsStatusLabel.SetVisible(!string.IsNullOrWhiteSpace(_viewModel.RecentObservationsStatusText));
        _recentHistoryMoreLabel.SetText(_viewModel.RecentHistoryMoreText);
        _recentHistoryMoreLabel.SetVisible(!string.IsNullOrWhiteSpace(_viewModel.RecentHistoryMoreText));

        GtkUiHelpers.ClearBoxChildren(_recentObservationsList);
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

            var createCaseBtn = Gtk.Button.NewWithLabel("形成学情问题");
            createCaseBtn.SetValign(Gtk.Align.Start);
            if (obs.ObservationId != Guid.Empty)
            {
                GtkUiHelpers.SetAutomationId(
                    createCaseBtn,
                    AgentAutomationIds.ObservationCreateLearningCase(obs.ObservationId));
            }

            var capturedObs = obs;
            createCaseBtn.OnClicked += async (_, _) =>
                await CreateLearningCaseDialogFlow.ShowFromObservationAsync(_dialogParent, _viewModel, capturedObs);

            row.Append(bullet);
            row.Append(body);
            row.Append(createCaseBtn);
            _recentObservationsList.Append(row);
            _recentObservationsList.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }
    }

    private SqliteObservationDraftStore GetOrCreatePrototypeDraftStore()
    {
        if (_prototypeDraftStore is not null)
        {
            return _prototypeDraftStore;
        }

        var path = LinuxLocalStatePaths.GetPrototypeMcpDraftDatabasePath();
        _prototypeDraftStore = new SqliteObservationDraftStore(path, new LinuxSecretServiceDatabaseKeyStore(path));
        return _prototypeDraftStore;
    }

    private async Task LoadPrototypeDraftForSelectedStudentAsync(bool forceReload = false)
    {
        if (_viewModel.SelectedStudent is not { } student)
        {
            _lastLoadedProtoStudentId = null;
            _prototypeDraftScope = null;
            _protoDeepLinkLabel.SetText(string.Empty);
            _protoDraftStatusLabel.SetText("选择学生后可查看或编辑本机加密草稿。");
            _protoDraftSaveButton.SetSensitive(false);
            return;
        }

        if (!forceReload && string.Equals(_lastLoadedProtoStudentId, student.Id, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoadedProtoStudentId = student.Id;
        var loadVersion = ++_prototypeDraftLoadVersion;
        await _protoPaperPanel.LoadPrototypeAsync(student);

        var scope = PrototypeDraftIdentity.CreateScopeForStudent(student.Id);
        _prototypeDraftScope = scope;
        _protoDeepLinkLabel.SetText($"深链：xueqing://student/{scope.StudentId:D}");

        try
        {
            var store = GetOrCreatePrototypeDraftStore();
            var opened = await store.OpenAsync(scope);
            if (loadVersion != _prototypeDraftLoadVersion)
            {
                return;
            }

            _prototypeDraftEpoch = opened.Epoch;
            var text = opened.Recovered?.Text ?? string.Empty;
            _suppressProtoDraftSync = true;
            try
            {
                GtkUiHelpers.SetText(_protoDraftTextView, text);
            }
            finally
            {
                _suppressProtoDraftSync = false;
            }

            _protoDraftTextView.SetEditable(true);
            _protoDraftTextView.SetSensitive(true);
            _protoDraftSaveButton.SetSensitive(true);
            _protoDraftStatusLabel.SetText(string.IsNullOrEmpty(text)
                ? $"加密草稿就绪（epoch={opened.Epoch}，原型模式仅保存在本机 SQLite3MC + libsecret）"
                : $"已加载本机加密草稿（epoch={opened.Epoch}，由本机或 Xueqing.Mcp 写入，待老师审核）");
        }
        catch (LocalDatabaseKeyUnavailableException ex)
        {
            if (loadVersion != _prototypeDraftLoadVersion)
            {
                return;
            }

            _protoDraftTextView.SetEditable(false);
            _protoDraftSaveButton.SetSensitive(false);
            _protoDraftStatusLabel.SetText($"本机加密草稿不可用（Fail-closed）：{ex.Message}");
        }
        catch (Exception ex)
        {
            if (loadVersion != _prototypeDraftLoadVersion)
            {
                return;
            }

            _protoDraftSaveButton.SetSensitive(false);
            _protoDraftStatusLabel.SetText($"读取加密草稿失败：{ex.Message}");
        }
    }

    private async Task SavePrototypeDraftAsync(string text)
    {
        if (_suppressProtoDraftSync || _protoImeComposing || _prototypeDraftScope is not { } scope)
        {
            return;
        }

        _protoDraftSaveButton.SetSensitive(false);
        try
        {
            var store = GetOrCreatePrototypeDraftStore();
            var saved = await store.SaveAsync(scope, _prototypeDraftEpoch, text, DateTimeOffset.UtcNow);
            if (saved)
            {
                _protoDraftStatusLabel.SetText(
                    $"已安全加密保存到本机（SQLite3MC + libsecret，epoch={_prototypeDraftEpoch}，未提交至服务器）");
            }
            else
            {
                _protoDraftStatusLabel.SetText(
                    "检测到草稿版本冲突（epoch 已更新），请点击“重新读取”后再保存。");
            }
        }
        catch (LocalDatabaseKeyUnavailableException ex)
        {
            _protoDraftStatusLabel.SetText($"保存失败（Fail-closed）：{ex.Message}");
        }
        catch (Exception ex)
        {
            _protoDraftStatusLabel.SetText($"保存加密草稿失败：{ex.Message}");
        }
        finally
        {
            _protoDraftSaveButton.SetSensitive(true);
        }
    }
}
