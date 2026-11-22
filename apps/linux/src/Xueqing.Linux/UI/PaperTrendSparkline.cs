using System.Runtime.Versioning;
using Xueqing.Windows.Core.Models;

[assembly: SupportedOSPlatform("linux")]

namespace Xueqing.Linux.UI;

/// <summary>
/// Handcrafted, zero-clutter Cairo visualizations for the Quiet Ink Editorial design:
/// 1. Minimalist Tufte-style score/cadence sparkline (no chunky progress bars or grid boxes)
/// 2. Deterministic one-sentence natural-language takeaway
/// 3. Clean exam-page silhouette canvas for prototype side-by-side grading preview.
/// </summary>
internal static class PaperTrendSparkline
{
    public static Gtk.Widget Create(StudentPaperLongitudinalSnapshot snapshot)
    {
        var container = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

        var chronological = snapshot.Papers
            .OrderBy(p => p.PaperDate)
            .ThenBy(p => p.CreatedAtServer)
            .ToArray();

        var scoredPoints = chronological
            .Where(p => p.ScoreEarned is not null && p.ScoreTotal is > 0)
            .Select(p => (
                Date: p.PaperDate,
                Ratio: Math.Clamp((double)(p.ScoreEarned!.Value / p.ScoreTotal!.Value), 0.0, 1.0),
                Confirmed: p.ConfirmationState == PaperConfirmationState.HumanConfirmed))
            .ToArray();

        if (scoredPoints.Length >= 2)
        {
            var avgRatio = snapshot.Summary.TeacherMarkedAvgScoreRatio is { } avg
                ? Math.Clamp((double)avg, 0.0, 1.0)
                : (double?)null;

            var area = Gtk.DrawingArea.New();
            area.SetContentHeight(84);
            area.SetHexpand(true);
            area.SetDrawFunc((_, cr, width, height) =>
                DrawScoreSparkline(cr, width, height, scoredPoints, avgRatio));
            container.Append(area);

            var axisRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            var first = scoredPoints[0];
            var last = scoredPoints[^1];
            var leftText = $"{first.Date:MM/dd} · {first.Ratio * 100:0}%";
            var rightText = last.Confirmed
                ? $"{last.Date:MM/dd} · {last.Ratio * 100:0}%"
                : $"{last.Date:MM/dd} · {last.Ratio * 100:0}%（待核对）";

            var leftLabel = Gtk.Label.New(leftText);
            leftLabel.SetHalign(Gtk.Align.Start);
            leftLabel.SetHexpand(true);
            leftLabel.AddCssClass("xq-date-col");

            var rightLabel = Gtk.Label.New(rightText);
            rightLabel.SetHalign(Gtk.Align.End);
            rightLabel.AddCssClass("xq-date-col");

            axisRow.Append(leftLabel);
            axisRow.Append(rightLabel);
            container.Append(axisRow);
        }
        else if (chronological.Length > 0)
        {
            var area = Gtk.DrawingArea.New();
            area.SetContentHeight(36);
            area.SetHexpand(true);
            area.SetDrawFunc((_, cr, width, height) =>
                DrawCadenceDots(cr, width, height, chronological));
            container.Append(area);
        }

        return container;
    }

