using System.Globalization;
using System.Text.Json;
using Xueqing.Linux.Infrastructure.LocalData;
using Xueqing.Linux.Infrastructure.Security;
using Xueqing.Linux.UI;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.LocalData;
using Xueqing.Windows.Infrastructure.Sync;

namespace Xueqing.Linux.Views;

/// <summary>
/// Quiet Ink Editorial exam-paper workspace:
/// - Overview snippet widget (Stage Suggestion callout + latest 2 papers) for the "概览" tab
/// - Full Papers tab widget (1-sentence human takeaway + minimalist Cairo sparkline + full hairline paper list)
/// - Immersive side-by-side desk-grading sheet (Original Paper Left · Mistakes &amp; Quick Confirm Right).
/// </summary>
internal sealed class PaperLongitudinalPanel
{
    private readonly Gtk.Widget _dialogParent;
    private readonly Func<StudyPlanProposedAction, Task<bool>>? _onAdoptProposedAction;
    private readonly Action? _onOpenFullPapersTab;

    // Full "试卷" tab root
    private readonly Gtk.Box _root;
    private readonly Gtk.Label _takeawayLabel;
    private readonly Gtk.Box _sparklineHost;
    private readonly Gtk.Box _topicsRow;
    private readonly Gtk.Box _topicsWrapBox;
    private readonly Gtk.Box _papersList;

    // Compact "概览" tab snippet
    private readonly Gtk.Box _overviewSnippet;
    private readonly Gtk.Box _studyPlanCallout;
    private readonly Gtk.Label _studyPlanKickerLabel;
    private readonly Gtk.Label _studyPlanTitleLabel;
    private readonly Gtk.Box _overviewRecentSection;
    private readonly Gtk.Box _overviewRecentList;

    private SqliteObservationDraftStore? _prototypePlanStore;
    private StudentPaperLongitudinalSnapshot? _currentSnapshot;
    private StudyPlanDraftSummary? _latestDraft;
    private IPaperAttachmentDownloader? _attachmentDownloader;
    private IPaperExtractionTrigger? _extractionTrigger;
    private IPaperExtractionConfirmer? _extractionConfirmer;
    private IStudyPlanDraftStatusUpdater? _draftStatusUpdater;
    private Func<Task>? _reloadCurrentAsync;
    private readonly HashSet<Guid> _prototypeConfirmedIds = new();
    private readonly Dictionary<Guid, string> _prototypeDraftStatusOverrides = new();
    private bool _isPrototype;

    public PaperLongitudinalPanel(
        Gtk.Widget dialogParent,
        Func<StudyPlanProposedAction, Task<bool>>? onAdoptProposedAction = null,
        Action? onOpenFullPapersTab = null)
    {
        _dialogParent = dialogParent ?? throw new ArgumentNullException(nameof(dialogParent));
        _onAdoptProposedAction = onAdoptProposedAction;
        _onOpenFullPapersTab = onOpenFullPapersTab;

        // --- 1. Full "试卷" tab workspace ---
        _root = Gtk.Box.New(Gtk.Orientation.Vertical, 18);
        GtkUiHelpers.SetAutomationId(_root, "PaperLongitudinalSection");

        _takeawayLabel = GtkUiHelpers.CreateBodyLabel(string.Empty);
        _takeawayLabel.AddCssClass("xq-takeaway");
        _root.Append(_takeawayLabel);

        _sparklineHost = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        _root.Append(_sparklineHost);

        _topicsRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
        _topicsRow.SetVisible(false);
        _topicsWrapBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
        _topicsRow.Append(_topicsWrapBox);
        _root.Append(_topicsRow);

        _papersList = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        _root.Append(_papersList);

        // --- 2. Compact "概览" tab snippet ---
        _overviewSnippet = Gtk.Box.New(Gtk.Orientation.Vertical, 18);

        _studyPlanCallout = Gtk.Box.New(Gtk.Orientation.Horizontal, 14);
        _studyPlanCallout.AddCssClass("xq-callout");
        _studyPlanCallout.SetVisible(false);

        var planTextBox = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
        planTextBox.SetHexpand(true);
        _studyPlanKickerLabel = GtkUiHelpers.CreateSectionKicker("阶段建议");
        _studyPlanTitleLabel = GtkUiHelpers.CreateHeading(string.Empty, "heading");
        planTextBox.Append(_studyPlanKickerLabel);
        planTextBox.Append(_studyPlanTitleLabel);

        var viewPlanButton = Gtk.Button.NewWithLabel("查看");
        viewPlanButton.AddCssClass("flat");
        viewPlanButton.SetValign(Gtk.Align.Center);
        viewPlanButton.OnClicked += async (_, _) =>
        {
            if (_latestDraft is { } draft)
            {
                await ShowStudyPlanDraftDialogAsync(draft);
            }
        };

        _studyPlanCallout.Append(planTextBox);
        _studyPlanCallout.Append(viewPlanButton);
        _overviewSnippet.Append(_studyPlanCallout);

        _overviewRecentSection = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        _overviewRecentSection.SetVisible(false);
        var recentHeader = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        var recentKicker = GtkUiHelpers.CreateSectionKicker("最近试卷");
        recentKicker.SetHexpand(true);
        recentHeader.Append(recentKicker);
        if (_onOpenFullPapersTab is not null)
        {
            var allPapersBtn = Gtk.Button.NewWithLabel("全部试卷 →");
            allPapersBtn.AddCssClass("flat");
            allPapersBtn.OnClicked += (_, _) => _onOpenFullPapersTab();
            recentHeader.Append(allPapersBtn);
        }

        _overviewRecentSection.Append(recentHeader);
        _overviewRecentList = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        _overviewRecentSection.Append(_overviewRecentList);
        _overviewSnippet.Append(_overviewRecentSection);
    }

