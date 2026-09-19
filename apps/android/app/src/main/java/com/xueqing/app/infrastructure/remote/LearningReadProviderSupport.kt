package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.learning.LearningReadFailure
import com.xueqing.app.application.learning.LearningReadFailureKind
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

internal object LearningReadProviderSupport {
    fun mapFailure(
        statusCode: Int,
        body: String,
        json: Json,
    ): LearningReadFailure {
        if (statusCode == 401) {
            return LearningReadFailure(
                LearningReadFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED",
            )
        }

        val message = providerMessage(body, json)
        if (message == "XQ_AUTH_REQUIRED" || message == "XQ_ACTOR_NOT_FOUND") {
            return LearningReadFailure(
                LearningReadFailureKind.AuthenticationRequired,
                message,
            )
        }

        if (
            message == "XQ_ACTOR_DISABLED" ||
            message == "XQ_TEACHING_CONTEXT_REQUIRED" ||
            message == "XQ_TEACHING_CONTEXT_UNAVAILABLE"
        ) {
            return LearningReadFailure(LearningReadFailureKind.AccessDenied, message)
        }

        if (
            message == "XQ_CASE_PRIMARY_ACTION_INVARIANT" ||
            message == "XQ_INVALID_ORGANIZATION_TIME_ZONE" ||
            message == "XQ_BUSINESS_DATE_INPUT_REQUIRED"
        ) {
            return LearningReadFailure(LearningReadFailureKind.ServerInvariant, message)
        }

        if (statusCode == 429 || statusCode >= 500) {
            return LearningReadFailure(
                LearningReadFailureKind.Transient,
                message ?: "HTTP_$statusCode",
            )
        }

        return LearningReadFailure(
            LearningReadFailureKind.InvalidResponse,
            message ?: "HTTP_$statusCode",
        )
    }

    private fun providerMessage(
        body: String,
        json: Json,
    ): String? = runCatching {
        val root = json.parseToJsonElement(body) as? JsonObject
            ?: error("Provider error body must be an object.")
        val message = root["message"] as? JsonPrimitive
            ?: error("Provider error body has no message.")
        require(message.isString)
        message.content
    }.getOrNull()
}