    public static string BuildNaturalTakeaway(StudentPaperLongitudinalSnapshot snapshot)
    {
        var s = snapshot.Summary;
        if (s.TotalPapers == 0)
        {
            return $"近 {snapshot.WindowDays} 天暂无试卷记录。";
        }

        if (s.TotalPapers == 1)
        {
            return $"近 {snapshot.WindowDays} 天记录 1 份试卷，继续积累后会呈现更清晰的走势。";
        }

        var parts = new List<string>();

        var confirmedScored = snapshot.Papers
            .Where(p => p.ConfirmationState == PaperConfirmationState.HumanConfirmed &&
                        p.ScoreEarned is not null &&
                        p.ScoreTotal is > 0)
            .OrderBy(p => p.PaperDate)
            .ThenBy(p => p.CreatedAtServer)
            .ToArray();

        if (confirmedScored.Length >= 2)
        {
            var firstPct = Math.Round((double)(confirmedScored[0].ScoreEarned!.Value / confirmedScored[0].ScoreTotal!.Value) * 100.0);
            var lastPct = Math.Round((double)(confirmedScored[^1].ScoreEarned!.Value / confirmedScored[^1].ScoreTotal!.Value) * 100.0);
            var avgPart = s.TeacherMarkedAvgScoreRatio is { } avg ? $"（均分 {avg * 100m:0}%）" : string.Empty;

            if (lastPct - firstPct >= 4)
            {
                parts.Add($"近 {snapshot.WindowDays} 天 {s.TotalPapers} 份试卷，成绩从 {firstPct:0}% 升至 {lastPct:0}%{avgPart}");
            }
            else if (firstPct - lastPct >= 4)
            {
                parts.Add($"近 {snapshot.WindowDays} 天 {s.TotalPapers} 份试卷，成绩从 {firstPct:0}% 回落至 {lastPct:0}%{avgPart}");
            }
            else
            {
                parts.Add($"近 {snapshot.WindowDays} 天 {s.TotalPapers} 份试卷，成绩保持平稳{avgPart}");
            }
        }
        else if (s.TeacherMarkedAvgScoreRatio is { } avgOnly)
        {
            parts.Add($"近 {snapshot.WindowDays} 天 {s.TotalPapers} 份试卷，均分 {avgOnly * 100m:0}%");
        }
        else
        {
            parts.Add($"近 {snapshot.WindowDays} 天记录 {s.TotalPapers} 份试卷");
        }

        var topTopic = snapshot.RecurringTopics.FirstOrDefault();
        var risingCause = snapshot.RootCauseBreakdown
            .Where(r => r.Recent30dCount > r.PriorWindowCount)
            .OrderByDescending(r => r.Recent30dCount)
            .FirstOrDefault();
        var improvingCause = snapshot.RootCauseBreakdown
            .Where(r => r.PriorWindowCount > 0 && r.Recent30dCount < r.PriorWindowCount)
            .OrderByDescending(r => r.PriorWindowCount - r.Recent30dCount)
            .FirstOrDefault();

        var insights = new List<string>();
        if (topTopic is not null && topTopic.OccurrenceCount >= 2)
        {
            insights.Add($"「{topTopic.TopicTag}」反复失分");
        }

        if (s.LateSectionMistakeRatio is >= 0.45m)
        {
            insights.Add("卷尾失分偏多");
        }
        else if (risingCause is not null)
        {
            insights.Add($"近期「{risingCause.Label}」有所增多");
        }

        if (improvingCause is not null && insights.Count < 2)
        {
            insights.Add($"「{improvingCause.Label}」较此前减少");
        }

        if (insights.Count > 0)
        {
            return $"{parts[0]}；{string.Join("，", insights)}。";
        }

        return $"{parts[0]}。";
    }

    public static Gtk.Widget CreateSimulatedPaperCanvas(
        PaperSubmissionSummary paper,
        PaperPageSummary page,
        IReadOnlyList<PaperMistakeItemSummary> pageMistakes)
    {
        var area = Gtk.DrawingArea.New();
        area.SetHexpand(true);
        area.SetVexpand(true);
        area.SetContentWidth(360);
        area.SetContentHeight(480);
        area.SetDrawFunc((_, cr, width, height) =>
            DrawPaperSilhouette(cr, width, height, page, pageMistakes));
        return area;
    }