    public Gtk.Widget Widget => _root;

    public Gtk.Widget OverviewSnippetWidget => _overviewSnippet;

    public async Task LoadPrototypeAsync(StudentSummary? student)
    {
        _isPrototype = true;
        _attachmentDownloader = null;
        _extractionTrigger = null;
        _extractionConfirmer = null;
        _draftStatusUpdater = null;
        _reloadCurrentAsync = () => LoadPrototypeAsync(student);

        if (student is null)
        {
            RenderEmpty("暂无试卷记录");
            return;
        }

        var studentUuid = PrototypeDraftIdentity.ResolveStudentUuid(student.Id);
        var drafts = new List<StudyPlanDraftSummary>();
        try
        {
            var store = GetOrCreatePrototypePlanStore();
            var scope = PrototypeDraftIdentity.CreateScopeForStudent(student.Id);
            var opened = await store.OpenAsync(scope);
            if (!string.IsNullOrWhiteSpace(opened.Recovered?.Text) &&
                TryParseLocalPrototypeStudyPlan(opened.Recovered.Text, out var parsedDraft) &&
                parsedDraft is not null)
            {
                drafts.Add(parsedDraft);
            }
        }
        catch (LocalDatabaseKeyUnavailableException)
        {
        }
        catch
        {
        }

        var raw = PrototypePaperLongitudinalFixtureFactory.Create(
            studentUuid,
            student.DisplayName,
            student.PrimarySubject,
            PrototypeDraftIdentity.OrganizationId,
            PrototypeDraftIdentity.SubjectProfileId,
            PrototypeDraftIdentity.AssignmentId,
            drafts);

        var adjustedPapers = raw.Papers.Select(p =>
            _prototypeConfirmedIds.Contains(p.PaperSubmissionId)
                ? p with
                {
                    ConfirmationState = PaperConfirmationState.HumanConfirmed,
                    GradingSource = p.GradingSource == PaperGradingSource.AiTentativeUnmarked
                        ? PaperGradingSource.HumanVerified
                        : p.GradingSource,
                }
                : p).ToArray();

        var adjustedDrafts = raw.RecentStudyPlanDrafts.Select(d =>
            _prototypeDraftStatusOverrides.TryGetValue(d.DraftId, out var status)
                ? d with { Status = status }
                : d).ToArray();

        var confirmedCount = adjustedPapers.Count(p => p.ConfirmationState == PaperConfirmationState.HumanConfirmed);
        var draftReadyCount = adjustedPapers.Count(p => p.ConfirmationState == PaperConfirmationState.DraftReady);
        var adjustedSummary = raw.Summary with
        {
            ConfirmedPapers = confirmedCount,
            DraftReadyPapers = draftReadyCount,
        };

        RenderSnapshot(raw with
        {
            Summary = adjustedSummary,
            Papers = adjustedPapers,
            RecentStudyPlanDrafts = adjustedDrafts,
        });
    }

    public async Task LoadAuthoritativeAsync(
        StudentLearningScope scope,
        Guid actorAppUserId,
        IStudentPaperLongitudinalReader? reader,
        IPaperAttachmentDownloader? attachmentDownloader,
        IPaperExtractionTrigger? extractionTrigger,
        IPaperExtractionConfirmer? extractionConfirmer,
        IStudyPlanDraftStatusUpdater? draftStatusUpdater)
    {
        _isPrototype = false;
        _attachmentDownloader = attachmentDownloader;
        _extractionTrigger = extractionTrigger;
        _extractionConfirmer = extractionConfirmer;
        _draftStatusUpdater = draftStatusUpdater;
        _reloadCurrentAsync = () => LoadAuthoritativeAsync(
            scope,
            actorAppUserId,
            reader,
            attachmentDownloader,
            extractionTrigger,
            extractionConfirmer,
            draftStatusUpdater);

        if (reader is null || actorAppUserId == Guid.Empty)
        {
            RenderEmpty("暂未加载试卷");
            return;
        }

        _takeawayLabel.SetText("正在读取试卷…");
        var result = await reader.ReadAsync(scope, actorAppUserId, windowDays: 90, limit: 30);
        if (!result.IsSuccess || result.Snapshot is null)
        {
            RenderEmpty("暂时无法读取试卷");
            return;
        }

        RenderSnapshot(result.Snapshot, attachmentDownloader);
    }

