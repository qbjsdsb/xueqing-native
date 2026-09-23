package com.xueqing.app.application.compatibility

import java.time.Instant

enum class ClientCompatibilityState {
    Supported,
    UpdateRecommended,
    UpdateRequired,
    SecurityBlocked,
    ;

    val allowsConsequentialWrite: Boolean
        get() = this == Supported || this == UpdateRecommended
}

data class ClientCompatibilityDecision(
    val generatedAtServer: Instant,
    val policyRevision: String,
    val platform: String,
    val appVersion: String,
    val clientContractVersion: Int,
    val state: ClientCompatibilityState,
    val reasonCode: String,
    val minimumSupportedAppVersion: String,
    val recommendedAppVersion: String,
    val minimumSupportedContractVersion: Int,
    val serverContractVersion: Int,
    val updateUri: String?,
)

data class ClientCompatibilityRequest(
    val platform: String,
    val appVersion: String,
    val contractVersion: Int,
) {
    init {
        require(platform == "android" || platform == "windows")
        require(appVersion.isNotBlank() && appVersion.length <= 64)
        require(appVersion == appVersion.trim())
        require(contractVersion >= 1)
    }
}

enum class ClientCompatibilityUnknownReason {
    Timeout,
    NetworkFailure,
    ServerFailure,
    PolicyUnavailable,
}

sealed interface ClientCompatibilityResult {
    data class Loaded(
        val decision: ClientCompatibilityDecision,
    ) : ClientCompatibilityResult

    data object AuthenticationRequired : ClientCompatibilityResult

    data class Unknown(
        val reason: ClientCompatibilityUnknownReason,
    ) : ClientCompatibilityResult

    data class ProtocolFailure(
        val code: String,
    ) : ClientCompatibilityResult
}

fun interface ClientCompatibilityRemote {
    fun check(request: ClientCompatibilityRequest): ClientCompatibilityResult
}
