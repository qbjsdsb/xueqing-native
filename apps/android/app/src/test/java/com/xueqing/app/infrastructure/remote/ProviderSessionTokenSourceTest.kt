package com.xueqing.app.infrastructure.remote

import java.time.Instant
import java.time.temporal.ChronoUnit
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ProviderSessionTokenSourceTest {
    private val now = Instant.parse("2026-09-22T00:00:00Z")

    @Test
    fun `new session is signed out and returns no token`() {
        val clock = MutableClock(now)
        val source = create(clock)

        assertEquals(ProviderSessionStatus.SignedOut, source.snapshot.status)
        assertNull(source.currentAccessToken())
    }

    @Test
    fun `established token expires fail closed into refresh required`() {
        val clock = MutableClock(now)
        val source = create(clock)
        val expiry = now.plus(5, ChronoUnit.MINUTES)
        source.establish("fictional-access-token", expiry)

        assertEquals("fictional-access-token", source.currentAccessToken())
        assertEquals(ProviderSessionStatus.Usable, source.snapshot.status)
        assertEquals(expiry, source.snapshot.accessTokenExpiresAt)

        clock.instant = expiry

        assertNull(source.currentAccessToken())
        assertEquals(ProviderSessionStatus.RefreshRequired, source.snapshot.status)
        assertNull(source.snapshot.accessTokenExpiresAt)
    }

    @Test
    fun `refresh invalid and signed out states never expose token`() {
        val clock = MutableClock(now)
        val source = create(clock)

        source.establish("token-one", now.plusSeconds(300))
        source.markRefreshRequired()
        assertEquals(ProviderSessionStatus.RefreshRequired, source.snapshot.status)
        assertNull(source.currentAccessToken())

        source.establish("token-two", now.plusSeconds(300))
        source.invalidate()
        assertEquals(ProviderSessionStatus.Invalid, source.snapshot.status)
        assertNull(source.currentAccessToken())

        source.establish("token-three", now.plusSeconds(300))
        source.signOut()
        assertEquals(ProviderSessionStatus.SignedOut, source.snapshot.status)
        assertNull(source.currentAccessToken())
    }

    @Test
    fun `malformed or expired session input is rejected`() {
        val clock = MutableClock(now)
        val source = create(clock)

        org.junit.Assert.assertThrows(IllegalArgumentException::class.java) {
            source.establish("token", now)
        }
        org.junit.Assert.assertThrows(IllegalArgumentException::class.java) {
            source.establish("token with spaces", now.plusSeconds(300))
        }
    }

    @Test
    fun `scope remains fixed while new session replaces memory token`() {
        val clock = MutableClock(now)
        val source = create(clock)

        source.establish("first-token", now.plusSeconds(300))
        source.establish("second-token", now.plusSeconds(600))

        assertEquals("prod-sg", source.snapshot.environmentId)
        assertEquals("xueqing-prod-sg", source.snapshot.trustDomainId)
        assertEquals("second-token", source.currentAccessToken())
        assertEquals(now.plusSeconds(600), source.snapshot.accessTokenExpiresAt)
    }

    private fun create(clock: MutableClock) = ProviderSessionTokenSource(
        environmentId = "prod-sg",
        trustDomainId = "xueqing-prod-sg",
        now = { clock.instant },
    )

    private data class MutableClock(var instant: Instant)
}