    public void RenderSnapshot(
        StudentPaperLongitudinalSnapshot snapshot,
        IPaperAttachmentDownloader? attachmentDownloader = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _currentSnapshot = snapshot;
        if (attachmentDownloader is not null)
        {
            _attachmentDownloader = attachmentDownloader;
        }

        // 1. One-sentence human takeaway
        _takeawayLabel.SetText(PaperTrendSparkline.BuildNaturalTakeaway(snapshot));

        // 2. Minimalist Cairo sparkline (zero progress bars!)
        GtkUiHelpers.ClearBoxChildren(_sparklineHost);
        if (snapshot.Papers.Count > 0)
        {
            _sparklineHost.Append(PaperTrendSparkline.Create(snapshot));
            _sparklineHost.SetVisible(true);
        }
        else
        {
            _sparklineHost.SetVisible(false);
        }

        // 3. Quiet monochrome recurring-topic chips
        GtkUiHelpers.ClearBoxChildren(_topicsWrapBox);
        if (snapshot.RecurringTopics.Count > 0)
        {
            foreach (var topic in snapshot.RecurringTopics.Take(5))
            {
                _topicsWrapBox.Append(GtkUiHelpers.CreateTag($"{topic.TopicTag} {topic.OccurrenceCount}"));
            }

            _topicsRow.SetVisible(true);
        }
        else
        {
            _topicsRow.SetVisible(false);
        }

        // 4. Full hairline paper list
        GtkUiHelpers.ClearBoxChildren(_papersList);
        if (snapshot.Papers.Count == 0)
        {
            var empty = Gtk.Label.New("暂无试卷记录");
            empty.SetHalign(Gtk.Align.Start);
            empty.AddCssClass("xq-empty-note");
            _papersList.Append(empty);
        }
        else
        {
            _papersList.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
            foreach (var paper in snapshot.Papers)
            {
                _papersList.Append(BuildPaperRow(paper));
            }
        }

        // 5. Overview snippet: Stage Suggestion callout + latest 2 papers
        _latestDraft = snapshot.RecentStudyPlanDrafts.FirstOrDefault();
        if (_latestDraft is { } draft)
        {
            var statusNote = draft.Status?.Trim().ToLowerInvariant() switch
            {
                "fully_adopted" or "partially_adopted" => "阶段建议 · 已采纳",
                "dismissed" => "阶段建议 · 已忽略",
                _ => "阶段建议",
            };
            _studyPlanKickerLabel.SetText(statusNote);
            _studyPlanTitleLabel.SetText(draft.Title);
            _studyPlanCallout.SetVisible(true);
        }
        else
        {
            _studyPlanCallout.SetVisible(false);
        }

        GtkUiHelpers.ClearBoxChildren(_overviewRecentList);
        var recentTwo = snapshot.Papers.Take(2).ToArray();
        if (recentTwo.Length > 0)
        {
            foreach (var paper in recentTwo)
            {
                _overviewRecentList.Append(BuildPaperRow(paper));
            }

            _overviewRecentSection.SetVisible(true);
        }
        else
        {
            _overviewRecentSection.SetVisible(false);
        }
    }

    public void RenderEmpty(string statusText)
    {
        _takeawayLabel.SetText(string.Empty);
        GtkUiHelpers.ClearBoxChildren(_sparklineHost);
        _sparklineHost.SetVisible(false);
        _topicsRow.SetVisible(false);
        _studyPlanCallout.SetVisible(false);
        _overviewRecentSection.SetVisible(false);
        GtkUiHelpers.ClearBoxChildren(_papersList);

        var empty = Gtk.Label.New(statusText);
        empty.SetHalign(Gtk.Align.Start);
        empty.AddCssClass("xq-empty-note");
        _papersList.Append(empty);
    }

    private Gtk.Box BuildPaperRow(PaperSubmissionSummary paper)
    {
        var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 16);
        row.AddCssClass("xq-list-row");

        var dateCol = Gtk.Label.New($"{paper.PaperDate:MM/dd}");
        dateCol.SetSizeRequest(48, -1);
        dateCol.SetHalign(Gtk.Align.Start);
        dateCol.SetValign(Gtk.Align.Start);
        dateCol.AddCssClass("xq-date-col");

        var middleCol = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
        middleCol.SetHexpand(true);

        var titleText = string.IsNullOrWhiteSpace(paper.Title) ? paper.KindLabel : paper.Title;
        middleCol.Append(GtkUiHelpers.CreateHeading(titleText, "heading"));

