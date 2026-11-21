using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Security;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class OfflineAccessLeaseTests
{
    private const long Hour = 60L * 60L * 1000L;
    private const long Minute = 60L * 1000L;
    private const long Issued = 1_700_000_000_000L;
    private const long ValidationWall = 1_700_000_100_000L;
    private const long ValidationMonotonic = 10L * Hour;

    private static readonly OfflineAccessScope Scope = new(
        EnvironmentId: "development",
        AppUserId: "app-user-a",
        OrganizationId: "org-a",
        InstallationId: "install-a");

    private static OfflineAccessLease CreateLease(
        long expiresAfterMillis = 72L * Hour,
        OfflineAccessScope? scope = null,
        string bootSessionId = "boot-a") =>
        new(
            Scope: scope ?? Scope,
            IssuedAtServerEpochMillis: Issued,
            ExpiresAtServerEpochMillis: Issued + expiresAfterMillis,
            WallClockAtValidationEpochMillis: ValidationWall,
            MonotonicAtValidationMillis: ValidationMonotonic,
            BootSessionId: bootSessionId);

    private static OfflineClockObservation CreateNow(
        long elapsedAfterValidation,
        long? wallClockEpochMillis = null,
        string bootSessionId = "boot-a") =>
        new(
            WallClockEpochMillis: wallClockEpochMillis ?? (ValidationWall + elapsedAfterValidation),
            MonotonicMillis: ValidationMonotonic + elapsedAfterValidation,
            BootSessionId: bootSessionId);

    [TestMethod]
    public void ValidSameBootLease_IsAllowedUntilMonotonicExpiry()
    {
        var result = OfflineAccessLeaseEvaluator.Evaluate(
            CreateLease(),
            Scope,
            CreateNow(2L * Hour));

        Assert.AreEqual(new OfflineAccessLeaseDecision.Allowed(70L * Hour), result);
    }

    [TestMethod]
    public void ExactExpiry_IsDenied()
    {
        var result = OfflineAccessLeaseEvaluator.Evaluate(
            CreateLease(),
            Scope,
            CreateNow(72L * Hour));

        Assert.AreEqual(
            new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.Expired),
            result);
    }

    [TestMethod]
    public void OverlongOrNonPositiveLease_IsMalformed()
    {
        long[] invalidDurations = [72L * Hour + 1L, 0L, -1L];
        foreach (var duration in invalidDurations)
        {
            var result = OfflineAccessLeaseEvaluator.Evaluate(
                CreateLease(expiresAfterMillis: duration),
                Scope,
                CreateNow(Hour));

            Assert.AreEqual(
                new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MalformedLease),
                result);
        }
    }

    [TestMethod]
    public void BlankBootSessionOrScopeFields_FailClosedAsMalformed()
    {
        var blankNow = CreateNow(Hour, bootSessionId: "   ");
        Assert.AreEqual(
            new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MalformedLease),
            OfflineAccessLeaseEvaluator.Evaluate(CreateLease(), Scope, blankNow));

        var blankOrgScope = Scope with { OrganizationId = "" };
        Assert.AreEqual(
            new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MalformedLease),
            OfflineAccessLeaseEvaluator.Evaluate(
                CreateLease(scope: blankOrgScope),
                blankOrgScope,
                CreateNow(Hour)));
    }

    [TestMethod]
    public void ScopeMismatch_NeverFallsBack()
    {
        var otherOrg = Scope with { OrganizationId = "org-b" };
        var result = OfflineAccessLeaseEvaluator.Evaluate(
            CreateLease(),
            otherOrg,
            CreateNow(Hour));

        Assert.AreEqual(
            new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.ScopeMismatch),
            result);
    }

    [TestMethod]
    public void InstallationScopeMismatch_NeverFallsBack()
    {
        var otherInstall = Scope with { InstallationId = "install-b" };
        var result = OfflineAccessLeaseEvaluator.Evaluate(
            CreateLease(),
            otherInstall,
            CreateNow(Hour));

        Assert.AreEqual(
            new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.ScopeMismatch),
            result);
    }

    [TestMethod]
    public void BootSessionChange_RequiresOnlineRevalidation()
    {
        var result = OfflineAccessLeaseEvaluator.Evaluate(
            CreateLease(),
            Scope,
            CreateNow(Hour, bootSessionId: "boot-b"));

        Assert.AreEqual(
            new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.BootSessionChanged),
            result);
    }

    [TestMethod]
    public void MonotonicClockRollback_FailsClosed()
    {
        var now = new OfflineClockObservation(
            WallClockEpochMillis: ValidationWall + Hour,
            MonotonicMillis: ValidationMonotonic - 1L,
            BootSessionId: "boot-a");

        var result = OfflineAccessLeaseEvaluator.Evaluate(CreateLease(), Scope, now);

        Assert.AreEqual(
            new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MonotonicClockRollback),
            result);
    }

    [TestMethod]
    public void WallClockRollbackBeyondTolerance_FailsClosed()
    {
        var result = OfflineAccessLeaseEvaluator.Evaluate(
            CreateLease(),
            Scope,
            CreateNow(Hour, wallClockEpochMillis: ValidationWall - 6L * Minute));

        Assert.AreEqual(
            new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.WallClockRollback),
            result);
    }

    [TestMethod]
    public void SmallWallClockCorrection_IsTolerated()
    {
        var result = OfflineAccessLeaseEvaluator.Evaluate(
            CreateLease(),
            Scope,
            CreateNow(Hour, wallClockEpochMillis: ValidationWall - 4L * Minute));

        Assert.IsInstanceOfType<OfflineAccessLeaseDecision.Allowed>(result);
    }

    [TestMethod]
    public void ForwardWallClockJump_DoesNotConsumeOrExtendMonotonicBudget()
    {
        var result = OfflineAccessLeaseEvaluator.Evaluate(
            CreateLease(),
            Scope,
            CreateNow(Hour, wallClockEpochMillis: ValidationWall + 30L * 24L * Hour));

        Assert.AreEqual(new OfflineAccessLeaseDecision.Allowed(71L * Hour), result);
    }

    [TestMethod]
    public void Expiry_CannotBeExtendedByWallClockRollbackWithinTolerance()
    {
        var result = OfflineAccessLeaseEvaluator.Evaluate(
            CreateLease(),
            Scope,
            CreateNow(72L * Hour, wallClockEpochMillis: ValidationWall - 4L * Minute));

        Assert.AreEqual(
            new OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.Expired),
            result);
    }
}
