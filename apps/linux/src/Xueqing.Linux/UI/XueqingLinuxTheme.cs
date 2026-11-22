using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("linux")]

namespace Xueqing.Linux.UI;

/// <summary>
/// Quiet Ink Editorial ("素墨手记") native GTK4 + libadwaita styling.
/// Favors typography, generous whitespace, hairline dividers, and a single
/// restrained slate-blue ink accent over heavy dashboard cards or bright pills.
/// </summary>
internal static class XueqingLinuxTheme
{
    private static bool _installed;

    private const string Stylesheet = """
        @define-color accent_bg_color #2C5E8A;
        @define-color accent_fg_color #FFFFFF;
        @define-color accent_color #4A82B5;

        .xq-surface {
            background-color: alpha(@window_fg_color, 0.032);
            border-radius: 10px;
            padding: 14px 16px;
        }

        .xq-callout {
            background-color: alpha(@window_fg_color, 0.032);
            border-left: 3px solid @accent_color;
            border-radius: 2px 10px 10px 2px;
            padding: 12px 16px;
        }

        .xq-list-row {
            padding: 13px 2px;
            border-bottom: 1px solid alpha(@window_fg_color, 0.075);
        }

        .xq-metric-strip {
            padding: 10px 0 14px 0;
            border-top: 1px solid alpha(@window_fg_color, 0.075);
            border-bottom: 1px solid alpha(@window_fg_color, 0.075);
        }

        .xq-metric-value {
            font-size: 21px;
            font-weight: 600;
            font-feature-settings: "tnum";
        }

        .xq-metric-value-warn {
            color: #C28422;
        }

        .xq-metric-label {
            font-size: 12px;
            opacity: 0.56;
        }

        .xq-section-kicker {
            font-size: 12px;
            font-weight: 600;
            opacity: 0.54;
            letter-spacing: 0.4px;
        }

        .xq-status-overdue {
            color: #CE5353;
            font-size: 12px;
            font-weight: 600;
        }

        .xq-status-today {
            color: @accent_color;
            font-size: 12px;
            font-weight: 600;
        }

        .xq-status-pending {
            color: #C28422;
            font-size: 12px;
            font-weight: 600;
        }

        .xq-status-muted {
            font-size: 12px;
            opacity: 0.54;
        }

        .xq-tag {
            background-color: alpha(@window_fg_color, 0.055);
            border-radius: 6px;
            padding: 3px 9px;
            font-size: 12px;
        }

        .xq-empty-note {
            padding: 36px 12px;
            opacity: 0.56;
        }

        progressbar.xq-thin-bar trough {
            min-height: 3px;
            border-radius: 2px;
            background-color: alpha(@window_fg_color, 0.09);
        }

        progressbar.xq-thin-bar progress {
            min-height: 3px;
            border-radius: 2px;
            background-color: alpha(@window_fg_color, 0.44);
        }

        progressbar.xq-thin-bar-accent progress {
            background-color: @accent_color;
        }

        progressbar.xq-thin-bar-warn progress {
            background-color: #C85B5B;
        }

        button.xq-tab {
            padding: 6px 10px;
            border-radius: 0;
            font-size: 13px;
            opacity: 0.62;
        }

        button.xq-tab:hover {
            opacity: 0.90;
        }

        button.xq-tab-active {
            opacity: 1.0;
            color: @accent_color;
            font-weight: 600;
            box-shadow: inset 0 -2px 0 @accent_color;
        }

        .xq-score-num {
            font-feature-settings: "tnum";
            font-weight: 600;
        }

        .xq-date-col {
            font-feature-settings: "tnum";
            font-size: 12px;
            opacity: 0.56;
        }
        """;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        var display = Gdk.Display.GetDefault();
        if (display is null)
        {
            return;
        }

        var provider = Gtk.CssProvider.New();
        provider.LoadFromString(Stylesheet);
        Gtk.StyleContext.AddProviderForDisplay(
            display,
            provider,
            Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
        _installed = true;
    }
}