    private static void DrawScoreSparkline(
        Cairo.Context cr,
        int width,
        int height,
        (DateOnly Date, double Ratio, bool Confirmed)[] points,
        double? avgRatio)
    {
        if (width < 40 || height < 24 || points.Length < 2)
        {
            return;
        }

        var isDark = Adw.StyleManager.GetDefault().GetDark();
        var (r, g, b) = isDark ? (0.533, 0.706, 0.863) : (0.173, 0.369, 0.541); // #88B4DC / #2C5E8A
        var (bgR, bgG, bgB) = isDark ? (0.078, 0.082, 0.090) : (0.980, 0.976, 0.969);
        var (warnR, warnG, warnB) = isDark ? (0.851, 0.635, 0.306) : (0.761, 0.518, 0.133);
        var fgAlpha = isDark ? 1.0 : 0.0;

        cr.Antialias = Cairo.Antialias.Good;

        const double padX = 10.0;
        const double padTop = 12.0;
        const double padBottom = 10.0;
        var plotW = width - padX * 2;
        var plotH = height - padTop - padBottom;

        var minVal = Math.Max(0.0, Math.Min(points.Min(p => p.Ratio), avgRatio ?? 1.0) - 0.12);
        var maxVal = Math.Min(1.0, Math.Max(points.Max(p => p.Ratio), avgRatio ?? 0.0) + 0.08);
        if (maxVal - minVal < 0.25)
        {
            minVal = Math.Max(0.0, maxVal - 0.25);
        }

        double MapY(double ratio) =>
            padTop + (1.0 - (ratio - minVal) / (maxVal - minVal)) * plotH;

        var coords = new (double X, double Y, bool Confirmed)[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var x = padX + (plotW * i / (points.Length - 1));
            coords[i] = (x, MapY(points[i].Ratio), points[i].Confirmed);
        }

        // Subtle dashed average reference line
        if (avgRatio is { } avg)
        {
            var avgY = MapY(avg);
            cr.SetSourceRgba(fgAlpha, fgAlpha, fgAlpha, 0.12);
            cr.LineWidth = 1.0;
            cr.SetDash([3.0, 4.0], 0.0);
            cr.MoveTo(padX, avgY);
            cr.LineTo(width - padX, avgY);
            cr.Stroke();
            cr.SetDash([], 0.0);
        }

        // Whisper-soft area fill underneath the curve
        cr.MoveTo(coords[0].X, height - padBottom);
        cr.LineTo(coords[0].X, coords[0].Y);
        TraceSmoothCurve(cr, coords);
        cr.LineTo(coords[^1].X, height - padBottom);
        cr.ClosePath();
        cr.SetSourceRgba(r, g, b, isDark ? 0.10 : 0.075);
        cr.Fill();

        // Crisp 1.8px studio slate-blue curve
        cr.MoveTo(coords[0].X, coords[0].Y);
        TraceSmoothCurve(cr, coords);
        cr.SetSourceRgba(r, g, b, 0.92);
        cr.LineWidth = 1.8;
        cr.LineCap = Cairo.LineCap.Round;
        cr.LineJoin = Cairo.LineJoin.Round;
        cr.Stroke();

        // Minimal ink dots (solid for confirmed, hollow warm ring for draft)
        foreach (var pt in coords)
        {
            if (pt.Confirmed)
            {
                cr.Arc(pt.X, pt.Y, 3.4, 0, Math.PI * 2);
                cr.SetSourceRgba(r, g, b, 1.0);
                cr.Fill();
            }
            else
            {
                cr.Arc(pt.X, pt.Y, 3.8, 0, Math.PI * 2);
                cr.SetSourceRgba(bgR, bgG, bgB, 1.0);
                cr.FillPreserve();
                cr.SetSourceRgba(warnR, warnG, warnB, 0.95);
                cr.LineWidth = 1.8;
                cr.Stroke();
            }
        }
    }

    private static void TraceSmoothCurve(
        Cairo.Context cr,
        (double X, double Y, bool Confirmed)[] coords)
    {
        for (var i = 0; i < coords.Length - 1; i++)
        {
            var p0 = coords[i];
            var p1 = coords[i + 1];
            var midX = (p0.X + p1.X) * 0.5;
            cr.CurveTo(midX, p0.Y, midX, p1.Y, p1.X, p1.Y);
        }
    }

    private static void DrawCadenceDots(
        Cairo.Context cr,
        int width,
        int height,
        PaperSubmissionSummary[] papers)
    {
        var isDark = Adw.StyleManager.GetDefault().GetDark();
        var (r, g, b) = isDark ? (0.533, 0.706, 0.863) : (0.173, 0.369, 0.541);
        var fgAlpha = isDark ? 1.0 : 0.0;
        var y = height * 0.5;
        const double padX = 12.0;

        cr.Antialias = Cairo.Antialias.Good;
        cr.SetSourceRgba(fgAlpha, fgAlpha, fgAlpha, 0.12);
        cr.LineWidth = 1.0;
        cr.MoveTo(padX, y);
        cr.LineTo(width - padX, y);
        cr.Stroke();

        var count = papers.Length;
        for (var i = 0; i < count; i++)
        {
            var x = count == 1 ? width * 0.5 : padX + (width - padX * 2) * i / (count - 1);
            cr.Arc(x, y, 4.0, 0, Math.PI * 2);
            cr.SetSourceRgba(r, g, b, 0.9);
            cr.Fill();
        }
    }

