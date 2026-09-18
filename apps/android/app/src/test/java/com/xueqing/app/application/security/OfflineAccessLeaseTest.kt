package com.xueqing.app.application.security

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class OfflineAccessLeaseTest {
    private val hour = 60L * 60L * 1000L
    private val minute = 60L * 1000L
    private val issued = 1_700_000_000_000L
    private val validationWall = 1_700_000_100_000L
    private val validationMonotonic = 10L * hour
    private val scope = OfflineAccessScope(
        environmentId = "development",
        appUserId = "app-user-a",
        organizationId = "org-a",
        installationId = "install-a",
    )

    private fun lease(
        expiresAfterMillis: Long = 72L * hour,
        scope: OfflineAccessScope = this.scope,
        bootSessionId: String = "boot-a",
    ) = OfflineAccessLease(
        scope = scope,
        issuedAtServerEpochMillis = issued,
        expiresAtServerEpochMillis = issued + expiresAfterMillis,
        wallClockAtValidationEpochMillis = validationWall,
        monotonicAtValidationMillis = validationMonotonic,
        bootSessionId = bootSessionId,
    )

    private fun now(
        elapsedAfterValidation: Long,
        wallClockEpochMillis: Long = validationWall + elapsedAfterValidation,
        bootSessionId: String = "boot-a",
    ) = OfflineClockObservation(
        wallClockEpochMillis = wallClockEpochMillis,
        monotonicMillis = validationMonotonic + elapsedAfterValidation,
        bootSessionId = bootSessionId,
    )

    @Test
    fun valid_same_boot_lease_is_allowed_until_monotonic_expiry() {
        val result = evaluateOfflineAccessLease(
            lease = lease(),
            requestedScope = scope,
            now = now(2L * hour),
        )

        assertEquals(
            OfflineAccessLeaseDecision.Allowed(70L * hour),
            result,
        )
    }

    @Test
    fun exact_expiry_is_denied() {
        val result = evaluateOfflineAccessLease(
            lease = lease(),
            requestedScope = scope,
            now = now(72L * hour),
        )

        assertEquals(
            OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.Expired),
            result,
        )
    }

    @Test
    fun overlong_or_non_positive_lease_is_malformed() {
        listOf(
            lease(expiresAfterMillis = 72L * hour + 1L),
            lease(expiresAfterMillis = 0L),
            lease(expiresAfterMillis = -1L),
        ).forEach { candidate ->
            assertEquals(
                OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MalformedLease),
                evaluateOfflineAccessLease(candidate, scope, now(hour)),
            )
        }
    }

    @Test
    fun scope_mismatch_never_falls_back() {
        val otherOrganization = scope.copy(organizationId = "org-b")

        assertEquals(
            OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.ScopeMismatch),
            evaluateOfflineAccessLease(lease(), otherOrganization, now(hour)),
        )
    }

    @Test
    fun installation_scope_mismatch_never_falls_back() {
        val otherInstallation = scope.copy(installationId = "install-b")

        assertEquals(
            OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.ScopeMismatch),
            evaluateOfflineAccessLease(lease(), otherInstallation, now(hour)),
        )
    }

    @Test
    fun boot_session_change_requires_online_revalidation() {
        val result = evaluateOfflineAccessLease(
            lease = lease(),
            requestedScope = scope,
            now = now(hour, bootSessionId = "boot-b"),
        )

        assertEquals(
            OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.BootSessionChanged),
            result,
        )
    }

    @Test
    fun monotonic_clock_rollback_fails_closed() {
        val result = evaluateOfflineAccessLease(
            lease = lease(),
            requestedScope = scope,
            now = OfflineClockObservation(
                wallClockEpochMillis = validationWall + hour,
                monotonicMillis = validationMonotonic - 1L,
                bootSessionId = "boot-a",
            ),
        )

        assertEquals(
            OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.MonotonicClockRollback),
            result,
        )
    }

    @Test
    fun wall_clock_rollback_beyond_tolerance_fails_closed() {
        val result = evaluateOfflineAccessLease(
            lease = lease(),
            requestedScope = scope,
            now = now(
                elapsedAfterValidation = hour,
                wallClockEpochMillis = validationWall - 6L * minute,
            ),
        )

        assertEquals(
            OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.WallClockRollback),
            result,
        )
    }

    @Test
    fun small_wall_clock_correction_is_tolerated() {
        val result = evaluateOfflineAccessLease(
            lease = lease(),
            requestedScope = scope,
            now = now(
                elapsedAfterValidation = hour,
                wallClockEpochMillis = validationWall - 4L * minute,
            ),
        )

        assertTrue(result is OfflineAccessLeaseDecision.Allowed)
    }

    @Test
    fun forward_wall_clock_jump_does_not_consume_or_extend_monotonic_budget() {
        val result = evaluateOfflineAccessLease(
            lease = lease(),
            requestedScope = scope,
            now = now(
                elapsedAfterValidation = hour,
                wallClockEpochMillis = validationWall + 30L * 24L * hour,
            ),
        )

        assertEquals(
            OfflineAccessLeaseDecision.Allowed(71L * hour),
            result,
        )
    }

    @Test
    fun expiry_cannot_be_extended_by_wall_clock_rollback_within_tolerance() {
        val result = evaluateOfflineAccessLease(
            lease = lease(),
            requestedScope = scope,
            now = now(
                elapsedAfterValidation = 72L * hour,
                wallClockEpochMillis = validationWall - 4L * minute,
            ),
        )

        assertEquals(
            OfflineAccessLeaseDecision.Denied(OfflineAccessLeaseDenial.Expired),
            result,
        )
    }
}
