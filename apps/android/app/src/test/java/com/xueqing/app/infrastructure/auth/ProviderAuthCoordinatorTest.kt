package com.xueqing.app.infrastructure.auth

import com.xueqing.app.infrastructure.remote.ProviderSessionStatus
import com.xueqing.app.infrastructure.remote.ProviderSessionTokenSource
import java.time.Instant
import kotlin.coroutines.startCoroutine
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Test

class ProviderAuthCoordinatorTest {
    private val now = Instant.parse("2026-09-22T00:00:00Z")

    @Test
    fun `refresh persists rotated token before access token becomes usable`() {
        val source = source()
        val vault = FakeVault("old-refresh")
        val transport = FakeTransport(
            refreshResult = success("new-access", "new-refresh"),
        )
        val coordinator = coordinator(source, vault, transport)

        val outcome = runSuspend { coordinator.refresh() }

        assertEquals(ProviderRefreshOutcome.Usable, outcome)
        assertEquals("new-refresh", vault.value)
        assertEquals("new-access", source.currentAccessToken())
        assertEquals(listOf("load", "store:new-refresh"), vault.events)
    }

    @Test
    fun `vault failure during rotation keeps access token unusable`() {
        val source = source()
        val vault = FakeVault("old-refresh", failStore = true)
        val coordinator = coordinator(
            source,
            vault,
            FakeTransport(refreshResult = success("new-access", "new-refresh")),
        )

        assertThrows(RefreshTokenVaultUnavailableException::class.java) {
            runSuspend { coordinator.refresh() }
        }

        assertEquals(ProviderSessionStatus.RefreshRequired, source.snapshot.status)
        assertNull(source.currentAccessToken())
        assertEquals("old-refresh", vault.value)
    }

    @Test
    fun `result unknown preserves previous refresh token`() {
        val source = source()
        val vault = FakeVault("old-refresh")
        val coordinator = coordinator(
            source,
            vault,
            FakeTransport(
                refreshResult = ProviderRefreshResult(
                    ProviderRefreshDisposition.ResultUnknown,
                ),
            ),
        )

        val outcome = runSuspend { coordinator.refresh() }

        assertEquals(ProviderRefreshOutcome.RefreshRequired, outcome)
        assertEquals("old-refresh", vault.value)
        assertEquals(ProviderSessionStatus.RefreshRequired, source.snapshot.status)
    }

    @Test
    fun `rejected refresh clears vault and invalidates session`() {
        val source = source()
        val vault = FakeVault("old-refresh")
        val coordinator = coordinator(
            source,
            vault,
            FakeTransport(
                refreshResult = ProviderRefreshResult(
                    ProviderRefreshDisposition.Rejected,
                ),
            ),
        )

        val outcome = runSuspend { coordinator.refresh() }

        assertEquals(ProviderRefreshOutcome.Invalid, outcome)
        assertNull(vault.value)
        assertEquals(ProviderSessionStatus.Invalid, source.snapshot.status)
    }

    @Test
    fun `remote signout failure still clears local state without claiming revocation`() {
        val source = source()
        source.establish("access", now.plusSeconds(600))
        val vault = FakeVault("refresh")
        val coordinator = coordinator(
            source,
            vault,
            FakeTransport(failSignOut = true),
        )

        val outcome = runSuspend { coordinator.signOut() }

        assertEquals(
            ProviderSignOutOutcome.LocalOnlyRemoteUnconfirmed,
            outcome,
        )
        assertNull(vault.value)
        assertEquals(ProviderSessionStatus.SignedOut, source.snapshot.status)
        assertNull(source.currentAccessToken())
    }

    @Test
    fun `signin vault failure never exposes new access token`() {
        val source = source()
        val vault = FakeVault(null, failStore = true)
        val coordinator = coordinator(
            source,
            vault,
            FakeTransport(
                signInResult = ProviderAuthTokens(
                    "signed-in-access",
                    "signed-in-refresh",
                    now.plusSeconds(600),
                ),
            ),
        )

        assertThrows(RefreshTokenVaultUnavailableException::class.java) {
            runSuspend {
                coordinator.signInWithPassword(
                    "fictional@example.invalid",
                    "fictional-password",
                )
            }
        }

        assertEquals(ProviderSessionStatus.SignedOut, source.snapshot.status)
        assertNull(source.currentAccessToken())
    }


    @Test
    fun `invalid session may reauthenticate only after revoked vault is cleared`() {
        val source = source()
        source.invalidate()
        val vault = FakeVault(null)
        val coordinator = coordinator(
            source,
            vault,
            FakeTransport(
                signInResult = ProviderAuthTokens(
                    "reauth-access",
                    "reauth-refresh",
                    now.plusSeconds(600),
                ),
            ),
        )

        runSuspend {
            coordinator.signInWithPassword(
                "fictional@example.invalid",
                "fictional-password",
            )
        }

        assertEquals(ProviderSessionStatus.Usable, source.snapshot.status)
        assertEquals("reauth-refresh", vault.value)
        assertEquals("reauth-access", source.currentAccessToken())
    }

    private fun source() = ProviderSessionTokenSource(
        environmentId = "prod-sg",
        trustDomainId = "xueqing-prod-sg",
        now = { now },
    )

    private fun coordinator(
        source: ProviderSessionTokenSource,
        vault: FakeVault,
        transport: FakeTransport,
    ) = ProviderAuthCoordinator(
        tokenSource = source,
        refreshTokenVault = vault,
        transport = transport,
        now = { now },
    )

    private fun success(
        accessToken: String,
        refreshToken: String,
    ) = ProviderRefreshResult(
        ProviderRefreshDisposition.Success,
        ProviderAuthTokens(
            accessToken,
            refreshToken,
            now.plusSeconds(600),
        ),
    )

    private class FakeVault(
        initial: String?,
        private val failStore: Boolean = false,
    ) : RefreshTokenVault {
        var value: String? = initial
            private set

        val events = mutableListOf<String>()

        override suspend fun load(): String? {
            events += "load"
            return value
        }

        override suspend fun store(refreshToken: String) {
            events += "store:$refreshToken"
            if (failStore) {
                throw RefreshTokenVaultUnavailableException(
                    "fictional vault failure",
                )
            }
            value = refreshToken
        }

        override suspend fun clear() {
            events += "clear"
            value = null
        }
    }

    private class FakeTransport(
        private val signInResult: ProviderAuthTokens? = null,
        private val refreshResult: ProviderRefreshResult =
            ProviderRefreshResult(ProviderRefreshDisposition.RetryableFailure),
        private val failSignOut: Boolean = false,
    ) : ProviderAuthTransport {
        override suspend fun signInWithPassword(
            email: String,
            password: String,
        ): ProviderAuthTokens =
            signInResult ?: error("No fictional sign-in result configured.")

        override suspend fun refresh(refreshToken: String): ProviderRefreshResult =
            refreshResult

        override suspend fun signOutLocal(accessToken: String) {
            if (failSignOut) {
                error("fictional remote sign-out failure")
            }
        }
    }

    private fun <T> runSuspend(block: suspend () -> T): T {
        var result: Result<T>? = null
        block.startCoroutine(
            object : kotlin.coroutines.Continuation<T> {
                override val context = kotlin.coroutines.EmptyCoroutineContext

                override fun resumeWith(value: Result<T>) {
                    result = value
                }
            },
        )
        return checkNotNull(result).getOrThrow()
    }
}
