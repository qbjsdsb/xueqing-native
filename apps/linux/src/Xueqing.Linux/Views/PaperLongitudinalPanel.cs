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
/// Native GTK4 + libadwaita longitudinal exam-paper timeline, KPI strip,
/// structured mistake inspector, private image previewer, and AI study-plan
/// draft viewer (interoperable with Xueqing.Mcp).
/// </summary>
internal sealed class PaperLongitudinalPanel
{
    private readonly Gtk.Widget _dialogParent;
    private readonly Gtk.Box _root;
    private readonly Gtk.Label _kpiLabel;
    private readonly Gtk.Label _topicsLabel;
    private readonly Gtk.Box _papersList;
    private readonly Gtk.Box _studyPlanCard;
    private readonly Gtk.Label _studyPlanTitleLabel;
    private readonly Gtk.Label _studyPlanMetaLabel;
    private readonly Gtk.Button _viewPlanButton;

    private SqliteObservationDraftStore? _prototypePlanStore;
    private StudyPlanDraftSummary? _latestDraft;
    private IPaperAttachmentDownloader? _attachmentDownloader;

    public PaperLongitudinalPanel(Gtk.Widget dialogParent)
    {
        _dialogParent = dialogParent ?? throw new ArgumentNullException(nameof(dialogParent));

        _root = Gtk.Box.New(Gtk.Orientation.Vertical, 10);
        GtkUiHelpers.SetAutomationId(_root, "PaperLongitudinalSection");

        _root.Append(GtkUiHelpers.CreateHeading("试卷时间线与 AI 阶段复盘", "title-4"));

        _kpiLabel = GtkUiHelpers.CreateDimLabel("正在加载试卷纵向指标…");
        _topicsLabel = GtkUiHelpers.CreateDimLabel(string.Empty, caption: true);
        _root.Append(_kpiLabel);
        _root.Append(_topicsLabel);

        // Latest Study Plan Draft Card
        _studyPlanCard = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
        _studyPlanCard.AddCssClass("card");
        _studyPlanCard.SetVisible(false);

        var planInner = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
        planInner.SetMarginStart(14);
        planInner.SetMarginEnd(14);
        planInner.SetMarginTop(10);
        planInner.SetMarginBottom(10);
        planInner.SetHexpand(true);

        var planTextBox = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
        planTextBox.SetHexpand(true);
        _studyPlanTitleLabel = GtkUiHelpers.CreateHeading(string.Empty, "heading");
        _studyPlanMetaLabel = GtkUiHelpers.CreateDimLabel(string.Empty, caption: true);
        planTextBox.Append(_studyPlanTitleLabel);
        planTextBox.Append(_studyPlanMetaLabel);

        _viewPlanButton = Gtk.Button.NewWithLabel("审阅计划草案");
        _viewPlanButton.AddCssClass("suggested-action");
        _viewPlanButton.SetValign(Gtk.Align.Center);
        _viewPlanButton.OnClicked += async (_, _) =>
        {
            if (_latestDraft is { } draft)
            {
                await ShowStudyPlanDraftDialogAsync(draft);
            }
        };

        planInner.Append(planTextBox);
        planInner.Append(_viewPlanButton);
        _studyPlanCard.Append(planInner);
        _root.Append(_studyPlanCard);

        _papersList = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        _root.Append(_papersList);
    }

    public Gtk.Widget Widget => _root;

