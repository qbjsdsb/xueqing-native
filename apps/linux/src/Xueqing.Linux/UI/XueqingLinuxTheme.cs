using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("linux")]

namespace Xueqing.Linux.UI;

/// <summary>
/// Quiet Ink Editorial ("安静手记") native GTK4 + libadwaita styling.
/// Dynamically aligns Light warm alabaster paper (#FAF9F7) and Dark neutral
/// graphite (#141517) with the Android Material 3 palette, paired with a single
/// restrained studio slate-blue accent (#2C5E8A / #88B4DC).
/// </summary>
internal static class XueqingLinuxTheme
{
    private static bool _installed;
    private static Gtk.CssProvider? _paletteProvider;

    private const string LightPaletteCss = """
        @define-color window_bg_color #FAF9F7;
        @define-color view_bg_color #FAF9F7;
        @define-color headerbar_bg_color #FAF9F7;
        @define-color sidebar_bg_color #F3F1EC;
        @define-color secondary_sidebar_bg_color #F3F1EC;
        @define-color dialog_bg_color #FAF9F7;
        @define-color popover_bg_color #FFFFFF;
        @define-color card_bg_color #FFFFFF;
        @define-color window_fg_color #1C1D1F;
        @define-color view_fg_color #1C1D1F;
        @define-color accent_bg_color #2C5E8A;
        @define-color accent_fg_color #FFFFFF;
        @define-color accent_color #2C5E8A;
        """;

    private const string DarkPaletteCss = """
        @define-color window_bg_color #141517;
        @define-color view_bg_color #141517;
        @define-color headerbar_bg_color #141517;
        @define-color sidebar_bg_color #191B1E;
        @define-color secondary_sidebar_bg_color #191B1E;
        @define-color dialog_bg_color #181A1D;
        @define-color popover_bg_color #1F2125;
        @define-color card_bg_color #1C1E22;
        @define-color window_fg_color #EAECEF;
        @define-color view_fg_color #EAECEF;
        @define-color accent_bg_color #2C5E8A;
        @define-color accent_fg_color #FFFFFF;
        @define-color accent_color #88B4DC;
        """;

    private const string SharedStylesheet = """
        .xq-surface {
            background-color: alpha(@window_fg_color, 0.032);
            border-radius: 10px;
            padding: 14px 16px;
        }

        .xq-paper-canvas {
            background-color: alpha(@window_fg_color, 0.035);
            border-right: 1px solid alpha(@window_fg_color, 0.08);
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

        .xq-takeaway {
            font-size: 14px;
            line-height: 1.32;
            opacity: 0.88;
        }

        .xq-section-kicker {
            font-size: 12px;
            font-weight: 600;
            opacity: 0.54;
            letter-spacing: 0.4px;
        }

        .xq-status-overdue {
            color: #C84B4B;
            font-size: 12px;
            font-weight: 600;
        }

        .xq-status-today {
            color: @accent_color;
            font-size: 12px;
            font-weight: 600;
        }

        .xq-status-pending {
            color: #B87A14;
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

        button.xq-tab {
            padding: 6px 10px;
            border-radius: 0;
            font-size: 13px;
            opacity: 0.60;
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

        var styleManager = Adw.StyleManager.GetDefault();

        _paletteProvider = Gtk.CssProvider.New();
        ApplyPalette(styleManager.GetDark());
        Gtk.StyleContext.AddProviderForDisplay(
            display,
            _paletteProvider,
            Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);

        var sharedProvider = Gtk.CssProvider.New();
        sharedProvider.LoadFromString(SharedStylesheet);
        Gtk.StyleContext.AddProviderForDisplay(
            display,
            sharedProvider,
            Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION + 1);

        styleManager.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "dark")
            {
                ApplyPalette(styleManager.GetDark());
            }
        };

        _installed = true;
    }

    private static void ApplyPalette(bool isDark)
    {
        _paletteProvider?.LoadFromString(isDark ? DarkPaletteCss : LightPaletteCss);
    }
}
