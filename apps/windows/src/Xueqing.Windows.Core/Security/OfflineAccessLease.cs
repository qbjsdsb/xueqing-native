namespace Xueqing.Windows.Core.Security;

public sealed record OfflineAccessScope(
    string EnvironmentId,
    string AppUserId,
    string OrganizationId,
    string InstallationId);

public sealed record OfflineAccessLease(
    OfflineAccessScope Scope,
    long IssuedAtServerEpochMillis,
    long ExpiresAtServerEpochMillis,
    long WallClockAtValidationEpochMillis,
    long MonotonicAtValidationMillis,
    string BootSessionId);

public sealed record OfflineClockObservation(
    long WallClockEpochMillis,
    long MonotonicMillis,
    string BootSessionId);

public sealed record OfflineAccessLeasePolicy
{
    public const long DefaultMaxLeaseDurationMillis = 72L * 60L * 60L * 1000L;
    public const long DefaultWallClockRollbackToleranceMillis = 5L * 60L * 1000L;

    public static OfflineAccessLeasePolicy Default { get; } = new();

    public OfflineAccessLeasePolicy(
        long maxLeaseDurationMillis = DefaultMaxLeaseDurationMillis,
        long wallClockRollbackToleranceMillis = DefaultWallClockRollbackToleranceMillis)
    {
        if (maxLeaseDurationMillis <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxLeaseDurationMillis),
                "Maximum lease duration must be positive.");
        }

        if (wallClockRollbackToleranceMillis < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wallClockRollbackToleranceMillis),
                "Wall-clock rollback tolerance cannot be negative.");
        }

        MaxLeaseDurationMillis = maxLeaseDurationMillis;
        WallClockRollbackToleranceMillis = wallClockRollbackToleranceMillis;
    }

    public long MaxLeaseDurationMillis { get; }

    public long WallClockRollbackToleranceMillis { get; }
}

public enum OfflineAccessLeaseDenial
{
    ScopeMismatch,
    MalformedLease,
    BootSessionChanged,
    MonotonicClockRollback,
    WallClockRollback,
    Expired,
}

public abstract record OfflineAccessLeaseDecision
{
    private OfflineAccessLeaseDecision()
    {
    }

    public sealed record Allowed(long RemainingMillis) : OfflineAccessLeaseDecision;

    public sealed record Denied(OfflineAccessLeaseDenial Reason) : OfflineAccessLeaseDecision;
}

/// <summary>
/// Platform-neutral desktop evaluator for Xueqing Offline Access Lease v1
/// (contracts/security/OFFLINE_ACCESS_LEASE_V1.md), matching Android semantics.
///
/// The lease bounds offline exposure of cached read projections only. It is
/// never a server authorization credential and never permits silent destruction
/// of encrypted unsynced teacher Durable Intent.
/// </summary>
public static class OfflineAccessLeaseEvaluator
{
    public static OfflineAccessLeaseDecision Evaluate(
        OfflineAccessLease lease,
        OfflineAccessScope requestedScope,
        OfflineClockObservation now,
        OfflineAccessLeasePolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(requestedScope);
        ArgumentNullException.ThrowIfNull(now);

        var activePolicy = policy ?? OfflineAccessLeasePolicy.Default;

        if (lease.Scope != requestedScope)
        {
            return new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.ScopeMismatch);
        }

        long duration;
        try
        {
            duration = checked(lease.ExpiresAtServerEpochMillis - lease.IssuedAtServerEpochMillis);
        }
        catch (OverflowException)
        {
            return new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MalformedLease);
        }

        if (string.IsNullOrWhiteSpace(lease.Scope.EnvironmentId) ||
            string.IsNullOrWhiteSpace(lease.Scope.AppUserId) ||
            string.IsNullOrWhiteSpace(lease.Scope.OrganizationId) ||
            string.IsNullOrWhiteSpace(lease.Scope.InstallationId) ||
            string.IsNullOrWhiteSpace(lease.BootSessionId) ||
            string.IsNullOrWhiteSpace(now.BootSessionId) ||
            lease.IssuedAtServerEpochMillis <= 0 ||
            lease.WallClockAtValidationEpochMillis <= 0 ||
            lease.MonotonicAtValidationMillis < 0 ||
            now.WallClockEpochMillis <= 0 ||
            now.MonotonicMillis < 0 ||
            duration <= 0 ||
            duration > activePolicy.MaxLeaseDurationMillis)
        {
            return new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MalformedLease);
        }

        if (!string.Equals(now.BootSessionId, lease.BootSessionId, StringComparison.Ordinal))
        {
            return new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.BootSessionChanged);
        }

        if (now.MonotonicMillis < lease.MonotonicAtValidationMillis)
        {
            return new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MonotonicClockRollback);
        }

        var rollbackFloor = lease.WallClockAtValidationEpochMillis < activePolicy.WallClockRollbackToleranceMillis
            ? 0L
            : lease.WallClockAtValidationEpochMillis - activePolicy.WallClockRollbackToleranceMillis;

        if (now.WallClockEpochMillis < rollbackFloor)
        {
            return new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.WallClockRollback);
        }

        var elapsed = now.MonotonicMillis - lease.MonotonicAtValidationMillis;
        long trustedNow;
        try
        {
            trustedNow = checked(lease.IssuedAtServerEpochMillis + elapsed);
        }
        catch (OverflowException)
        {
            return new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MalformedLease);
        }

        var remaining = lease.ExpiresAtServerEpochMillis - trustedNow;
        if (remaining <= 0)
        {
            return new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.Expired);
        }

        return new OfflineAccessLeaseDecision.Allowed(remaining);
    }
}
