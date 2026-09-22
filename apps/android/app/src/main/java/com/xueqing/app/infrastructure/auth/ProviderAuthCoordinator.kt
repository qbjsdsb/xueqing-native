package com.xueqing.app.infrastructure.auth

import com.xueqing.app.infrastructure.remote.ProviderSessionStatus
import com.xueqing.app.infrastructure.remote.ProviderSessionTokenSource
import java.time.Instant
import java.util.concurrent.CancellationException
import java.util.concurrent.atomic.AtomicBoolean

data class ProviderAuthTokens(
    val accessToken: String,
    val refreshToken: String,
    val accessTokenExpiresAt: Instant,
)

enum class ProviderRefreshDisposition {
    Success,
    Rejected,
    RetryableFailure,
    ResultUnknown,
}

data class ProviderRefreshResult(
    val disposition: ProviderRefreshDisposition,
    val tokens: ProviderAuthTokens? = null,
)

interface ProviderAuthTransport {
    suspend fun signInWithPassword(email: String, password: String): ProviderAuthTokens
    suspend fun refresh(refreshToken: String): ProviderRefreshResult
    suspend fun signOutLocal(accessToken: String)
}

enum class ProviderRefreshOutcome {
    Usable,
    SignedOut,
    RefreshRequired,
    Invalid,
}

enum class ProviderSignOutOutcome {
    LocalAndRemoteConfirmed,
    LocalOnlyRemoteUnconfirmed,
}

/**
 * Owns provider session mutation while keeping provider identity out of Domain.
 *
 * A rotated refresh token is committed to secure storage before its access token
 * can become usable. Concurrent auth mutations are rejected so one-time refresh
 * token rotation cannot race inside the process.
 */
class ProviderAuthCoordinator(
    private val tokenSource: ProviderSessionTokenSource,
    private val refreshTokenVault: RefreshTokenVault,
    private val transport: ProviderAuthTransport,
    private val now: () -> Instant = Instant::now,
) {
    private val mutating = AtomicBoolean(false)

    suspend fun signInWithPassword(email: String, password: String) = serialized {
        require(email.isNotBlank()) { "Email must not be blank." }
        require(password.isNotBlank()) { "Password must not be blank." }
        val status = tokenSource.snapshot.status
        check(
            status == ProviderSessionStatus.SignedOut ||
                status == ProviderSessionStatus.Invalid
        ) {
            "Explicit sign-in requires a signed-out/invalid session. Account switching must close the previous local scope first."
        }
        if (status == ProviderSessionStatus.Invalid) {
            check(refreshTokenVault.load() == null) {
                "Invalid session cannot re-authenticate while a refresh credential remains persisted."
            }
        }

        val tokens = transport.signInWithPassword(email, password)
        validateTokens(tokens)
        refreshTokenVault.store(tokens.refreshToken)
        tokenSource.establish(tokens.accessToken, tokens.accessTokenExpiresAt)
    }

    suspend fun restore(): ProviderRefreshOutcome = serialized {
        if (tokenSource.snapshot.status == ProviderSessionStatus.Usable) {
            ProviderRefreshOutcome.Usable
        } else {
            refreshLocked()
        }
    }

    suspend fun refresh(): ProviderRefreshOutcome = serialized {
        refreshLocked()
    }

    suspend fun signOut(): ProviderSignOutOutcome = serialized {
        val accessToken = tokenSource.currentAccessToken()
        var remoteConfirmed = accessToken == null

        if (accessToken != null) {
            remoteConfirmed = try {
                transport.signOutLocal(accessToken)
                true
            } catch (error: CancellationException) {
                throw error
            } catch (_: Exception) {
                false
            }
        }

        try {
            refreshTokenVault.clear()
            tokenSource.signOut()
        } catch (error: Exception) {
            tokenSource.invalidate()
            throw error
        }

        if (remoteConfirmed) {
            ProviderSignOutOutcome.LocalAndRemoteConfirmed
        } else {
            ProviderSignOutOutcome.LocalOnlyRemoteUnconfirmed
        }
    }

    private suspend fun refreshLocked(): ProviderRefreshOutcome {
        val persistedRefreshToken = refreshTokenVault.load()
        if (persistedRefreshToken == null) {
            tokenSource.signOut()
            return ProviderRefreshOutcome.SignedOut
        }

        tokenSource.markRefreshRequired()

        val result = try {
            transport.refresh(persistedRefreshToken)
        } catch (error: CancellationException) {
            throw error
        } catch (_: Exception) {
            return ProviderRefreshOutcome.RefreshRequired
        }

        return when (result.disposition) {
            ProviderRefreshDisposition.Success -> {
                val tokens = checkNotNull(result.tokens) {
                    "Successful refresh result must include rotated tokens."
                }
                validateTokens(tokens)
                refreshTokenVault.store(tokens.refreshToken)
                tokenSource.establish(tokens.accessToken, tokens.accessTokenExpiresAt)
                ProviderRefreshOutcome.Usable
            }

            ProviderRefreshDisposition.Rejected -> {
                refreshTokenVault.clear()
                tokenSource.invalidate()
                ProviderRefreshOutcome.Invalid
            }

            ProviderRefreshDisposition.RetryableFailure,
            ProviderRefreshDisposition.ResultUnknown,
            -> ProviderRefreshOutcome.RefreshRequired
        }
    }

    private fun validateTokens(tokens: ProviderAuthTokens) {
        require(tokens.accessToken.isNotBlank()) { "Access token must not be blank." }
        require(tokens.refreshToken.isNotBlank()) { "Refresh token must not be blank." }
        require(tokens.accessToken.none(Char::isWhitespace)) {
            "Access token must not contain whitespace."
        }
        require(tokens.refreshToken.none(Char::isWhitespace)) {
            "Refresh token must not contain whitespace."
        }
        require(tokens.accessTokenExpiresAt > now()) {
            "Access token expiry must be in the future."
        }
    }

    private suspend fun <T> serialized(block: suspend () -> T): T {
        check(mutating.compareAndSet(false, true)) {
            "Another authentication mutation is already in progress."
        }
        return try {
            block()
        } finally {
            mutating.set(false)
        }
    }
}
