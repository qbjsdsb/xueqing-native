using System.Globalization;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("linux")]

namespace Xueqing.Linux.UI;

/// <summary>
/// Coalesces bursts of ObservableCollection.CollectionChanged / PropertyChanged
/// notifications (e.g., 1,000 synchronous Add calls during ReplaceStudents) into
/// a single GLib main-loop idle pass so UI rebuilds remain strictly O(N).
/// </summary>
internal sealed class CoalescedUiAction
{
    private readonly Action _action;
    private bool _scheduled;

    public CoalescedUiAction(Action action) =>
        _action = action ?? throw new ArgumentNullException(nameof(action));

    public void Schedule()
    {
        if (_scheduled)
        {
            return;
        }

        _scheduled = true;
        GLib.Functions.IdleAdd(0, () =>
        {
            if (!_scheduled)
            {
                return false;
            }

            _scheduled = false;
            _action();
            return false;
        });
    }

    public void RunNow()
    {
        _scheduled = false;
        _action();
    }
}

internal static class GtkUiHelpers
{
    public static void SetAutomationId(Gtk.Widget widget, string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            widget.SetName(id);
        }
    }

    public static Gtk.Label CreateHeading(string text, string cssClass = "title-3")
    {
        var label = Gtk.Label.New(text);
        label.SetHalign(Gtk.Align.Start);
        label.SetXalign(0f);
        label.SetWrap(true);
        label.AddCssClass(cssClass);
        return label;
    }

    public static Gtk.Label CreateDimLabel(string text, bool caption = false)
    {
        var label = Gtk.Label.New(text);
        label.SetHalign(Gtk.Align.Start);
        label.SetXalign(0f);
        label.SetWrap(true);
        label.AddCssClass("dim-label");
        if (caption)
        {
            label.AddCssClass("caption");
        }

        return label;
    }

    public static Gtk.Label CreateBodyLabel(string text)
    {
        var label = Gtk.Label.New(text);
        label.SetHalign(Gtk.Align.Start);
        label.SetXalign(0f);
        label.SetWrap(true);
        return label;
    }

    public static Gtk.Label CreateBadge(string text, string variantCss = "xq-pill-neutral")
    {
        var label = Gtk.Label.New(text);
        label.SetWrap(false);
        label.SetSingleLineMode(true);
        label.SetHalign(Gtk.Align.End);
        label.SetValign(Gtk.Align.Center);
        label.AddCssClass("xq-pill");
        label.AddCssClass(variantCss);
        return label;
    }

    public static Gtk.Box CreateKpiTile(
        string valueText,
        string labelText,
        string? sublabelText = null,
        string? valueAccentCss = null)
    {
        var card = Gtk.Box.New(Gtk.Orientation.Vertical, 3);
        card.AddCssClass("xq-kpi-card");
        card.SetHexpand(true);

        var title = Gtk.Label.New(labelText);
        title.SetHalign(Gtk.Align.Start);
        title.SetXalign(0f);
        title.SetWrap(false);
        title.SetSingleLineMode(true);
        title.AddCssClass("xq-kpi-label");
        card.Append(title);

        var value = Gtk.Label.New(valueText);
        value.SetHalign(Gtk.Align.Start);
        value.SetXalign(0f);
        value.SetWrap(false);
        value.SetSingleLineMode(true);
        value.SetEllipsize(Pango.EllipsizeMode.End);
        value.AddCssClass("xq-kpi-value");
        if (!string.IsNullOrWhiteSpace(valueAccentCss))
        {
            value.AddCssClass(valueAccentCss);
        }

        card.Append(value);

        if (!string.IsNullOrWhiteSpace(sublabelText))
        {
            var sub = CreateDimLabel(sublabelText, caption: true);
            card.Append(sub);
        }

        return card;
    }

    public static Gtk.Box CreateProgressBarRow(
        string labelText,
        string valueText,
        double fraction,
        string? barExtraCss = null)
    {
        var row = Gtk.Box.New(Gtk.Orientation.Vertical, 4);

        var top = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        var left = CreateBodyLabel(labelText);
        left.SetHexpand(true);
        var right = Gtk.Label.New(valueText);
        right.SetWrap(false);
        right.SetSingleLineMode(true);
        right.SetHalign(Gtk.Align.End);
        right.AddCssClass("dim-label");
        right.AddCssClass("caption");
        top.Append(left);
        top.Append(right);
        row.Append(top);

        var bar = Gtk.ProgressBar.New();
        bar.SetFraction(Math.Clamp(fraction, 0.0, 1.0));
        bar.AddCssClass("xq-bar");
        if (!string.IsNullOrWhiteSpace(barExtraCss))
        {
            bar.AddCssClass(barExtraCss);
        }

        row.Append(bar);
        return row;
    }

    public static Gtk.Box CreateCardBox(int spacing = 10, string cardCss = "xq-card")
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, spacing);
        box.AddCssClass(cardCss);
        return box;
    }

    public static Gtk.ScrolledWindow WrapInScroll(Gtk.Widget child, int? minHeight = null)
    {
        var scroll = Gtk.ScrolledWindow.New();
        scroll.SetVexpand(true);
        scroll.SetPolicy(Gtk.PolicyType.Never, Gtk.PolicyType.Automatic);
        if (minHeight is { } h)
        {
            scroll.SetMinContentHeight(h);
        }

        scroll.SetChild(child);
        return scroll;
    }

    public static void ClearBoxChildren(Gtk.Box box)
    {
        while (box.GetFirstChild() is { } child)
        {
            box.Remove(child);
        }
    }

    public static string GetText(Gtk.TextView textView)
    {
        var buffer = textView.GetBuffer();
        if (buffer is null)
        {
            return string.Empty;
        }

        buffer.GetStartIter(out var start);
        buffer.GetEndIter(out var end);
        return buffer.GetText(start, end, false) ?? string.Empty;
    }

    public static void SetText(Gtk.TextView textView, string text)
    {
        var buffer = textView.GetBuffer();
        if (buffer is null)
        {
            return;
        }

        var safe = text ?? string.Empty;
        buffer.SetText(safe, -1);
    }

    public static Gtk.Widget CreateDateEntryRow(
        string labelText,
        DateOnly? initialDate,
        out Gtk.Entry dateEntry,
        out Gtk.Box quickButtonsBox)
    {
        var container = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        container.Append(CreateHeading(labelText, "heading"));

        dateEntry = Gtk.Entry.New();
        dateEntry.SetPlaceholderText("YYYY-MM-DD（留空表示待安排）");
        if (initialDate is { } d)
        {
            dateEntry.SetText(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        container.Append(dateEntry);

        var entryRef = dateEntry;
        quickButtonsBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
        var today = DateOnly.FromDateTime(DateTime.Now);
        AddQuickDateButton(quickButtonsBox, "今天", today, entryRef);
        AddQuickDateButton(quickButtonsBox, "明天", today.AddDays(1), entryRef);
        AddQuickDateButton(quickButtonsBox, "一周后", today.AddDays(7), entryRef);

        var clearBtn = Gtk.Button.NewWithLabel("待安排");
        clearBtn.AddCssClass("flat");
        clearBtn.OnClicked += (_, _) => entryRef.SetText(string.Empty);
        quickButtonsBox.Append(clearBtn);

        container.Append(quickButtonsBox);
        return container;
    }

    private static void AddQuickDateButton(
        Gtk.Box box,
        string label,
        DateOnly date,
        Gtk.Entry targetEntry)
    {
        var btn = Gtk.Button.NewWithLabel(label);
        btn.AddCssClass("flat");
        btn.OnClicked += (_, _) =>
            targetEntry.SetText(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        box.Append(btn);
    }

    public static bool TryParseOptionalDate(
        string? rawText,
        out DateOnly? parsedDate,
        out string? errorMessage)
    {
        parsedDate = null;
        errorMessage = null;

        var trimmed = rawText?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (DateOnly.TryParseExact(
                trimmed,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            parsedDate = date;
            return true;
        }

        errorMessage = "日期格式请使用 YYYY-MM-DD（例如 2026-11-25），或留空表示待安排。";
        return false;
    }

    public static Task ShowMessageDialogAsync(
        Gtk.Widget parent,
        string heading,
        string body,
        string closeLabel = "知道了")
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = Adw.AlertDialog.New(heading, body);
        dialog.AddResponse("close", closeLabel);
        dialog.SetDefaultResponse("close");
        dialog.SetCloseResponse("close");
        dialog.OnResponse += (_, _) => tcs.TrySetResult();
        dialog.Present(parent);
        return tcs.Task;
    }

    public static Task<bool> ShowConfirmDialogAsync(
        Gtk.Widget parent,
        string heading,
        string body,
        string primaryLabel,
        string cancelLabel = "取消",
        bool destructive = false)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = Adw.AlertDialog.New(heading, body);
        dialog.AddResponse("cancel", cancelLabel);
        dialog.AddResponse("primary", primaryLabel);
        dialog.SetCloseResponse("cancel");
        dialog.SetDefaultResponse("primary");
        dialog.SetResponseAppearance(
            "primary",
            destructive
                ? Adw.ResponseAppearance.Destructive
                : Adw.ResponseAppearance.Suggested);
        dialog.OnResponse += (_, args) =>
            tcs.TrySetResult(string.Equals(args.Response, "primary", StringComparison.Ordinal));
        dialog.Present(parent);
        return tcs.Task;
    }

    public static Gtk.Widget CreateMultilineTextFrame(
        out Gtk.TextView textView,
        int minHeight = 96,
        string? initialText = null)
    {
        textView = Gtk.TextView.New();
        textView.SetWrapMode(Gtk.WrapMode.WordChar);
        textView.SetTopMargin(8);
        textView.SetBottomMargin(8);
        textView.SetLeftMargin(10);
        textView.SetRightMargin(10);
        if (!string.IsNullOrEmpty(initialText))
        {
            SetText(textView, initialText);
        }

        var frame = Gtk.Frame.New(null);
        var scroll = Gtk.ScrolledWindow.New();
        scroll.SetMinContentHeight(minHeight);
        scroll.SetPolicy(Gtk.PolicyType.Never, Gtk.PolicyType.Automatic);
        scroll.SetChild(textView);
        frame.SetChild(scroll);
        return frame;
    }
}
