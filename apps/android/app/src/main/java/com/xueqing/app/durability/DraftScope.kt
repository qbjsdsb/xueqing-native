package com.xueqing.app.durability

import java.security.MessageDigest

data class DraftScope(
    val environmentId: String,
    val appUserId: String,
    val organizationId: String,
    val studentId: String,
    val subjectId: String,
    val contextId: String,
) {
    init {
        require(environmentId.isNotBlank())
        require(appUserId.isNotBlank())
        require(organizationId.isNotBlank())
        require(studentId.isNotBlank())
        require(subjectId.isNotBlank())
        require(contextId.isNotBlank())
    }

    val storageKey: String by lazy(LazyThreadSafetyMode.PUBLICATION) {
        val canonical = listOf(
            environmentId,
            appUserId,
            organizationId,
            studentId,
            subjectId,
            contextId,
        ).joinToString(separator = "\u001f")

        MessageDigest.getInstance("SHA-256")
            .digest(canonical.toByteArray(Charsets.UTF_8))
            .joinToString(separator = "") { byte -> "%02x".format(byte) }
    }
}