    public async Task LoadPrototypeAsync(StudentSummary? student)
    {
        _attachmentDownloader = null;
        if (student is null)
        {
            RenderEmpty("选择学生后可查看历次试卷趋势与 AI 阶段计划草案。");
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

        var snapshot = PrototypePaperLongitudinalFixtureFactory.Create(
            studentUuid,
            student.DisplayName,
            student.PrimarySubject,
            PrototypeDraftIdentity.OrganizationId,
            PrototypeDraftIdentity.SubjectProfileId,
            PrototypeDraftIdentity.AssignmentId,
            drafts);
        RenderSnapshot(snapshot);
    }

    public void RenderSnapshot(
        StudentPaperLongitudinalSnapshot snapshot,
        IPaperAttachmentDownloader? attachmentDownloader = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _attachmentDownloader = attachmentDownloader;

        var s = snapshot.Summary;
        var avgText = s.TeacherMarkedAvgScoreRatio is { } avg
            ? $"权威均分 {avg * 100m:0.0}%"
            : "权威均分待补充";
        var fatigueText = s.LateSectionMistakeRatio is { } fatigue
            ? $"卷尾大题失分占比 {fatigue * 100m:0}%"
            : "卷尾失分占比待统计";

        _kpiLabel.SetText(
            $"近 {snapshot.WindowDays} 天：共 {s.TotalPapers} 份卷（已确认 {s.ConfirmedPapers} · 待核对 {s.DraftReadyPapers}）  |  {avgText}  |  {fatigueText}");

        if (snapshot.RecurringTopics.Count > 0)
        {
            var topTopics = snapshot.RecurringTopics
                .Take(4)
                .Select(t => $"{t.TopicTag}（{t.OccurrenceCount}次/{t.PaperCount}卷）");
            _topicsLabel.SetText("高频复发主题：" + string.Join(" · ", topTopics));
            _topicsLabel.SetVisible(true);
        }
        else
        {
            _topicsLabel.SetVisible(false);
        }

        _latestDraft = snapshot.RecentStudyPlanDrafts.FirstOrDefault();
        if (_latestDraft is { } draft)
        {
            _studyPlanTitleLabel.SetText($"[AI 阶段计划草案] {draft.Title}");
            _studyPlanMetaLabel.SetText(
                $"窗口 {draft.WindowDays} 天 · 分析 {draft.PaperCountAnalyzed} 份卷 · 状态：{draft.Status}（仅草案，未自动改动学情 Case）");
            _studyPlanCard.SetVisible(true);
        }
        else
        {
            _studyPlanCard.SetVisible(false);
        }

        GtkUiHelpers.ClearBoxChildren(_papersList);
        if (snapshot.Papers.Count == 0)
        {
            _papersList.Append(GtkUiHelpers.CreateDimLabel("当前时间窗口内暂无试卷记录。"));
            return;
        }

        foreach (var paper in snapshot.Papers)
        {
            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 14);
            row.SetMarginTop(6);
            row.SetMarginBottom(6);

            var left = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
            left.SetHexpand(true);
            var titleText = string.IsNullOrWhiteSpace(paper.Title)
                ? $"{paper.PaperDate:yyyy-MM-dd} · {paper.KindLabel}"
                : $"{paper.PaperDate:yyyy-MM-dd} · {paper.Title}";
            left.Append(GtkUiHelpers.CreateHeading(titleText, "heading"));

            var meta = $"{paper.KindLabel} · {paper.ScoreDisplay} · 错题 {paper.MistakeCount ?? paper.Mistakes.Count} 道 · {paper.Pages.Count} 页 · {paper.ConfirmationLabel}";
            left.Append(GtkUiHelpers.CreateDimLabel(meta, caption: true));
            if (!string.IsNullOrWhiteSpace(paper.ExtractionSummary))
            {
                left.Append(GtkUiHelpers.CreateDimLabel(paper.ExtractionSummary));
            }

            var inspectBtn = Gtk.Button.NewWithLabel("查看错题");
            inspectBtn.SetValign(Gtk.Align.Center);
            var capturedPaper = paper;
            inspectBtn.OnClicked += async (_, _) => await ShowPaperDetailDialogAsync(capturedPaper);

            row.Append(left);
            row.Append(inspectBtn);
            _papersList.Append(row);
            _papersList.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        }
    }

    public void RenderEmpty(string statusText)
    {
        _kpiLabel.SetText(statusText);
        _topicsLabel.SetVisible(false);
        _studyPlanCard.SetVisible(false);
        GtkUiHelpers.ClearBoxChildren(_papersList);
    }

