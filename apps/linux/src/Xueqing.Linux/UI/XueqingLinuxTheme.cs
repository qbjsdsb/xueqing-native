using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("linux")]

namespace Xueqing.Linux.UI;

/// <summary>
/// Unified "青墨书卷" (Teal-Ink &amp; Paper) native GTK4 + libadwaita styling.
/// Works gracefully across both Light and Dark system color schemes while
/// enforcing single-line semantic pills, calm card hierarchy, tabular KPI
/// numerals, and restrained educational teal accents.
/// </summary>
internal static class XueqingLinuxTheme
{
    private static bool _installed;

    private const string Stylesheet = """
        @define-color accent_bg_color #0F766E;
        @define-color accent_fg_color #FFFFFF;
        @define-color accent_color #149187;

        .xq-card {
            background-color: @card_bg_color;
            border: 1px solid alpha(@window_fg_color, 0.10);
            border-radius: 12px;
            padding: 14px 16px;
        }

        .xq-hero-card {
            background-color: alpha(#0F766E, 0.10);
            border: 1px solid alpha(#149187, 0.32);
            border-radius: 12px;
            padding: 14px 16px;
        }

        .xq-kpi-card {
            background-color: @card_bg_color;
            border: 1px solid alpha(@window_fg_color, 0.09);
            border-radius: 12px;
            padding: 12px 14px;
        }

        .xq-kpi-value {
            font-size: 21px;
            font-weight: 700;
            font-feature-settings: "tnum";
        }

        .xq-kpi-value-accent {
            color: #149187;
        }

        .xq-kpi-value-warn {
            color: #D97706;
        }

        .xq-kpi-value-overdue {
            color: #D9534F;
        }

        .xq-kpi-label {
            font-size: 12px;
            opacity: 0.72;
        }

        .xq-pill {
            border-radius: 999px;
            padding: 3px 10px;
            font-size: 12px;
            font-weight: 600;
        }

        .xq-pill-confirmed {
            background-color: alpha(#10B981, 0.18);
            color: #10B981;
        }

        .xq-pill-draft {
            background-color: alpha(#F59E0B, 0.18);
            color: #D97706;
        }

        .xq-pill-overdue {
            background-color: alpha(#E05D56, 0.18);
            color: #E05D56;
        }

        .xq-pill-ai {
            background-color: alpha(#149187, 0.18);
            color: #149187;
        }

        .xq-pill-neutral {
            background-color: alpha(@window_fg_color, 0.09);
            color: alpha(@window_fg_color, 0.78);
        }

        .xq-empty-card {
            background-color: @card_bg_color;
            border: 1px dashed alpha(@window_fg_color, 0.18);
            border-radius: 14px;
            padding: 32px 24px;
        }

        progressbar.xq-bar trough {
            min-height: 7px;
            border-radius: 999px;
            background-color: alpha(@window_fg_color, 0.10);
        }

        progressbar.xq-bar progress {
            min-height: 7px;
            border-radius: 999px;
            background-color: #149187;
        }

        progressbar.xq-bar-warn progress {
            background-color: #D97706;
        }

        progressbar.xq-bar-coral progress {
            background-color: #E05D56;
        }

        button.xq-filter-active {
            background-color: alpha(#0F766E, 0.18);
            color: #149187;
            font-weight: 600;
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