        var subtitleText = !string.IsNullOrWhiteSpace(paper.ExtractionSummary)
            ? $"{paper.KindLabel} · {paper.ExtractionSummary}"
            : $"{paper.KindLabel} · {paper.Pages.Count} 页";
        middleCol.Append(GtkUiHelpers.CreateDimLabel(subtitleText, caption: true));

        var rightCol = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
        rightCol.SetValign(Gtk.Align.Center);

        var scoreText = paper.ScoreEarned is { } earned && paper.ScoreTotal is { } total
            ? $"{earned:0.##}/{total:0.##}"
            : $"错 {paper.MistakeCount ?? paper.Mistakes.Count} 题";
        var scoreLabel = Gtk.Label.New(scoreText);
        scoreLabel.AddCssClass("xq-score-num");
        rightCol.Append(scoreLabel);

        if (paper.ConfirmationState == PaperConfirmationState.DraftReady)
        {
            rightCol.Append(GtkUiHelpers.CreateStatusText("待核对", "xq-status-pending"));
        }
        else if (paper.ConfirmationState == PaperConfirmationState.AwaitingExtraction)
        {
            rightCol.Append(GtkUiHelpers.CreateStatusText("待解析", "xq-status-muted"));
        }
        else if (paper.ConfirmationState == PaperConfirmationState.ExtractionFailed)
        {
            rightCol.Append(GtkUiHelpers.CreateStatusText("解析失败", "xq-status-overdue"));
        }

        var capturedPaper = paper;
        if (paper.ConfirmationState is PaperConfirmationState.AwaitingExtraction or PaperConfirmationState.ExtractionFailed)
        {
            var extractBtn = Gtk.Button.NewWithLabel("解析");
            extractBtn.AddCssClass("suggested-action");
            extractBtn.OnClicked += async (_, _) => await TriggerExtractionForPaperAsync(capturedPaper, extractBtn);
            rightCol.Append(extractBtn);
        }

        var detailBtn = Gtk.Button.NewWithLabel(
            paper.ConfirmationState == PaperConfirmationState.HumanConfirmed ? "阅卷" : "核对");
        detailBtn.AddCssClass("flat");
        detailBtn.OnClicked += async (_, _) => await ShowSideBySidePaperDialogAsync(capturedPaper);
        rightCol.Append(detailBtn);