    private static void DrawPaperSilhouette(
        Cairo.Context cr,
        int width,
        int height,
        PaperPageSummary page,
        IReadOnlyList<PaperMistakeItemSummary> pageMistakes)
    {
        var isDark = Adw.StyleManager.GetDefault().GetDark();
        cr.Antialias = Cairo.Antialias.Good;

        // Centered A4-ratio paper sheet inside the calm desk canvas
        var margin = 24.0;
        var availW = Math.Max(120.0, width - margin * 2);
        var availH = Math.Max(160.0, height - margin * 2);
        var ratio = 0.707; // A4 width/height
        var sheetW = Math.Min(availW, availH * ratio);
        var sheetH = sheetW / ratio;
        var x = (width - sheetW) * 0.5;
        var y = (height - sheetH) * 0.5;

        // Subtle paper sheet fill & border
        cr.Rectangle(x, y, sheetW, sheetH);
        if (isDark)
        {
            cr.SetSourceRgba(0.155, 0.160, 0.172, 1.0);
        }
        else
        {
            cr.SetSourceRgba(1.0, 0.998, 0.992, 1.0);
        }

        cr.FillPreserve();
        cr.SetSourceRgba(isDark ? 1.0 : 0.0, isDark ? 1.0 : 0.0, isDark ? 1.0 : 0.0, isDark ? 0.10 : 0.09);
        cr.LineWidth = 1.0;
        cr.Stroke();

        // Top 14% header area (solid redacted bar if HeaderRedacted is true)
        var innerPad = sheetW * 0.09;
        var headerH = sheetH * 0.11;
        if (page.HeaderRedacted)
        {
            cr.Rectangle(x + innerPad, y + innerPad * 0.7, sheetW - innerPad * 2, headerH * 0.65);
            cr.SetSourceRgba(isDark ? 0.08 : 0.16, isDark ? 0.085 : 0.17, isDark ? 0.095 : 0.19, 0.85);
            cr.Fill();
        }
        else
        {
            cr.MoveTo(x + innerPad, y + innerPad * 1.2);
            cr.LineTo(x + sheetW - innerPad, y + innerPad * 1.2);
            cr.SetSourceRgba(isDark ? 1.0 : 0.0, isDark ? 1.0 : 0.0, isDark ? 1.0 : 0.0, 0.12);
            cr.LineWidth = 1.0;
            cr.Stroke();
        }

        // Ruled question lines representing exam layout
        var lineColor = isDark ? 1.0 : 0.0;
        var startY = y + innerPad + headerH;
        var usableH = sheetH - innerPad * 2 - headerH;
        const int blocks = 6;
        for (var i = 0; i < blocks; i++)
        {
            var by = startY + (usableH / blocks) * i;
            cr.SetSourceRgba(lineColor, lineColor, lineColor, 0.14);
            cr.LineWidth = 2.0;
            cr.MoveTo(x + innerPad, by);
            cr.LineTo(x + sheetW - innerPad * (i % 2 == 0 ? 1.0 : 1.8), by);
            cr.Stroke();

            cr.SetSourceRgba(lineColor, lineColor, lineColor, 0.07);
            cr.LineWidth = 1.2;
            cr.MoveTo(x + innerPad, by + 11);
            cr.LineTo(x + sheetW - innerPad * 1.5, by + 11);
            cr.Stroke();
        }

        // Restrained crimson-ink annotations corresponding to mistakes on this page
        for (var i = 0; i < Math.Min(pageMistakes.Count, 3); i++)
        {
            var m = pageMistakes[i];
            var frac = m.PaperSectionPosition switch
            {
                PaperSectionPosition.Early => 0.18,
                PaperSectionPosition.Middle => 0.48,
                PaperSectionPosition.Late => 0.78,
                _ => 0.32 + i * 0.22,
            };
            var my = startY + usableH * Math.Clamp(frac + i * 0.06, 0.12, 0.88);
            var mx = x + innerPad + 14;
            cr.Arc(mx, my, 9.0, 0, Math.PI * 2);
            cr.SetSourceRgba(0.80, 0.32, 0.32, 0.72);
            cr.LineWidth = 1.6;
            cr.Stroke();
        }
    }
}
