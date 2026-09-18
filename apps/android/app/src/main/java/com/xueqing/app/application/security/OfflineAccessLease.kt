package com.xueqing.app.application.security

data class OfflineAccessScope(
    val environmentId: String,
    val appUserId: String,
    val organizationId: String,
    val installationId: String,
)

data class OfflineAccessLease(
    val scope: OfflineAccessScope,
    val issuedAtServerEpochMillis: Long,
    val expiresAtServerEpochMillis: Long,
    val wallClockAtValidationEpochMillis: Long,
    val monotonicAtValidationMillis: Long,
    val bootSessionId: String,
)

data class OfflineClockObservation(
    val wallClockEpochMillis: Long,
    val monotonicMillis: Long,
    val bootSessionId: String,
)

data class OfflineAccessLeasePolicy(
    val maxLeaseDurationMillis: Long = DEFAULT_MAX_LEASE_DURATION_MILLIS,
    val wallClockRollbackToleranceMillis: Long = DEFAULT_WALL_CLOCK_ROLLBACK_TOLERANCE_MILLIS,
) {
    init {
        require(maxLeaseDurationMillis > 0)
        require(wallClockRollbackToleranceMillis >= 0)
    }

    companion object {
        const val DEFAULT_MAX_LEASE_DURATION_MILLIS: Long = 72L * 60L * 60L * 1000L
        const val DEFAULT_WALL_CLOCK_ROLLBACK_TOLERANCE_MILLIS: Long = 5L * 60L * 1000L
    }
}

enum class OfflineAccessLeaseDenial {
    ScopeMismatch,
    MalformedLease,
    BootSessionChanged,
    MonotonicClockRollback,
    WallClockRollback,
    Expired,
}

sealed interface OfflineAccessLeaseDecision {
    data class Allowed(val remainingMillis: Long) : OfflineAccessLeaseDecision
    data class Denied(val reason: OfflineAccessLeaseDenial) : OfflineAccessLeaseDecision
}

/**
 * Evaluates whether an already-cached projection may be read while offline.
 *
 * This is deliberately not an authorization credential. Authoritative server
 * commands still re-run live authorization and the Teaching Fact Gate.
 *
 * V1 trusts elapsed time only within the same validated boot session. After a
 * reboot/boot-session change the client must revalidate online instead of
 * reconstructing elapsed authorization time from an adjustable wall clock.
 */
fun evaluateOfflineAccessLease(
    lease: OfflineAccessLease,
    requestedScope: OfflineAccessScope,
    now: OfflineClockObservation,
    policy: OfflineAccessLeasePolicy = OfflineAccessLeasePolicy(),
): OfflineAccessLeaseDecision {
    if (lease.scope != requestedScope) {
        return OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.ScopeMismatch)
    }

    val duration = runCatching {
        Math.subtractExact(lease.expiresAtServerEpochMillis, lease.issuedAtServerEpochMillis)
    }.getOrElse {
        return OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MalformedLease)
    }

    if (
        lease.scope.environmentId.isBlank() ||
        lease.scope.appUserId.isBlank() ||
        lease.scope.organizationId.isBlank() ||
        lease.scope.installationId.isBlank() ||
        lease.bootSessionId.isBlank() ||
        now.bootSessionId.isBlank() ||
        lease.issuedAtServerEpochMillis <= 0 ||
        lease.wallClockAtValidationEpochMillis <= 0 ||
        lease.monotonicAtValidationMillis < 0 ||
        now.wallClockEpochMillis <= 0 ||
        now.monotonicMillis < 0 ||
        duration <= 0 ||
        duration > policy.maxLeaseDurationMillis
    ) {
        return OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MalformedLease)
    }

    if (now.bootSessionId != lease.bootSessionId) {
        return OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.BootSessionChanged)
    }

    if (now.monotonicMillis < lease.monotonicAtValidationMillis) {
        return OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MonotonicClockRollback)
    }

    val rollbackFloor = if (
        lease.wallClockAtValidationEpochMillis <
        policy.wallClockRollbackToleranceMillis
    ) {
        0L
    } else {
        lease.wallClockAtValidationEpochMillis - policy.wallClockRollbackToleranceMillis
    }
    if (now.wallClockEpochMillis < rollbackFloor) {
        return OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.WallClockRollback)
    }

    val elapsed = now.monotonicMillis - lease.monotonicAtValidationMillis
    val trustedNow = runCatching {
        Math.addExact(lease.issuedAtServerEpochMillis, elapsed)
    }.getOrElse {
        return OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MalformedLease)
    }

    val remaining = lease.expiresAtServerEpochMillis - trustedNow
    if (remaining <= 0) {
        return OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.Expired)
    }

    return OfflineAccessLeaseDecision.Allowed(remainingMillis = remaining)
}
