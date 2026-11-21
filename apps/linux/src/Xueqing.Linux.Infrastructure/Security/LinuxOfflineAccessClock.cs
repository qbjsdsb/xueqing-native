using System.Runtime.Versioning;
using Xueqing.Windows.Core.Security;

namespace Xueqing.Linux.Infrastructure.Security;

public sealed record LinuxBootClockSample(
    string BootSessionId,
    long MonotonicElapsedMilliseconds,
    DateTimeOffset WallClockUtc);

/// <summary>
/// Provides the Linux kernel boot_id + monotonic tick sample needed to enforce
/// Xueqing's 72-hour same-boot Offline Access Lease with clock-rollback detection.
/// </summary>
[SupportedOSPlatform("linux")]
public static class LinuxOfflineAccessClock
{
    public const string DefaultBootIdPath = "/proc/sys/kernel/random/boot_id";

    public static LinuxBootClockSample Sample(string? bootIdPath = null)
    {
        var path = string.IsNullOrWhiteSpace(bootIdPath)
            ? DefaultBootIdPath
            : bootIdPath;

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Linux kernel boot_id is unavailable at {path}; offline access lease must fail closed.");
        }

        var rawBootId = File.ReadAllText(path).Trim();
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

    /// <summary>
    /// Produces a platform-neutral OfflineClockObservation for OfflineAccessLeaseEvaluator.
    /// If Linux boot_id cannot be read or is malformed, emits a blank BootSessionId so the
    /// evaluator deterministically fails closed (MalformedLease), matching Android behavior.
    /// </summary>
    public static OfflineClockObservation Observe(string? bootIdPath = null)
    {
        try
        {
            var sample = Sample(bootIdPath);
            return new OfflineClockObservation(
                sample.WallClockUtc.ToUnixTimeMilliseconds(),
                sample.MonotonicElapsedMilliseconds,
                sample.BootSessionId);
        }
        catch
        {
            return new OfflineClockObservation(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Math.Max(0L, Environment.TickCount64),
                string.Empty);
        }
    }
}
