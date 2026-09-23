package com.xueqing.app.infrastructure.remote

import java.time.Instant

enum class ProviderSessionStatus {
    SignedOut,
    Usable,
    RefreshRequired,
    Invalid,
}

data class ProviderSessionSnapshot(
    val environmentId: String,
    val trustDomainId: String,
    val status: ProviderSessionStatus,
    val accessTokenExpiresAt: Instant?,
)

/**
 * Process-local access-token boundary used by provider adapters and workers.
 *
 * Provider login/refresh-token persistence belongs to the Auth coordinator.
 * This source never persists tokens and never treats provider subject as
 * Xueqing business identity.
 */
class ProviderSessionTokenSource(
    environmentId: String,
    trustDomainId: String,
    private val now: () -> Instant = Instant::now,
) : SessionTokenSource {
    val environmentId: String = requireIdentifier(environmentId, "environmentId")
    val trustDomainId: String = requireIdentifier(trustDomainId, "trustDomainId")

    private var accessToken: String? = null
    private var accessTokenExpiresAt: Instant? = null
    private var status: ProviderSessionStatus = ProviderSessionStatus.SignedOut

    val snapshot: ProviderSessionSnapshot
        @Synchronized get() {
            expireIfNeeded()
            return ProviderSessionSnapshot(
                environmentId = environmentId,
                trustDomainId = trustDomainId,
                status = status,
                accessTokenExpiresAt = accessTokenExpiresAt,
            )
        }

    @Synchronized
    fun establish(accessToken: String, expiresAt: Instant) {
        require(accessToken.isNotBlank()) { "Access token must not be blank." }
        require(accessToken.none(Char::isWhitespace)) {
            "Access token must not contain whitespace."
        }
        require(expiresAt > now()) { "Access token expiry must be in the future." }

        this.accessToken = accessToken
        accessTokenExpiresAt = expiresAt
        status = ProviderSessionStatus.Usable
    }

    @Synchronized
    fun markRefreshRequired() {
        clearToken()
        status = ProviderSessionStatus.RefreshRequired
    }

    @Synchronized
    fun invalidate() {
        clearToken()
        status = ProviderSessionStatus.Invalid
    }

    @Synchronized
    fun signOut() {
        clearToken()
        status = ProviderSessionStatus.SignedOut
    }

    @Synchronized
    override fun currentAccessToken(): String? {
        expireIfNeeded()
        return accessToken.takeIf { status == ProviderSessionStatus.Usable }
    }

    private fun expireIfNeeded() {
        if (status != ProviderSessionStatus.Usable) return
        val expiry = accessTokenExpiresAt ?: return
        if (expiry <= now()) {
            clearToken()
            status = ProviderSessionStatus.RefreshRequired
        }
    }

    private fun clearToken() {
        accessToken = null
        accessTokenExpiresAt = null
    }

    private companion object {
        fun requireIdentifier(value: String, name: String): String {
            require(value.isNotBlank()) { "$name must not be blank." }
            require(value == value.trim()) {
                "$name must not contain surrounding whitespace."
            }
            return value
        }
    }
}
