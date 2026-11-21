using System.Runtime.Versioning;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Linux.NativeSpike;

[SupportedOSPlatform("linux")]
internal sealed class LinuxNativeSpikeWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Adw.ApplicationWindow _window;
    private readonly Adw.NavigationSplitView _studentsSplitView;
    private readonly Gtk.StringList _studentIdsModel;
    private readonly Gtk.SingleSelection _studentSelection;
    private readonly Gtk.Label _studentCountLabel;
    private readonly Gtk.Label _detailNameLabel;
    private readonly Gtk.Label _detailMetaLabel;
    private readonly Gtk.Label _imeStatusLabel;
    private readonly Gtk.Label _securityStatusLabel;
    private readonly Gtk.Label _breakpointLabel;
    private readonly Gtk.TextView _captureTextView;
    private readonly Dictionary<string, StudentSummary> _studentsById = new(StringComparer.Ordinal);
    private bool _suppressSelectionSync;
    private int _colorSchemeIndex;

    public LinuxNativeSpikeWindow(Adw.Application application, MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        _window = Adw.ApplicationWindow.New(application);
        _window.SetTitle("学情 · Linux 真原生 Spike (GTK4 + libadwaita)");
        _window.SetDefaultSize(1320, 820);

        _studentIdsModel = Gtk.StringList.New(null);
        _studentSelection = Gtk.SingleSelection.New(_studentIdsModel);
        _studentCountLabel = Gtk.Label.New(string.Empty);
        _detailNameLabel = CreateHeadingLabel(string.Empty, "title-2");
        _detailMetaLabel = CreateDimLabel(string.Empty);
        _imeStatusLabel = CreateDimLabel("中文 IME 监听就绪：可在上方搜索框或下方课堂观察输入框测试拼音组合态");
        _securityStatusLabel = CreateDimLabel("点击右上角“验证本地加密”可在主线程异步验证 libsecret + SQLite3MC 2.4.0");
        _breakpointLabel = CreateDimLabel("布局断点：等待测量");
        _captureTextView = Gtk.TextView.New();
        _studentsSplitView = Adw.NavigationSplitView.New();

        var viewStack = Adw.ViewStack.New();
        viewStack.AddTitledWithIcon(BuildTodayPage(), "today", "今日", "view-list-symbolic");
        viewStack.AddTitledWithIcon(BuildStudentsPage(), "students", "学生", "system-users-symbolic");
        viewStack.AddTitledWithIcon(BuildLearningPage(), "learning", "学情", "document-edit-symbolic");
        viewStack.AddTitledWithIcon(BuildOrganizationPage(), "org", "成员与权限", "emblem-system-symbolic");
        viewStack.SetVisibleChildName("students");

        var switcher = Adw.ViewSwitcher.New();
        switcher.SetStack(viewStack);
        switcher.SetPolicy(Adw.ViewSwitcherPolicy.Wide);

        var headerBar = Adw.HeaderBar.New();
        headerBar.SetTitleWidget(switcher);

        var themeButton = Gtk.Button.NewWithLabel("切换亮/暗");
        themeButton.OnClicked += (_, _) => CycleColorScheme();
        headerBar.PackStart(themeButton);

        var securityButton = Gtk.Button.NewWithLabel("验证本地加密");
        securityButton.AddCssClass("suggested-action");
        securityButton.OnClicked += async (_, _) => await RunSecurityProbeAsync(securityButton);
        headerBar.PackEnd(securityButton);

        var toolbarView = Adw.ToolbarView.New();
        toolbarView.AddTopBar(headerBar);
        toolbarView.SetContent(viewStack);

        var statusBar = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);
        statusBar.SetMarginStart(14);
        statusBar.SetMarginEnd(14);
        statusBar.SetMarginTop(6);
        statusBar.SetMarginBottom(6);
        _breakpointLabel.SetHalign(Gtk.Align.Start);
        _securityStatusLabel.SetHalign(Gtk.Align.End);
        _securityStatusLabel.SetHexpand(true);
        statusBar.Append(_breakpointLabel);
        statusBar.Append(_securityStatusLabel);
        toolbarView.AddBottomBar(statusBar);

        _window.SetContent(toolbarView);

        _window.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "default-width")
            {
                ApplyResponsiveLayout();
            }
        };

        // Periodic width check handles tiling/Wayland resize smoothly in the spike
        GLib.Functions.TimeoutAdd(0, 250, () =>
        {
            if (!_window.GetVisible())
            {
                return false;
            }

            ApplyResponsiveLayout();
            return true;
        });

        SyncStudentsFromViewModel();
        ApplyResponsiveLayout();
    }

    public Adw.ApplicationWindow Window => _window;

    public void Present() => _window.Present();

    public void FilterStudentsForSmoke(string query)
    {
        _viewModel.FilterStudents(query);
        SyncStudentsFromViewModel();
    }

    private Gtk.Widget BuildStudentsPage()
    {
        // Sidebar: heading + search entry + virtualized Gtk.ListView (1,000 students)
        var sidebarBox = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        sidebarBox.SetMarginStart(12);
        sidebarBox.SetMarginEnd(12);
        sidebarBox.SetMarginTop(12);
        sidebarBox.SetMarginBottom(12);

        var headerRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        var heading = CreateHeadingLabel("学生", "title-3");
        heading.SetHexpand(true);
        _studentCountLabel.AddCssClass("dim-label");
        headerRow.Append(heading);
        headerRow.Append(_studentCountLabel);
        sidebarBox.Append(headerRow);

        var searchEntry = Gtk.SearchEntry.New();
        searchEntry.SetPlaceholderText("搜索学生姓名、编号或学科（可测中文 IME）");
        searchEntry.OnSearchChanged += (_, _) =>
        {
            _viewModel.FilterStudents(searchEntry.GetText());
            SyncStudentsFromViewModel();
        };
        sidebarBox.Append(searchEntry);

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
            nameLabel.AddCssClass("heading");
            var codeLabel = Gtk.Label.New(string.Empty);
            codeLabel.SetHalign(Gtk.Align.Start);
            codeLabel.AddCssClass("dim-label");
            codeLabel.AddCssClass("caption");
            left.Append(nameLabel);
            left.Append(codeLabel);

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
                nameLabel.GetNextSibling() is Gtk.Label codeLabel)
            {
                nameLabel.SetText(student.DisplayName);
                codeLabel.SetText(student.SecondaryLabel);
            }

            if (row.GetLastChild() is Gtk.Box right &&
                right.GetFirstChild() is Gtk.Label subjectLabel &&
                subjectLabel.GetNextSibling() is Gtk.Label casesLabel)
            {
                subjectLabel.SetText(student.PrimarySubject);
                casesLabel.SetText(student.ActiveCaseCountLabel);
            }
        };

        _studentSelection.SetAutoselect(true);
        _studentSelection.OnSelectionChanged += (_, _) =>
        {
            if (_suppressSelectionSync)
            {
                return;
            }

            UpdateSelectedStudentDetail();
        };

        var listView = Gtk.ListView.New(_studentSelection, factory);
        listView.AddCssClass("navigation-sidebar");
        listView.SetSingleClickActivate(false);

        var scrolledList = Gtk.ScrolledWindow.New();
        scrolledList.SetVexpand(true);
        scrolledList.SetPolicy(Gtk.PolicyType.Never, Gtk.PolicyType.Automatic);
        scrolledList.SetChild(listView);
        sidebarBox.Append(scrolledList);

        // Detail pane: selected student + Chinese IME multiline capture TextView
        var detailBox = Gtk.Box.New(Gtk.Orientation.Vertical, 14);
        detailBox.SetMarginStart(24);
        detailBox.SetMarginEnd(24);
        detailBox.SetMarginTop(20);
        detailBox.SetMarginBottom(20);

        detailBox.Append(_detailNameLabel);
        detailBox.Append(_detailMetaLabel);
        detailBox.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));

        var captureHeading = CreateHeadingLabel("快速记录课堂观察（中文 IME 组合态实测区）", "title-4");
        detailBox.Append(captureHeading);

        _captureTextView.SetWrapMode(Gtk.WrapMode.WordChar);
        _captureTextView.SetTopMargin(10);
        _captureTextView.SetBottomMargin(10);
        _captureTextView.SetLeftMargin(12);
        _captureTextView.SetRightMargin(12);
        _captureTextView.OnPreeditChanged += (_, args) =>
        {
            var preedit = args.Preedit ?? string.Empty;
            _imeStatusLabel.SetText(string.IsNullOrEmpty(preedit)
                ? "中文 IME 组合态已提交 / 空闲"
                : $"中文 IME 组合中（preedit）：「{preedit}」");
        };

        var captureFrame = Gtk.Frame.New(null);
        var captureScroll = Gtk.ScrolledWindow.New();
        captureScroll.SetMinContentHeight(140);
        captureScroll.SetChild(_captureTextView);
        captureFrame.SetChild(captureScroll);
        detailBox.Append(captureFrame);
        detailBox.Append(_imeStatusLabel);

        var detailActions = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        var previewDialogButton = Gtk.Button.NewWithLabel("打开原生确认弹窗");
        previewDialogButton.OnClicked += (_, _) =>
        {
            var dialog = Adw.AlertDialog.New(
                "验证原生 libadwaita 弹窗",
                $"当前选中学生：{_viewModel.SelectedStudent?.DisplayName ?? "未选择"}。\n弹窗关闭后键盘焦点应自然返回工作区。");
            dialog.AddResponse("close", "关闭");
            dialog.SetDefaultResponse("close");
            dialog.SetCloseResponse("close");
            dialog.Present(_window);
        };
        detailActions.Append(previewDialogButton);

        var clearDraftButton = Gtk.Button.NewWithLabel("清空输入框");
        clearDraftButton.OnClicked += (_, _) =>
        {
            _captureTextView.GetBuffer()?.SetText(string.Empty, 0);
        };
        detailActions.Append(clearDraftButton);
        detailBox.Append(detailActions);

        var detailScroll = Gtk.ScrolledWindow.New();
        detailScroll.SetVexpand(true);
        detailScroll.SetPolicy(Gtk.PolicyType.Never, Gtk.PolicyType.Automatic);
        detailScroll.SetChild(detailBox);

        var sidebarPage = Adw.NavigationPage.New(sidebarBox, "学生列表");
        var detailPage = Adw.NavigationPage.New(detailScroll, "学生详情");
        _studentsSplitView.SetMinSidebarWidth(300);
        _studentsSplitView.SetMaxSidebarWidth(380);
        _studentsSplitView.SetSidebar(sidebarPage);
        _studentsSplitView.SetContent(detailPage);
        return _studentsSplitView;
    }

    private Gtk.Widget BuildTodayPage()
    {
        var box = CreatePageContainer("今日行动队列（来自 MainWindowViewModel 原型数据）");
        foreach (var item in _viewModel.TodayActions)
        {
            var card = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
            card.SetMarginTop(6);
            card.SetMarginBottom(6);
            var title = CreateHeadingLabel($"[{item.BucketLabel}] {item.StudentDisplayName} · {item.Subject} — {item.Title}", "heading");
            var subtitleText = string.IsNullOrEmpty(item.InteractionStateLabel)
                ? $"截止：{item.DueLabel}"
                : $"截止：{item.DueLabel}  |  {item.InteractionStateLabel}";
            card.Append(title);
            card.Append(CreateDimLabel(subtitleText));
            box.Append(card);
            box.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }

        return WrapInScroll(box);
    }

    private Gtk.Widget BuildLearningPage()
    {
        var lc = _viewModel.LearningCase;
        var box = CreatePageContainer($"{lc.StudentDisplayName} · {lc.Subject} — {lc.Title}");
        box.Append(CreateDimLabel($"状态：{lc.StateLabel}  |  负责老师：{lc.ResponsibleTeacher}  |  下一步：{lc.NextAction}"));
        box.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));

        foreach (var entry in lc.Timeline)
        {
            var row = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
            row.SetMarginTop(4);
            row.SetMarginBottom(4);
            row.Append(CreateHeadingLabel($"{entry.DateLabel} · {entry.Heading}（{entry.ActorDisplayName}）", "heading"));
            var body = Gtk.Label.New(entry.Body);
            body.SetHalign(Gtk.Align.Start);
            body.SetWrap(true);
            row.Append(body);
            box.Append(row);
            box.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }

        return WrapInScroll(box);
    }

    private Gtk.Widget BuildOrganizationPage()
    {
        var box = CreatePageContainer($"成员与权限（{_viewModel.OrganizationMembers.Count} 位原型成员）");
        foreach (var member in _viewModel.OrganizationMembers)
        {
            var row = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
            row.SetMarginTop(4);
            row.SetMarginBottom(4);
            row.Append(CreateHeadingLabel($"{member.DisplayName} · {member.RoleLabel}（{member.StatusLabel}）", "heading"));
            row.Append(CreateDimLabel($"{member.RecentActivity}  |  {member.BulkSafetyLabel}"));
            box.Append(row);
            box.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }

        return WrapInScroll(box);
    }

    private void SyncStudentsFromViewModel()
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

            var currentCount = _studentIdsModel.GetNItems();
            _studentIdsModel.Splice(0, currentCount, ids);
            _studentCountLabel.SetText($"{ids.Length} 名学生");
        }
        finally
        {
            _suppressSelectionSync = false;
        }

        UpdateSelectedStudentDetail();
    }

    private void UpdateSelectedStudentDetail()
    {
        if (_studentSelection.GetSelectedItem() is Gtk.StringObject idObj &&
            _studentsById.TryGetValue(idObj.GetString(), out var student))
        {
            _viewModel.SelectedStudent = student;
            _detailNameLabel.SetText(student.DisplayName);
            var caseText = string.IsNullOrEmpty(student.ActiveCaseCountLabel)
                ? "无活跃学情问题"
                : student.ActiveCaseCountLabel;
            _detailMetaLabel.SetText($"{student.SecondaryLabel}  ·  主科：{student.PrimarySubject}  ·  {caseText}");
            if (_studentsSplitView.GetCollapsed())
            {
                _studentsSplitView.SetShowContent(true);
            }

            return;
        }

        _viewModel.SelectedStudent = null;
        _detailNameLabel.SetText("未找到匹配学生");
        _detailMetaLabel.SetText("请调整搜索关键词。");
    }

    private void ApplyResponsiveLayout()
    {
        var width = _window.GetWidth();
        if (width <= 0)
        {
            width = 1320;
        }

        var mode = WindowLayoutPolicy.Resolve(width);
        var collapsed = mode != WindowLayoutMode.Expanded;
        if (_studentsSplitView.GetCollapsed() != collapsed)
        {
            _studentsSplitView.SetCollapsed(collapsed);
        }

        var style = Adw.StyleManager.GetDefault();
        var theme = style.GetDark() ? "暗色" : "亮色";
        var hc = style.GetHighContrast() ? " · 高对比度" : string.Empty;
        _breakpointLabel.SetText($"窗口 {width}px → WindowLayoutPolicy.{mode}  |  主题：{theme}{hc}");
    }

    private void CycleColorScheme()
    {
        _colorSchemeIndex = (_colorSchemeIndex + 1) % 3;
        var scheme = _colorSchemeIndex switch
        {
            1 => Adw.ColorScheme.ForceDark,
            2 => Adw.ColorScheme.ForceLight,
            _ => Adw.ColorScheme.Default,
        };
        Adw.StyleManager.GetDefault().SetColorScheme(scheme);
        ApplyResponsiveLayout();
    }

    private async Task RunSecurityProbeAsync(Gtk.Button button)
    {
        button.SetSensitive(false);
        _securityStatusLabel.SetText("正在后台验证 libsecret + SQLite3MC 2.4.0 + boot_id…");
        try
        {
            var report = await Task.Run(() => LinuxSecuritySelfTest.RunAsync());
            _securityStatusLabel.SetText(
                $"本地安全链全部通过：SQLite3MC {report.Sqlite3McVersion} + libsecret + boot_id ({report.BootSessionId[..8]}…)");
        }
        catch (Exception ex)
        {
            _securityStatusLabel.SetText($"安全链验证失败（Fail-closed）：{ex.Message}");
        }
        finally
        {
            button.SetSensitive(true);
        }
    }

    private static Gtk.Box CreatePageContainer(string titleText)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        box.SetMarginStart(24);
        box.SetMarginEnd(24);
        box.SetMarginTop(20);
        box.SetMarginBottom(20);
        box.Append(CreateHeadingLabel(titleText, "title-3"));
        return box;
    }

    private static Gtk.ScrolledWindow WrapInScroll(Gtk.Widget child)
    {
        var scroll = Gtk.ScrolledWindow.New();
        scroll.SetVexpand(true);
        scroll.SetPolicy(Gtk.PolicyType.Never, Gtk.PolicyType.Automatic);
        scroll.SetChild(child);
        return scroll;
    }

    private static Gtk.Label CreateHeadingLabel(string text, string cssClass)
    {
        var label = Gtk.Label.New(text);
        label.SetHalign(Gtk.Align.Start);
        label.SetWrap(true);
        label.AddCssClass(cssClass);
        return label;
    }

    private static Gtk.Label CreateDimLabel(string text)
    {
        var label = Gtk.Label.New(text);
        label.SetHalign(Gtk.Align.Start);
        label.SetWrap(true);
        label.AddCssClass("dim-label");
        return label;
    }
}
