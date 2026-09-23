package com.xueqing.app.application.support

import java.time.Instant

enum class DiagnosticSessionState(val wireValue: String) {
    SignedOut("signed_out"),
    Authenticated("authenticated"),
    RefreshRequired("refresh_required"),
    RevokedOrInvalid("revoked_or_invalid"),
    ConfigurationUnavailable("configuration_unavailable"),
}

enum class DiagnosticSyncCategory(val wireValue: String) {
    Never("never"),
    Recent("recent"),
    Stale("stale"),
    Unknown("unknown"),
}

enum class DiagnosticCompatibilityState(val wireValue: String) {
    Supported("supported"),
    UpdateRecommended("update_recommended"),
    UpdateRequired("update_required"),
    SecurityBlocked("security_blocked"),
    Unknown("unknown"),
}

data class DiagnosticDeployment(
    val profileId: String,
    val environmentId: String,
    val trustDomainId: String,
    val providerId: String,
)

data class DiagnosticCompatibility(
    val state: DiagnosticCompatibilityState,
    val reasonCode: String?,
    val policyRevision: String?,
)

data class DiagnosticQueueCounts(
    val pendingIntents: Int,
    val outboxItems: Int,
    val attachmentStaging: Int,
)

data class DiagnosticsSnapshot(
    val generatedAt: Instant,
    val osVersion: String,
    val architecture: String,
    val packageIdentity: String,
    val appVersion: String,
    val sourceCommit: String,
    val clientContractVersion: Int,
    val localSchemaVersion: Int,
    val deployment: DiagnosticDeployment,
    val sessionState: DiagnosticSessionState,
    val lastSyncCategory: DiagnosticSyncCategory,
    val compatibility: DiagnosticCompatibility,
    val queues: DiagnosticQueueCounts,
    val recentErrorCodes: List<String>,
) {
    init {
        require(osVersion.isNotBlank() && osVersion.length <= 128)
        require(architecture.isNotBlank() && architecture.length <= 32)
        require(packageIdentity.isNotBlank() && packageIdentity.length <= 160)
        require(appVersion.isNotBlank() && appVersion.length <= 64)
        require(SOURCE_COMMIT.matches(sourceCommit))
        require(clientContractVersion >= 1)
        require(localSchemaVersion >= 1)
        require(deployment.profileId.isNotBlank() && deployment.profileId.length <= 64)
        require(deployment.environmentId.isNotBlank() && deployment.environmentId.length <= 64)
        require(deployment.trustDomainId.isNotBlank() && deployment.trustDomainId.length <= 96)
        require(deployment.providerId.isNotBlank() && deployment.providerId.length <= 32)
        require(queues.pendingIntents >= 0)
        require(queues.outboxItems >= 0)
        require(queues.attachmentStaging >= 0)
        require(recentErrorCodes.size <= 20)
        require(recentErrorCodes.all(ERROR_CODE::matches))
        compatibility.reasonCode?.let { require(ERROR_CODE.matches(it)) }
        compatibility.policyRevision?.let { require(POLICY_REVISION.matches(it)) }
    }

    private companion object {
        val SOURCE_COMMIT = Regex("^[0-9a-f]{40}$")
        val ERROR_CODE = Regex("^XQ_[A-Z0-9_]{3,64}$")
        val POLICY_REVISION = Regex("^[a-z0-9][a-z0-9._-]{1,63}$")
    }
}
