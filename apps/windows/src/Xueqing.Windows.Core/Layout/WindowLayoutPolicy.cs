namespace Xueqing.Windows.Core.Layout;

public enum WindowLayoutMode
{
    Compact,
    Standard,
    Expanded,
}

public static class WindowLayoutPolicy
{
    public const double CompactBreakpoint = 900;
    public const double ExpandedBreakpoint = 1280;

    public static WindowLayoutMode Resolve(double width)
    {
        if (double.IsNaN(width) || width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (width < CompactBreakpoint)
        {
            return WindowLayoutMode.Compact;
        }

        return width < ExpandedBreakpoint
            ? WindowLayoutMode.Standard
            : WindowLayoutMode.Expanded;
    }
}
