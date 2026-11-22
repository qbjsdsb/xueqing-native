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
/// Native GTK4 + libadwaita longitudinal exam-paper workspace:
/// - 4-tile KPI strip (volume, authoritative score average, late-section fatigue, top topic)
/// - AI stage brief &amp; structured study-plan draft review/adoption/dismissal
/// - Weekly cadence &amp; root-cause evolution native progress bars
/// - Paper timeline cards with AI extraction trigger, human-confirmation review dialog,
///   and authenticated private attachment preview.
/// Works seamlessly in both Production mode (live PostgREST + extract-paper-v1) and Prototype mode.
/// </summary>
internal sealed class PaperLongitudinalPanel
{
    private readonly Gtk.Widget _dialogParent;
    private readonly Func<StudyPlanProposedAction, Task<bool>>? _onAdoptProposedAction;
    private readonly Gtk.Box _root;
    private readonly Gtk.Label _statusLabel;
    private readonly Gtk.Box _kpiStrip;
    private readonly Gtk.Box _studyPlanCard;
    private readonly Gtk.Label _studyPlanTitleLabel;
    private readonly Gtk.Label _studyPlanMetaLabel;
    private readonly Gtk.Box _studyPlanBadgeHost;
    private readonly Gtk.Button _viewPlanButton;
    private readonly Gtk.Box _trendsRow;
    private readonly Gtk.Box _topicsCard;
    private readonly Gtk.Box _topicsWrapBox;
    private readonly Gtk.Box _papersList;

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
        Func<StudyPlanProposedAction, Task<bool>>? onAdoptProposedAction = null)
    {
        _dialogParent = dialogParent ?? throw new ArgumentNullException(nameof(dialogParent));
        _onAdoptProposedAction = onAdoptProposedAction;

        _root = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
        GtkUiHelpers.SetAutomationId(_root, "PaperLongitudinalSection");

        var headerRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        var title = GtkUiHelpers.CreateHeading("试卷纵向档案与阶段复盘", "title-4");
        title.SetHexpand(true);
        headerRow.Append(title);
        _statusLabel = GtkUiHelpers.CreateDimLabel(string.Empty, caption: true);
        _statusLabel.SetHalign(Gtk.Align.End);
        headerRow.Append(_statusLabel);
        _root.Append(headerRow);

        // 2x2 KPI Grid (spacious and legible from 900px laptops to wide desktops)
        _kpiStrip = Gtk.Box.New(Gtk.Orientation.Vertical, 10);
        _root.Append(_kpiStrip);

        // Latest Study Plan Draft Hero Card
        _studyPlanCard = Gtk.Box.New(Gtk.Orientation.Horizontal, 14);
        _studyPlanCard.AddCssClass("xq-hero-card");
        _studyPlanCard.SetVisible(false);

        var planTextBox = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        planTextBox.SetHexpand(true);

        var planTopRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        _studyPlanTitleLabel = GtkUiHelpers.CreateHeading(string.Empty, "heading");
        _studyPlanTitleLabel.SetHexpand(true);
        _studyPlanBadgeHost = Gtk.Box.New(Gtk.Orientation.Horizontal, 4);
        planTopRow.Append(_studyPlanTitleLabel);
        planTopRow.Append(_studyPlanBadgeHost);

        _studyPlanMetaLabel = GtkUiHelpers.CreateDimLabel(string.Empty, caption: true);
        planTextBox.Append(planTopRow);
        planTextBox.Append(_studyPlanMetaLabel);

        _viewPlanButton = Gtk.Button.NewWithLabel("审阅与采纳");
        _viewPlanButton.AddCssClass("suggested-action");
        _viewPlanButton.SetValign(Gtk.Align.Center);
        _viewPlanButton.OnClicked += async (_, _) =>
        {
            if (_latestDraft is { } draft)
            {
                await ShowStudyPlanDraftDialogAsync(draft);
            }
        };

        _studyPlanCard.Append(planTextBox);
        _studyPlanCard.Append(_viewPlanButton);
        _root.Append(_studyPlanCard);

        // 2-Column Visual Trends (Weekly Cadence + Root Cause Shift)
        _trendsRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        _trendsRow.SetHomogeneous(true);
        _trendsRow.SetVisible(false);
        _root.Append(_trendsRow);

        // Recurring Topics Pill Strip
        _topicsCard = GtkUiHelpers.CreateCardBox(spacing: 8);
        _topicsCard.SetVisible(false);
        _topicsCard.Append(GtkUiHelpers.CreateHeading("高频复发知识点", "heading"));
        _topicsWrapBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
        _topicsCard.Append(_topicsWrapBox);
        _root.Append(_topicsCard);

        // Papers Timeline List
        _papersList = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        _root.Append(_papersList);
    }

    public Gtk.Widget Widget => _root;

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
            RenderEmpty("请从左侧选择一位学生，查看历次试卷趋势与阶段复盘。");
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
            // Prototype paper timeline still renders cleanly without keyring.
        }
        catch
        {
            // Ignore malformed local prototype JSON cleanly.
        }

        var raw = PrototypePaperLongitudinalFixtureFactory.Create(
            studentUuid,
            student.DisplayName,
            student.PrimarySubject,
            PrototypeDraftIdentity.OrganizationId,
            PrototypeDraftIdentity.SubjectProfileId,
            PrototypeDraftIdentity.AssignmentId,
            drafts);

        // Apply any interactive prototype confirmation/status overrides in this session.
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
            RenderEmpty("当前会话尚未就绪，暂未加载云端试卷档案。");
            return;
        }

        _statusLabel.SetText("正在同步试卷档案…");
        var result = await reader.ReadAsync(scope, actorAppUserId, windowDays: 90, limit: 30);
        if (!result.IsSuccess || result.Snapshot is null)
        {
            var reason = result.Failure?.Code ?? "NETWORK_UNAVAILABLE";
            RenderEmpty($"暂时无法读取试卷纵向数据（{reason}）。");
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

        var s = snapshot.Summary;
        _statusLabel.SetText(_isPrototype ? "演示数据 · 可体验核对与采纳" : $"近 {snapshot.WindowDays} 天实时数据");

        // 1. 2x2 KPI Strip
        GtkUiHelpers.ClearBoxChildren(_kpiStrip);
        var kpiRow1 = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        kpiRow1.SetHomogeneous(true);
        var kpiRow2 = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        kpiRow2.SetHomogeneous(true);

        kpiRow1.Append(GtkUiHelpers.CreateKpiTile(
            $"{s.TotalPapers} 份",
            $"近 {snapshot.WindowDays} 天卷数",
            $"已确认 {s.ConfirmedPapers} · 待核对 {s.DraftReadyPapers}",
            "xq-kpi-value-accent"));

        var avgValue = s.TeacherMarkedAvgScoreRatio is { } avg ? $"{avg * 100m:0.0}%" : "—";
        kpiRow1.Append(GtkUiHelpers.CreateKpiTile(
            avgValue,
            "权威均分",
            "仅计老师批改与已核对"));

        var fatigueValue = s.LateSectionMistakeRatio is { } fatigue ? $"{fatigue * 100m:0}%" : "—";
        var fatigueCss = s.LateSectionMistakeRatio is >= 0.45m ? "xq-kpi-value-warn" : null;
        kpiRow2.Append(GtkUiHelpers.CreateKpiTile(
            fatigueValue,
            "卷尾失分占比",
            s.LateSectionMistakeRatio is >= 0.45m ? "后半段疲劳较明显" : "后半段表现平稳",
            fatigueCss));

        var topTopic = snapshot.RecurringTopics.FirstOrDefault();
        kpiRow2.Append(GtkUiHelpers.CreateKpiTile(
            topTopic?.TopicTag ?? "暂无",
            "首要薄弱知识点",
            topTopic is not null ? $"累计 {topTopic.OccurrenceCount} 次 / {topTopic.PaperCount} 卷" : "数据积累中"));

        _kpiStrip.Append(kpiRow1);
        _kpiStrip.Append(kpiRow2);

        // 2. Latest Study Plan Draft Hero Card
        _latestDraft = snapshot.RecentStudyPlanDrafts.FirstOrDefault();
        if (_latestDraft is { } draft)
        {
            _studyPlanTitleLabel.SetText($"阶段复盘建议 · {draft.Title}");
            _studyPlanMetaLabel.SetText(
                $"基于近 {draft.WindowDays} 天内 {draft.PaperCountAnalyzed} 份试卷生成 · 审阅后可一键转为正式跟进课题");
            GtkUiHelpers.ClearBoxChildren(_studyPlanBadgeHost);
            _studyPlanBadgeHost.Append(GtkUiHelpers.CreateBadge(
                PaperEvidenceWireNames.ToChineseStudyPlanStatus(draft.Status),
                StudyPlanBadgeCss(draft.Status)));
            _studyPlanCard.SetVisible(true);
        }
        else
        {
            _studyPlanCard.SetVisible(false);
        }

        // 3. Visual Trends (Weekly Cadence + Root Cause Breakdown)
        GtkUiHelpers.ClearBoxChildren(_trendsRow);
        var hasTrends = snapshot.WeeklyCadence.Count > 0 || snapshot.RootCauseBreakdown.Count > 0;
        _trendsRow.SetVisible(hasTrends);
        if (hasTrends)
        {
            // Left card: Weekly Cadence
            var cadenceCard = GtkUiHelpers.CreateCardBox(spacing: 8);
            cadenceCard.SetHexpand(true);
            cadenceCard.Append(GtkUiHelpers.CreateHeading("每周做卷节奏", "heading"));
            if (snapshot.WeeklyCadence.Count == 0)
            {
                cadenceCard.Append(GtkUiHelpers.CreateDimLabel("暂无按周记录。", caption: true));
            }
            else
            {
                var recentWeeks = snapshot.WeeklyCadence.TakeLast(5).ToArray();
                var maxPapers = Math.Max(1, recentWeeks.Max(w => w.PaperCount));
                foreach (var week in recentWeeks)
                {
                    var label = $"{week.WeekStart:MM/dd} 周";
                    var detail = $"{week.PaperCount} 卷 · 错 {week.MistakeCount} 题";
                    cadenceCard.Append(GtkUiHelpers.CreateProgressBarRow(
                        label,
                        detail,
                        (double)week.PaperCount / maxPapers));
                }
            }

            // Right card: Root Cause Evolution
            var rootCauseCard = GtkUiHelpers.CreateCardBox(spacing: 8);
            rootCauseCard.SetHexpand(true);
            rootCauseCard.Append(GtkUiHelpers.CreateHeading("错因演变（近30天 / 更早）", "heading"));
            if (snapshot.RootCauseBreakdown.Count == 0)
            {
                rootCauseCard.Append(GtkUiHelpers.CreateDimLabel("暂无结构化错因统计。", caption: true));
            }
            else
            {
                var topCauses = snapshot.RootCauseBreakdown.Take(4).ToArray();
                var maxTotal = Math.Max(1, topCauses.Max(c => c.TotalCount));
                foreach (var cause in topCauses)
                {
                    var detail = $"近30天 {cause.Recent30dCount} · 更早 {cause.PriorWindowCount}";
                    var barCss = cause.Recent30dCount > cause.PriorWindowCount
                        ? "xq-bar-coral"
                        : cause.Recent30dCount > 0
                            ? "xq-bar-warn"
                            : null;
                    rootCauseCard.Append(GtkUiHelpers.CreateProgressBarRow(
                        cause.Label,
                        detail,
                        (double)cause.TotalCount / maxTotal,
                        barCss));
                }
            }

            _trendsRow.Append(cadenceCard);
            _trendsRow.Append(rootCauseCard);
        }

        // 4. Recurring Topics Pills
        GtkUiHelpers.ClearBoxChildren(_topicsWrapBox);
        if (snapshot.RecurringTopics.Count > 0)
        {
            foreach (var topic in snapshot.RecurringTopics.Take(5))
            {
                _topicsWrapBox.Append(GtkUiHelpers.CreateBadge(
                    $"{topic.TopicTag} · {topic.OccurrenceCount}次",
                    topic.OccurrenceCount >= 3 ? "xq-pill-draft" : "xq-pill-neutral"));
            }

            _topicsCard.SetVisible(true);
        }
        else
        {
            _topicsCard.SetVisible(false);
        }

        // 5. Paper Timeline Cards
        GtkUiHelpers.ClearBoxChildren(_papersList);
        if (snapshot.Papers.Count == 0)
        {
            var emptyCard = GtkUiHelpers.CreateCardBox(spacing: 4, cardCss: "xq-empty-card");
            emptyCard.Append(GtkUiHelpers.CreateHeading("当前时间窗口内暂无试卷记录", "heading"));
            emptyCard.Append(GtkUiHelpers.CreateDimLabel("在手机端「拍卷子」上传多页试卷后，系统会自动汇聚到此时间线并生成结构化错题草稿。"));
            _papersList.Append(emptyCard);
            return;
        }

        foreach (var paper in snapshot.Papers)
        {
            var card = GtkUiHelpers.CreateCardBox(spacing: 8);

            var topRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
            var titleText = string.IsNullOrWhiteSpace(paper.Title)
                ? $"{paper.PaperDate:yyyy-MM-dd} · {paper.KindLabel}"
                : $"{paper.PaperDate:yyyy-MM-dd} · {paper.Title}";
            var paperTitle = GtkUiHelpers.CreateHeading(titleText, "heading");
            paperTitle.SetHexpand(true);
            topRow.Append(paperTitle);
            topRow.Append(GtkUiHelpers.CreateBadge(paper.KindLabel, "xq-pill-neutral"));
            topRow.Append(GtkUiHelpers.CreateBadge(paper.ConfirmationLabel, ConfirmationBadgeCss(paper.ConfirmationState)));
            card.Append(topRow);

            var metaRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
            var leftMeta = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
            leftMeta.SetHexpand(true);

            var gradingNote = PaperEvidenceWireNames.ToChineseGradingSource(paper.GradingSource);
            var metaText = $"{paper.ScoreDisplay}  ·  错题 {paper.MistakeCount ?? paper.Mistakes.Count} 道  ·  共 {paper.Pages.Count} 页  ·  {gradingNote}";
            leftMeta.Append(GtkUiHelpers.CreateDimLabel(metaText, caption: true));
            if (!string.IsNullOrWhiteSpace(paper.ExtractionSummary))
            {
                leftMeta.Append(GtkUiHelpers.CreateBodyLabel(paper.ExtractionSummary));
            }

            var actionsBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            actionsBox.SetValign(Gtk.Align.Center);

            var capturedPaper = paper;
            if (paper.ConfirmationState is PaperConfirmationState.AwaitingExtraction or PaperConfirmationState.ExtractionFailed)
            {
                var extractBtn = Gtk.Button.NewWithLabel("AI 视觉解析");
                extractBtn.AddCssClass("suggested-action");
                extractBtn.OnClicked += async (_, _) => await TriggerExtractionForPaperAsync(capturedPaper, extractBtn);
                actionsBox.Append(extractBtn);
            }

            var inspectLabel = paper.ConfirmationState == PaperConfirmationState.HumanConfirmed
                ? "查看错题"
                : "核对并确认";
            var inspectBtn = Gtk.Button.NewWithLabel(inspectLabel);
            if (paper.ConfirmationState == PaperConfirmationState.DraftReady)
            {
                inspectBtn.AddCssClass("suggested-action");
            }

            inspectBtn.OnClicked += async (_, _) => await ShowPaperDetailDialogAsync(capturedPaper);
            actionsBox.Append(inspectBtn);

            metaRow.Append(leftMeta);
            metaRow.Append(actionsBox);
            card.Append(metaRow);

            _papersList.Append(card);
        }
    }

    public void RenderEmpty(string statusText)
    {
        _statusLabel.SetText(string.Empty);
        GtkUiHelpers.ClearBoxChildren(_kpiStrip);
        _studyPlanCard.SetVisible(false);
        _trendsRow.SetVisible(false);
        _topicsCard.SetVisible(false);
        GtkUiHelpers.ClearBoxChildren(_papersList);

        var card = GtkUiHelpers.CreateCardBox(spacing: 4, cardCss: "xq-empty-card");
        card.Append(GtkUiHelpers.CreateDimLabel(statusText));
        _papersList.Append(card);
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
                "演示模式提示",
                "登录真实云端环境并配置 GEMINI_API_KEY 后，点击此处即可调用 extract-paper-v1 自动识别卷面错题草稿。");
            return;
        }

        triggerButton.SetSensitive(false);
        triggerButton.SetLabel("正在解析…");
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
                    "AI 解析暂未完成",
                    $"服务端返回：{result.Failure?.Code ?? "UNKNOWN"}。原卷图片和已有记录不受影响，可稍后重试。");
                return;
            }

            await GtkUiHelpers.ShowMessageDialogAsync(
                _dialogParent,
                "AI 错题草稿已生成",
                $"已分析 {result.Receipt.PagesAnalyzed} 页，提取 {result.Receipt.MistakeItemsSaved} 条错题草稿。请核对后确认入库。");
            if (_reloadCurrentAsync is not null)
            {
                await _reloadCurrentAsync();
            }
        }
        finally
        {
            triggerButton.SetSensitive(true);
            triggerButton.SetLabel("AI 视觉解析");
        }
    }

    private async Task ShowPaperDetailDialogAsync(PaperSubmissionSummary paper)
    {
        var dialog = Adw.Dialog.New();
        dialog.SetTitle(paper.Title ?? $"{paper.PaperDate:yyyy-MM-dd} {paper.KindLabel}");
        dialog.SetContentWidth(680);
        dialog.SetContentHeight(620);

        var toolbar = Adw.ToolbarView.New();
        toolbar.AddTopBar(Adw.HeaderBar.New());

        var content = Gtk.Box.New(Gtk.Orientation.Vertical, 14);
        content.SetMarginStart(20);
        content.SetMarginEnd(20);
        content.SetMarginTop(16);
        content.SetMarginBottom(20);

        // Header badges row
        var badgeRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        badgeRow.Append(GtkUiHelpers.CreateBadge(paper.PaperDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "xq-pill-neutral"));
        badgeRow.Append(GtkUiHelpers.CreateBadge(paper.KindLabel, "xq-pill-neutral"));
        badgeRow.Append(GtkUiHelpers.CreateBadge(paper.ConfirmationLabel, ConfirmationBadgeCss(paper.ConfirmationState)));
        badgeRow.Append(GtkUiHelpers.CreateBadge(
            PaperEvidenceWireNames.ToChineseGradingSource(paper.GradingSource),
            paper.GradingSource == PaperGradingSource.AiTentativeUnmarked ? "xq-pill-ai" : "xq-pill-neutral"));
        content.Append(badgeRow);

        if (!string.IsNullOrWhiteSpace(paper.ExtractionSummary))
        {
            var summaryCard = GtkUiHelpers.CreateCardBox(spacing: 4);
            summaryCard.Append(GtkUiHelpers.CreateHeading("卷面总体小结", "heading"));
            summaryCard.Append(GtkUiHelpers.CreateBodyLabel(paper.ExtractionSummary));
            content.Append(summaryCard);
        }

        // Structured Mistakes
        content.Append(GtkUiHelpers.CreateHeading($"结构化错题项（{paper.Mistakes.Count}）", "title-4"));
        if (paper.Mistakes.Count == 0)
        {
            content.Append(GtkUiHelpers.CreateDimLabel("暂无结构化错题项。"));
        }
        else
        {
            foreach (var m in paper.Mistakes)
            {
                var mistakeCard = GtkUiHelpers.CreateCardBox(spacing: 6);
                var mTop = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
                var pts = m.PointsLost is { } lost ? $" · -{lost:0.##}分" : string.Empty;
                var mTitle = GtkUiHelpers.CreateHeading($"第{m.PageIndex}页 · {m.QuestionLabel}{pts}", "heading");
                mTitle.SetHexpand(true);
                mTop.Append(mTitle);
                mTop.Append(GtkUiHelpers.CreateBadge(
                    PaperEvidenceWireNames.ToChineseLabel(m.ErrorRootCause),
                    "xq-pill-draft"));
                mTop.Append(GtkUiHelpers.CreateBadge(
                    PaperEvidenceWireNames.ToChineseProvenance(m.Provenance),
                    string.Equals(m.Provenance, "human_confirmed", StringComparison.OrdinalIgnoreCase)
                        ? "xq-pill-confirmed"
                        : "xq-pill-ai"));
                mistakeCard.Append(mTop);

                mistakeCard.Append(GtkUiHelpers.CreateBodyLabel(m.Summary));

                var mFoot = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
                mFoot.Append(GtkUiHelpers.CreateDimLabel(
                    PaperEvidenceWireNames.ToChineseLabel(m.PaperSectionPosition),
                    caption: true));
                if (m.CorrectedOnPaper is { } corr)
                {
                    mFoot.Append(GtkUiHelpers.CreateDimLabel(corr ? "· 卷面已订正" : "· 卷面未订正", caption: true));
                }

                if (m.TopicTags.Count > 0)
                {
                    mFoot.Append(GtkUiHelpers.CreateDimLabel("· 知识点：" + string.Join(" / ", m.TopicTags), caption: true));
                }

                mistakeCard.Append(mFoot);
                content.Append(mistakeCard);
            }
        }

        // Pages section
        content.Append(GtkUiHelpers.CreateHeading($"试卷页图（{paper.Pages.Count} 页）", "title-4"));
        foreach (var pg in paper.Pages)
        {
            var pageRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
            var pageLabel = GtkUiHelpers.CreateBodyLabel($"第 {pg.PageIndex} 页 · {pg.ByteSize / 1024} KiB");
            pageLabel.SetHexpand(true);
            pageRow.Append(pageLabel);
            pageRow.Append(GtkUiHelpers.CreateBadge(
                pg.HeaderRedacted ? "卷头已脱敏" : "未遮挡卷头",
                pg.HeaderRedacted ? "xq-pill-confirmed" : "xq-pill-neutral"));

            if (_attachmentDownloader is not null)
            {
                var previewBtn = Gtk.Button.NewWithLabel("预览原图");
                var capturedPage = pg;
                previewBtn.OnClicked += async (_, _) =>
                    await PreviewPrivateAttachmentAsync(dialog, capturedPage);
                pageRow.Append(previewBtn);
            }

            content.Append(pageRow);
        }

        // Human confirmation section for unconfirmed papers
        if (paper.ConfirmationState != PaperConfirmationState.HumanConfirmed)
        {
            var confirmCard = GtkUiHelpers.CreateCardBox(spacing: 10, cardCss: "xq-hero-card");
            confirmCard.Append(GtkUiHelpers.CreateHeading("人工核对并确认为权威记录", "heading"));
            confirmCard.Append(GtkUiHelpers.CreateDimLabel(
                "确认后将计入孩子的权威均分与长期趋势，且不会被后续 AI 解析覆盖。",
                caption: true));

            var fieldsRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            fieldsRow.SetHomogeneous(true);

            var earnedEntry = Gtk.Entry.New();
            earnedEntry.SetPlaceholderText("实得分（可选）");
            if (paper.ScoreEarned is { } se)
            {
                earnedEntry.SetText(se.ToString("0.##", CultureInfo.InvariantCulture));
            }

            var totalEntry = Gtk.Entry.New();
            totalEntry.SetPlaceholderText("满分（如 100）");
            if (paper.ScoreTotal is { } st)
            {
                totalEntry.SetText(st.ToString("0.##", CultureInfo.InvariantCulture));
            }

            var mistakesEntry = Gtk.Entry.New();
            mistakesEntry.SetPlaceholderText("错题数");
            mistakesEntry.SetText((paper.MistakeCount ?? paper.Mistakes.Count).ToString(CultureInfo.InvariantCulture));

            fieldsRow.Append(earnedEntry);
            fieldsRow.Append(totalEntry);
            fieldsRow.Append(mistakesEntry);
            confirmCard.Append(fieldsRow);

            var summaryEntry = Gtk.Entry.New();
            summaryEntry.SetPlaceholderText("核对后的卷面小结（可选）");
            if (!string.IsNullOrWhiteSpace(paper.ExtractionSummary))
            {
                summaryEntry.SetText(paper.ExtractionSummary);
            }

            confirmCard.Append(summaryEntry);

            var confirmBtn = Gtk.Button.NewWithLabel("确认并锁定为权威记录");
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
                    await GtkUiHelpers.ShowMessageDialogAsync(dialog, "分数输入有误", "实得分不能大于满分。");
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
                        await GtkUiHelpers.ShowMessageDialogAsync(
                            dialog,
                            "确认失败",
                            $"服务端拒绝或暂不可用：{saveResult.Failure?.Code ?? "UNKNOWN"}");
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
            confirmCard.Append(confirmBtn);
            content.Append(confirmCard);
        }

        var closeBtn = Gtk.Button.NewWithLabel("关闭");
        closeBtn.SetHalign(Gtk.Align.End);
        closeBtn.OnClicked += (_, _) => dialog.Close();
        content.Append(closeBtn);

        toolbar.SetContent(GtkUiHelpers.WrapInScroll(content));
        dialog.SetChild(toolbar);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.OnClosed += (_, _) => tcs.TrySetResult();
        dialog.Present(_dialogParent);
        await tcs.Task;
    }

    private async Task PreviewPrivateAttachmentAsync(Gtk.Widget parent, PaperPageSummary page)
    {
        if (_attachmentDownloader is null)
        {
            return;
        }

        var download = await _attachmentDownloader.DownloadImageAsync(page.BucketId, page.ObjectName);
        if (!download.IsSuccess || download.Image is null)
        {
            await GtkUiHelpers.ShowMessageDialogAsync(
                parent,
                "无法加载原卷图片",
                $"私有附件下载失败：{download.Failure?.Code ?? "UNKNOWN"}");
            return;
        }

        var bytes = GLib.Bytes.New(download.Image.Bytes);
        var texture = Gdk.Texture.NewFromBytes(bytes);
        var picture = Gtk.Picture.NewForPaintable(texture);
        picture.SetCanShrink(true);

        var imgDialog = Adw.Dialog.New();
        imgDialog.SetTitle($"第 {page.PageIndex} 页原卷预览");
        imgDialog.SetContentWidth(780);
        imgDialog.SetContentHeight(640);

        var tb = Adw.ToolbarView.New();
        tb.AddTopBar(Adw.HeaderBar.New());
        tb.SetContent(GtkUiHelpers.WrapInScroll(picture));
        imgDialog.SetChild(tb);
        imgDialog.Present(parent);
    }

    private async Task ShowStudyPlanDraftDialogAsync(StudyPlanDraftSummary draft)
    {
        var dialog = Adw.Dialog.New();
        dialog.SetTitle(draft.Title);
        dialog.SetContentWidth(640);
        dialog.SetContentHeight(580);

        var toolbar = Adw.ToolbarView.New();
        toolbar.AddTopBar(Adw.HeaderBar.New());

        var content = Gtk.Box.New(Gtk.Orientation.Vertical, 14);
        content.SetMarginStart(20);
        content.SetMarginEnd(20);
        content.SetMarginTop(16);
        content.SetMarginBottom(20);

        var metaRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        metaRow.Append(GtkUiHelpers.CreateBadge(
            PaperEvidenceWireNames.ToChineseStudyPlanStatus(draft.Status),
            StudyPlanBadgeCss(draft.Status)));
        metaRow.Append(GtkUiHelpers.CreateDimLabel(
            $"分析窗口：近 {draft.WindowDays} 天 · 样本：{draft.PaperCountAnalyzed} 份试卷 · 仅草案，需由人工决定是否采纳",
            caption: true));
        content.Append(metaRow);

        var briefCard = GtkUiHelpers.CreateCardBox(spacing: 6);
        briefCard.Append(GtkUiHelpers.CreateHeading("阶段复盘简报", "heading"));
        briefCard.Append(GtkUiHelpers.CreateBodyLabel(draft.BriefMarkdown));
        content.Append(briefCard);

        var proposedActions = PaperEvidenceWireNames.ParseProposedActions(draft.ProposedActionsJson);
        if (proposedActions.Count > 0)
        {
            content.Append(GtkUiHelpers.CreateHeading($"候选教学行动建议（{proposedActions.Count}）", "title-4"));
            foreach (var action in proposedActions)
            {
                var actionCard = GtkUiHelpers.CreateCardBox(spacing: 6);
                var aTop = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
                var aTitle = GtkUiHelpers.CreateHeading(action.Title, "heading");
                aTitle.SetHexpand(true);
                aTop.Append(aTitle);
                aTop.Append(GtkUiHelpers.CreateBadge(action.KindLabel, "xq-pill-ai"));
                actionCard.Append(aTop);

                actionCard.Append(GtkUiHelpers.CreateBodyLabel($"建议下一步：{action.NextAction}"));
                if (!string.IsNullOrWhiteSpace(action.Rationale))
                {
                    actionCard.Append(GtkUiHelpers.CreateDimLabel($"依据：{action.Rationale}", caption: true));
                }

                if (!string.IsNullOrWhiteSpace(action.TargetDate))
                {
                    actionCard.Append(GtkUiHelpers.CreateDimLabel($"建议节点：{action.TargetDate}", caption: true));
                }

                if (!string.Equals(draft.Status, "dismissed", StringComparison.OrdinalIgnoreCase))
                {
                    var adoptBtn = Gtk.Button.NewWithLabel("采纳为跟进课题");
                    adoptBtn.AddCssClass("suggested-action");
                    adoptBtn.SetHalign(Gtk.Align.End);
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
                    actionCard.Append(adoptBtn);
                }

                content.Append(actionCard);
            }
        }

        var footer = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        footer.SetHalign(Gtk.Align.End);

        if (string.Equals(draft.Status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            var dismissBtn = Gtk.Button.NewWithLabel("忽略此草案");
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
            await GtkUiHelpers.ShowMessageDialogAsync(
                dialog,
                "更新草案状态失败",
                $"服务端返回：{result.Failure?.Code ?? "UNKNOWN"}");
            return;
        }

        dialog.Close();
        if (_reloadCurrentAsync is not null)
        {
            await _reloadCurrentAsync();
        }
    }

    private static string ConfirmationBadgeCss(PaperConfirmationState state) => state switch
    {
        PaperConfirmationState.HumanConfirmed => "xq-pill-confirmed",
        PaperConfirmationState.DraftReady => "xq-pill-draft",
        PaperConfirmationState.AwaitingExtraction => "xq-pill-ai",
        PaperConfirmationState.ExtractionFailed => "xq-pill-overdue",
        _ => "xq-pill-neutral",
    };

    private static string StudyPlanBadgeCss(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "fully_adopted" or "partially_adopted" => "xq-pill-confirmed",
        "dismissed" => "xq-pill-neutral",
        _ => "xq-pill-ai",
    };

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