        row.Append(dateCol);
        row.Append(middleCol);
        row.Append(rightCol);
        return row;
    }

    private async Task TriggerExtractionForPaperAsync(PaperSubmissionSummary paper, Gtk.Button triggerButton)
    {
        if (_currentSnapshot is null)
        {
            return;
        }

        if (_extractionTrigger is null)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(
                _dialogParent,
                "需要登录云端环境",
                "连接正式云端账号后即可解析试卷图片。");
            return;
        }

        triggerButton.SetSensitive(false);
        triggerButton.SetLabel("解析中…");
        try
        {
            var result = await _extractionTrigger.TriggerExtractionAsync(new TriggerPaperExtractionRequest(
                Guid.NewGuid(),
                _currentSnapshot.OrganizationId,
                _currentSnapshot.StudentId,
                _currentSnapshot.SubjectProfileId,
                paper.PaperSubmissionId,
                paper.Version));

            if (!result.IsSuccess || result.Receipt is null)
            {
                await GtkUiHelpers.ShowMessageDialogAsync(
                    _dialogParent,
                    "解析未完成",
                    "请稍后重试，原卷图片不受影响。");
                return;
            }

            if (_reloadCurrentAsync is not null)
            {
                await _reloadCurrentAsync();
            }
        }
        finally
        {
            triggerButton.SetSensitive(true);
            triggerButton.SetLabel("解析");
        }
    }

    /// <summary>
    /// Immersive side-by-side desk-grading sheet:
    /// Left pane displays the original exam pages with pagination;
    /// Right pane lists mistakes (clicking jumps the left page!) and inline confirmation controls.
    /// </summary>
    private async Task ShowSideBySidePaperDialogAsync(PaperSubmissionSummary paper)
    {
        var dialog = Adw.Dialog.New();
        dialog.SetTitle(paper.Title ?? $"{paper.PaperDate:yyyy-MM-dd} {paper.KindLabel}");
        dialog.SetContentWidth(960);
        dialog.SetContentHeight(640);

        var toolbar = Adw.ToolbarView.New();
        toolbar.AddTopBar(Adw.HeaderBar.New());

        var split = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
        split.SetHexpand(true);
        split.SetVexpand(true);

        // === LEFT PANE: Original Paper Canvas ===
        var leftPane = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        leftPane.AddCssClass("xq-paper-canvas");
        leftPane.SetHexpand(true);
        leftPane.SetVexpand(true);
        leftPane.SetSizeRequest(420, -1);

        var canvasHost = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        canvasHost.SetHexpand(true);
        canvasHost.SetVexpand(true);
        leftPane.Append(canvasHost);

        var pagerBar = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        pagerBar.SetMarginStart(16);
        pagerBar.SetMarginEnd(16);
        pagerBar.SetMarginTop(10);
        pagerBar.SetMarginBottom(12);

        var prevBtn = Gtk.Button.NewWithLabel("上一页");
        prevBtn.AddCssClass("flat");
        var pageIndicator = Gtk.Label.New(string.Empty);
        pageIndicator.SetHexpand(true);
        pageIndicator.SetHalign(Gtk.Align.Center);
        pageIndicator.AddCssClass("xq-date-col");
        var nextBtn = Gtk.Button.NewWithLabel("下一页");
        nextBtn.AddCssClass("flat");

        pagerBar.Append(prevBtn);
        pagerBar.Append(pageIndicator);
        pagerBar.Append(nextBtn);
        leftPane.Append(pagerBar);

        var orderedPages = paper.Pages.OrderBy(p => p.PageIndex).ToArray();
        var currentPageIdx = 0;
        var textureCache = new Dictionary<int, Gdk.Texture>();

        async Task RenderLeftPageAsync(int index)
        {
            GtkUiHelpers.ClearBoxChildren(canvasHost);

            if (orderedPages.Length == 0)
            {
                var noPage = Gtk.Label.New("暂无原卷照片");
                noPage.SetVexpand(true);
                noPage.SetValign(Gtk.Align.Center);
                noPage.SetHalign(Gtk.Align.Center);
                noPage.AddCssClass("xq-empty-note");
                canvasHost.Append(noPage);
                pageIndicator.SetText("0 / 0");
                prevBtn.SetSensitive(false);
                nextBtn.SetSensitive(false);
                return;
            }

            currentPageIdx = Math.Clamp(index, 0, orderedPages.Length - 1);
            var page = orderedPages[currentPageIdx];
            prevBtn.SetSensitive(currentPageIdx > 0);
            nextBtn.SetSensitive(currentPageIdx < orderedPages.Length - 1);

            var redactHint = page.HeaderRedacted ? " · 已遮挡姓名" : string.Empty;
            pageIndicator.SetText($"第 {page.PageIndex} / {orderedPages.Length} 页{redactHint}");

            if (_attachmentDownloader is null)
            {
                var pageMistakes = paper.Mistakes.Where(m => m.PageIndex == page.PageIndex).ToArray();
                canvasHost.Append(PaperTrendSparkline.CreateSimulatedPaperCanvas(paper, page, pageMistakes));
                return;
            }

            if (textureCache.TryGetValue(page.PageIndex, out var cachedTexture))
            {
                var pic = Gtk.Picture.NewForPaintable(cachedTexture);
                pic.SetCanShrink(true);
                pic.SetVexpand(true);
                pic.SetHexpand(true);
                canvasHost.Append(pic);
                return;
            }

            var loading = Gtk.Label.New("正在加载原卷…");
            loading.SetVexpand(true);
            loading.SetValign(Gtk.Align.Center);
            loading.SetHalign(Gtk.Align.Center);
            loading.AddCssClass("xq-empty-note");
            canvasHost.Append(loading);

            var download = await _attachmentDownloader.DownloadImageAsync(page.BucketId, page.ObjectName);
            if (currentPageIdx != index)
            {
                return;
            }

            GtkUiHelpers.ClearBoxChildren(canvasHost);
            if (!download.IsSuccess || download.Image is null)
            {
                var err = Gtk.Label.New("暂时无法加载原卷图片");
                err.SetVexpand(true);
                err.SetValign(Gtk.Align.Center);
                err.SetHalign(Gtk.Align.Center);
                err.AddCssClass("xq-empty-note");
                canvasHost.Append(err);
                return;
            }

            var texture = Gdk.Texture.NewFromBytes(GLib.Bytes.New(download.Image.Bytes));
            textureCache[page.PageIndex] = texture;
            var picture = Gtk.Picture.NewForPaintable(texture);
            picture.SetCanShrink(true);
            picture.SetVexpand(true);
            picture.SetHexpand(true);
            canvasHost.Append(picture);
        }

        prevBtn.OnClicked += async (_, _) => await RenderLeftPageAsync(currentPageIdx - 1);
        nextBtn.OnClicked += async (_, _) => await RenderLeftPageAsync(currentPageIdx + 1);

        // === RIGHT PANE: Mistakes & Inline Quick Confirm ===
        var rightContent = Gtk.Box.New(Gtk.Orientation.Vertical, 16);
        rightContent.SetMarginStart(24);
        rightContent.SetMarginEnd(24);
        rightContent.SetMarginTop(18);
        rightContent.SetMarginBottom(24);

        var metaParts = new List<string>
        {
            paper.PaperDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            paper.KindLabel,
        };
        if (paper.ScoreEarned is { } se0 && paper.ScoreTotal is { } st0)
        {
            metaParts.Add($"{se0:0.##}/{st0:0.##}");
        }

        metaParts.Add(paper.ConfirmationState == PaperConfirmationState.HumanConfirmed ? "已确认" : "待核对");
        rightContent.Append(GtkUiHelpers.CreateDimLabel(string.Join("  ·  ", metaParts), caption: true));

        // Inline confirmation controls at top for unconfirmed papers
        if (paper.ConfirmationState != PaperConfirmationState.HumanConfirmed)
        {
            var confirmBox = GtkUiHelpers.CreateSurfaceBox(spacing: 10);
            confirmBox.Append(GtkUiHelpers.CreateSectionKicker("核对分数与小结"));

            var fieldsRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
            fieldsRow.SetHomogeneous(true);

            var earnedEntry = Gtk.Entry.New();
            earnedEntry.SetPlaceholderText("得分");
            if (paper.ScoreEarned is { } se)
            {
                earnedEntry.SetText(se.ToString("0.##", CultureInfo.InvariantCulture));
            }

            var totalEntry = Gtk.Entry.New();
            totalEntry.SetPlaceholderText("满分");
            if (paper.ScoreTotal is { } st)
            {
                totalEntry.SetText(st.ToString("0.##", CultureInfo.InvariantCulture));
            }

            var mistakesEntry = Gtk.Entry.New();
            mistakesEntry.SetPlaceholderText("错题");
            mistakesEntry.SetText((paper.MistakeCount ?? paper.Mistakes.Count).ToString(CultureInfo.InvariantCulture));

            fieldsRow.Append(WrapLabeledEntry("得分", earnedEntry));
            fieldsRow.Append(WrapLabeledEntry("满分", totalEntry));
            fieldsRow.Append(WrapLabeledEntry("错题", mistakesEntry));
            confirmBox.Append(fieldsRow);

            var summaryEntry = Gtk.Entry.New();
            summaryEntry.SetPlaceholderText("小结（可选）");
            if (!string.IsNullOrWhiteSpace(paper.ExtractionSummary))
            {
                summaryEntry.SetText(paper.ExtractionSummary);
            }

            confirmBox.Append(summaryEntry);

            var confirmBtn = Gtk.Button.NewWithLabel("确认");
            confirmBtn.AddCssClass("suggested-action");
            confirmBtn.SetHalign(Gtk.Align.End);
            confirmBtn.OnClicked += async (_, _) =>
            {
                decimal? parsedEarned = decimal.TryParse(earnedEntry.GetText().Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var eVal) ? eVal : null;
                decimal? parsedTotal = decimal.TryParse(totalEntry.GetText().Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var tVal) ? tVal : null;
                int? parsedMistakes = int.TryParse(mistakesEntry.GetText().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mVal) ? mVal : null;
                var editedSummary = summaryEntry.GetText().Trim();

                if (parsedEarned is not null && parsedTotal is not null && parsedEarned > parsedTotal)
                {
                    await GtkUiHelpers.ShowMessageDialogAsync(dialog, "分数有误", "得分不能大于满分。");
                    return;
                }

                if (_isPrototype || _extractionConfirmer is null)
                {
                    _prototypeConfirmedIds.Add(paper.PaperSubmissionId);
                    dialog.Close();
                    if (_reloadCurrentAsync is not null)
                    {
                        await _reloadCurrentAsync();
                    }

                    return;
                }

                confirmBtn.SetSensitive(false);
                try
                {
                    var mistakeInputs = paper.Mistakes.Select(m => new PaperMistakeDraftInput(
                        m.PageIndex,
                        m.QuestionLabel,
                        m.TopicTags,
                        m.ErrorRootCause,
                        m.PaperSectionPosition,
                        m.PointsLost,
                        m.CorrectedOnPaper,
                        m.Summary,
                        m.LinkedCaseId)).ToArray();

                    var saveResult = await _extractionConfirmer.SaveExtractionAsync(new SavePaperExtractionRequest(
                        Guid.NewGuid(),
                        paper.PaperSubmissionId,
                        paper.Version,
                        Mode: "human_confirm",
                        GradingSource: paper.GradingSource == PaperGradingSource.AiTentativeUnmarked
                            ? PaperGradingSource.HumanVerified
                            : paper.GradingSource,
                        Title: paper.Title,
                        QuestionCount: paper.QuestionCount,
                        MistakeCount: parsedMistakes,
                        ScoreEarned: parsedEarned,
                        ScoreTotal: parsedTotal,
                        DurationMinutes: paper.DurationMinutes,
                        ExtractionSummary: string.IsNullOrWhiteSpace(editedSummary) ? paper.ExtractionSummary : editedSummary,
                        Mistakes: mistakeInputs));

                    if (!saveResult.IsSuccess)
                    {
                        await GtkUiHelpers.ShowMessageDialogAsync(dialog, "保存失败", "请稍后重试。");
                        return;
                    }

                    dialog.Close();
                    if (_reloadCurrentAsync is not null)
                    {
                        await _reloadCurrentAsync();
                    }
                }
                finally
                {
                    confirmBtn.SetSensitive(true);
                }
            };
            confirmBox.Append(confirmBtn);
            rightContent.Append(confirmBox);
        }
        else if (!string.IsNullOrWhiteSpace(paper.ExtractionSummary))
        {
            rightContent.Append(GtkUiHelpers.CreateBodyLabel(paper.ExtractionSummary));
        }

        // Mistakes list (clicking page button jumps left canvas to that page!)
        var mistakesSection = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        mistakesSection.Append(GtkUiHelpers.CreateSectionKicker($"错题（{paper.Mistakes.Count}）"));
        if (paper.Mistakes.Count == 0)
        {
            mistakesSection.Append(GtkUiHelpers.CreateDimLabel("暂无错题记录", caption: true));
        }
        else
        {
            foreach (var m in paper.Mistakes)
            {
                var row = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
                row.AddCssClass("xq-list-row");

                var top = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
                var pts = m.PointsLost is { } lost ? $"  -{lost:0.##}" : string.Empty;
                var mTitle = GtkUiHelpers.CreateHeading($"{m.QuestionLabel}{pts}", "heading");
                mTitle.SetHexpand(true);
                top.Append(mTitle);

                top.Append(GtkUiHelpers.CreateStatusText(
                    PaperEvidenceWireNames.ToChineseLabel(m.ErrorRootCause),
                    "xq-status-muted"));

                var targetPageIndex = Array.FindIndex(orderedPages, p => p.PageIndex == m.PageIndex);
                if (targetPageIndex >= 0 && orderedPages.Length > 1)
                {
                    var jumpBtn = Gtk.Button.NewWithLabel($"第{m.PageIndex}页");
                    jumpBtn.AddCssClass("flat");
                    var capturedIdx = targetPageIndex;
                    jumpBtn.OnClicked += async (_, _) => await RenderLeftPageAsync(capturedIdx);
                    top.Append(jumpBtn);
                }

                row.Append(top);
                row.Append(GtkUiHelpers.CreateBodyLabel(m.Summary));

                var details = new List<string> { PaperEvidenceWireNames.ToChineseLabel(m.PaperSectionPosition) };
                if (m.CorrectedOnPaper is { } corr)
                {
                    details.Add(corr ? "已订正" : "未订正");
                }

                if (m.TopicTags.Count > 0)
                {
                    details.Add(string.Join(" / ", m.TopicTags));
                }

                row.Append(GtkUiHelpers.CreateDimLabel(string.Join(" · ", details), caption: true));
                mistakesSection.Append(row);
            }
        }

        rightContent.Append(mistakesSection);

        var rightScroll = GtkUiHelpers.WrapInScroll(rightContent);
        rightScroll.SetHexpand(true);
        rightScroll.SetSizeRequest(380, -1);

        split.Append(leftPane);
        split.Append(rightScroll);
        toolbar.SetContent(split);
        dialog.SetChild(toolbar);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.OnClosed += (_, _) => tcs.TrySetResult();
        dialog.Present(_dialogParent);

        _ = RenderLeftPageAsync(0);
        await tcs.Task;
    }

    private async Task ShowStudyPlanDraftDialogAsync(StudyPlanDraftSummary draft)
    {
        var dialog = Adw.Dialog.New();
        dialog.SetTitle(draft.Title);
        dialog.SetContentWidth(620);
        dialog.SetContentHeight(560);

        var toolbar = Adw.ToolbarView.New();
        toolbar.AddTopBar(Adw.HeaderBar.New());

        var content = Gtk.Box.New(Gtk.Orientation.Vertical, 18);
        content.SetMarginStart(24);
        content.SetMarginEnd(24);
        content.SetMarginTop(16);
        content.SetMarginBottom(24);

        content.Append(GtkUiHelpers.CreateDimLabel($"近 {draft.WindowDays} 天 · {draft.PaperCountAnalyzed} 份试卷", caption: true));
        content.Append(GtkUiHelpers.CreateBodyLabel(draft.BriefMarkdown));

        var proposedActions = PaperEvidenceWireNames.ParseProposedActions(draft.ProposedActionsJson);
        if (proposedActions.Count > 0)
        {
            content.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
            content.Append(GtkUiHelpers.CreateSectionKicker("建议行动"));
            foreach (var action in proposedActions)
            {
                var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 14);
                row.AddCssClass("xq-list-row");

                var textCol = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
                textCol.SetHexpand(true);
                textCol.Append(GtkUiHelpers.CreateHeading(action.Title, "heading"));
                textCol.Append(GtkUiHelpers.CreateBodyLabel(action.NextAction));
                if (!string.IsNullOrWhiteSpace(action.Rationale))
                {
                    textCol.Append(GtkUiHelpers.CreateDimLabel(action.Rationale, caption: true));
                }

                row.Append(textCol);

                if (!string.Equals(draft.Status, "dismissed", StringComparison.OrdinalIgnoreCase))
                {
                    var adoptBtn = Gtk.Button.NewWithLabel("采纳");
                    adoptBtn.AddCssClass("suggested-action");
                    adoptBtn.SetValign(Gtk.Align.Center);
                    var capturedAction = action;
                    adoptBtn.OnClicked += async (_, _) =>
                    {
                        var adopted = _onAdoptProposedAction is not null
                            ? await _onAdoptProposedAction(capturedAction)
                            : true;
                        if (!adopted)
                        {
                            return;
                        }

                        await UpdateDraftStatusAsync(draft, "partially_adopted", dialog);
                    };
                    row.Append(adoptBtn);
                }

                content.Append(row);
            }
        }

        var footer = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        footer.SetHalign(Gtk.Align.End);

        if (string.Equals(draft.Status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            var dismissBtn = Gtk.Button.NewWithLabel("忽略");
            dismissBtn.AddCssClass("flat");
            dismissBtn.OnClicked += async (_, _) => await UpdateDraftStatusAsync(draft, "dismissed", dialog);
            footer.Append(dismissBtn);
        }

        var closeBtn = Gtk.Button.NewWithLabel("关闭");
        closeBtn.OnClicked += (_, _) => dialog.Close();
        footer.Append(closeBtn);
        content.Append(footer);

        toolbar.SetContent(GtkUiHelpers.WrapInScroll(content));
        dialog.SetChild(toolbar);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.OnClosed += (_, _) => tcs.TrySetResult();
        dialog.Present(_dialogParent);
        await tcs.Task;
    }

    private async Task UpdateDraftStatusAsync(StudyPlanDraftSummary draft, string nextStatus, Adw.Dialog dialog)
    {
        if (_isPrototype || _draftStatusUpdater is null)
        {
            _prototypeDraftStatusOverrides[draft.DraftId] = nextStatus;
            dialog.Close();
            if (_reloadCurrentAsync is not null)
            {
                await _reloadCurrentAsync();
            }

            return;
        }

        var result = await _draftStatusUpdater.UpdateStatusAsync(new UpdateStudyPlanDraftStatusRequest(
            Guid.NewGuid(),
            draft.DraftId,
            draft.Version,
            nextStatus));
        if (!result.IsSuccess)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(dialog, "更新失败", "请稍后重试。");
            return;
        }

        dialog.Close();
        if (_reloadCurrentAsync is not null)
        {
            await _reloadCurrentAsync();
        }
    }

    private static Gtk.Box WrapLabeledEntry(string labelText, Gtk.Entry entry)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
        box.Append(GtkUiHelpers.CreateDimLabel(labelText, caption: true));
        box.Append(entry);
        return box;
    }

    private SqliteObservationDraftStore GetOrCreatePrototypePlanStore()
    {
        if (_prototypePlanStore is not null)
        {
            return _prototypePlanStore;
        }

        var path = LinuxLocalStatePaths.GetPrototypeStudyPlanDraftDatabasePath();
        _prototypePlanStore = new SqliteObservationDraftStore(path, new LinuxSecretServiceDatabaseKeyStore(path));
        return _prototypePlanStore;
    }

    private static bool TryParseLocalPrototypeStudyPlan(string json, out StudyPlanDraftSummary? draft)
    {
        draft = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            var brief = root.TryGetProperty("briefMarkdown", out var b) ? b.GetString() : null;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(brief))
            {
                return false;
            }

            var id = root.TryGetProperty("draftId", out var idEl) && Guid.TryParse(idEl.GetString(), out var g)
                ? g
                : Guid.NewGuid();
            var refs = root.TryGetProperty("evidenceRefsJson", out var r) ? r.GetString() ?? "[]" : "[]";
            var actions = root.TryGetProperty("proposedActionsJson", out var a) ? a.GetString() ?? "[]" : "[]";
            var windowDays = root.TryGetProperty("windowDays", out var w) && w.TryGetInt32(out var wd) ? wd : 90;
            var papersCount = root.TryGetProperty("paperCountAnalyzed", out var pc) && pc.TryGetInt32(out var pcv) ? pcv : 0;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "draft" : "draft";
            var savedAt = root.TryGetProperty("savedAtUtc", out var sa) && sa.TryGetDateTimeOffset(out var dto)
                ? dto
                : DateTimeOffset.UtcNow;

            draft = new StudyPlanDraftSummary(id, title, brief, refs, actions, windowDays, papersCount, status, 1, savedAt, savedAt);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
