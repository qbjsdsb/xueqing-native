using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("linux")]

namespace Xueqing.Linux.NativeSpike;

internal sealed record LinuxBootClockSample(
    string BootSessionId,
    long MonotonicElapsedMilliseconds,
    DateTimeOffset WallClockUtc);

/// <summary>
/// Provides the Linux kernel boot_id + monotonic tick sample needed to enforce
/// Xueqing's 72-hour same-boot Offline Access Lease with clock-rollback detection.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class LinuxOfflineAccessClock
{
    private const string BootIdPath = "/proc/sys/kernel/random/boot_id";

    public static LinuxBootClockSample Sample()
    {
        if (!File.Exists(BootIdPath))
        {
            throw new InvalidOperationException(
                $"Linux kernel boot_id is unavailable at {BootIdPath}; offline access lease must fail closed.");
        }

        var rawBootId = File.ReadAllText(BootIdPath).Trim();
        if (!Guid.TryParse(rawBootId, out var parsed) || parsed == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Linux kernel boot_id is malformed; offline access lease must fail closed.");
        }

        var monotonicMs = Environment.TickCount64;
        if (monotonicMs < 0)
        {
            throw new InvalidOperationException(
                "Monotonic clock reading is invalid; offline access lease must fail closed.");
        }

        return new LinuxBootClockSample(
            parsed.ToString("D"),
            monotonicMs,
            DateTimeOffset.UtcNow);
    }
}