    private async Task ShowPaperDetailDialogAsync(PaperSubmissionSummary paper)
    {
        var dialog = Adw.Dialog.New();
        dialog.SetTitle(paper.Title ?? $"{paper.PaperDate:yyyy-MM-dd} {paper.KindLabel}");
        dialog.SetContentWidth(620);
        dialog.SetContentHeight(560);

        var toolbar = Adw.ToolbarView.New();
        toolbar.AddTopBar(Adw.HeaderBar.New());

        var content = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
        content.SetMarginStart(20);
        content.SetMarginEnd(20);
        content.SetMarginTop(16);
        content.SetMarginBottom(20);

        content.Append(GtkUiHelpers.CreateDimLabel(
            $"日期：{paper.PaperDate:yyyy-MM-dd}  |  类型：{paper.KindLabel}  |  分数：{paper.ScoreDisplay}  |  状态：{paper.ConfirmationLabel}"));

        if (!string.IsNullOrWhiteSpace(paper.ExtractionSummary))
        {
            content.Append(GtkUiHelpers.CreateBodyLabel(paper.ExtractionSummary));
        }

        content.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
        content.Append(GtkUiHelpers.CreateHeading($"结构化错题项（{paper.Mistakes.Count}）", "title-4"));

        if (paper.Mistakes.Count == 0)
        {
            content.Append(GtkUiHelpers.CreateDimLabel("暂无结构化错题项。"));
        }
        else
        {
            foreach (var m in paper.Mistakes)
            {
                var tags = m.TopicTags.Count > 0 ? $"  |  标签：{string.Join(" / ", m.TopicTags)}" : string.Empty;
                var pts = m.PointsLost is { } lost ? $"  |  -{lost:0.##}分" : string.Empty;
                var corr = m.CorrectedOnPaper switch
                {
                    true => "  |  已订正",
                    false => "  |  未订正",
                    null => string.Empty,
                };
                var header = $"{m.QuestionLabel}（第{m.PageIndex}页 · {PaperEvidenceWireNames.ToChineseLabel(m.ErrorRootCause)} · {PaperEvidenceWireNames.ToChineseLabel(m.PaperSectionPosition)}{pts}{corr}）";
                var block = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
                block.Append(GtkUiHelpers.CreateHeading(header, "heading"));
                block.Append(GtkUiHelpers.CreateBodyLabel(m.Summary));
                if (!string.IsNullOrEmpty(tags))
                {
                    block.Append(GtkUiHelpers.CreateDimLabel($"来源：{m.Provenance}{tags}", caption: true));
                }

                content.Append(block);
                content.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
            }
        }

        content.Append(GtkUiHelpers.CreateHeading($"试卷页图（{paper.Pages.Count} 页）", "title-4"));
        foreach (var pg in paper.Pages)
        {
            var pageRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
            var redactionNote = pg.HeaderRedacted ? "卷头已脱敏遮挡" : "未遮挡卷头";
            var pageLabel = GtkUiHelpers.CreateDimLabel($"第 {pg.PageIndex} 页 · {pg.ContentType} · {pg.ByteSize / 1024} KiB · {redactionNote}");
            pageLabel.SetHexpand(true);
            pageRow.Append(pageLabel);

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
        dialog.SetContentWidth(600);
        dialog.SetContentHeight(520);

        var toolbar = Adw.ToolbarView.New();
        toolbar.AddTopBar(Adw.HeaderBar.New());

        var content = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
        content.SetMarginStart(20);
        content.SetMarginEnd(20);
        content.SetMarginTop(16);
        content.SetMarginBottom(20);

        content.Append(GtkUiHelpers.CreateDimLabel(
            $"分析窗口：{draft.WindowDays} 天  |  样本卷数：{draft.PaperCountAnalyzed}  |  状态：{draft.Status}（AI 草案，需由老师审阅决定）"));
        content.Append(GtkUiHelpers.CreateHeading("阶段复盘简报", "title-4"));
        content.Append(GtkUiHelpers.CreateBodyLabel(draft.BriefMarkdown));

        if (!string.IsNullOrWhiteSpace(draft.ProposedActionsJson) && draft.ProposedActionsJson != "[]")
        {
            content.Append(Gtk.Separator.New(Gtk.Orientation.Horizontal));
            content.Append(GtkUiHelpers.CreateHeading("候选教学行动草案（待人工采纳）", "title-4"));
            content.Append(GtkUiHelpers.CreateDimLabel(draft.ProposedActionsJson));
        }

        var closeBtn = Gtk.Button.NewWithLabel("知道了");
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
