package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.learning.LearningReadFailure
import com.xueqing.app.application.learning.LearningReadFailureKind
import com.xueqing.app.application.learning.StudentLearningFocusRemote
import com.xueqing.app.application.learning.StudentLearningFocusResult
import com.xueqing.app.application.learning.StudentLearningScope
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

class SupabaseStudentLearningFocusAdapter(
    private val sessionTokenSource: SessionTokenSource,
    private val transport: RpcTransport,
    private val json: Json = Json { ignoreUnknownKeys = true },
) : StudentLearningFocusRemote {
    override fun fetch(
        scope: StudentLearningScope,
        expectedActorAppUserId: UUID,
    ): StudentLearningFocusResult {
        if (
            scope.organizationId == ZERO_UUID ||
            scope.studentId == ZERO_UUID ||
            scope.subjectProfileId == ZERO_UUID
        ) {
            return failed(LearningReadFailureKind.InvalidResponse, "XQ_CLIENT_SCOPE_INVALID")
        }

        if (expectedActorAppUserId == ZERO_UUID) {
            return failed(
                LearningReadFailureKind.AuthenticationRequired,
                "XQ_CLIENT_ACTOR_REQUIRED",
            )
        }

        val accessToken = sessionTokenSource.currentAccessToken()
            ?.takeIf(String::isNotBlank)
            ?: return failed(
                LearningReadFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED",
            )

        val body = buildJsonObject {
            put("p_organization_id", scope.organizationId.toString())
            put("p_student_id", scope.studentId.toString())
            put("p_subject_profile_id", scope.subjectProfileId.toString())
        }.toString()

        return when (
            val result = transport.post(
                RpcCall(
                    functionName = RPC_NAME,
                    accessToken = accessToken,
                    bodyJson = body,
                ),
            )
        ) {
            RpcTransportResult.Timeout ->
                failed(LearningReadFailureKind.Transient, "XQ_NETWORK_TIMEOUT")

            RpcTransportResult.NetworkFailure ->
                failed(LearningReadFailureKind.Transient, "XQ_NETWORK_UNAVAILABLE")

            is RpcTransportResult.Response ->
                mapResponse(result, scope, expectedActorAppUserId)
        }
    }

    private fun mapResponse(
        response: RpcTransportResult.Response,
        scope: StudentLearningScope,
        expectedActorAppUserId: UUID,
    ): StudentLearningFocusResult {
        if (response.statusCode in 200..299) {
            val snapshot = runCatching {
                LearningProjectionJson.parseStudentFocus(
                    json = json,
                    body = response.body,
                    requestedScope = scope,
                    expectedActorAppUserId = expectedActorAppUserId,
                )
            }.getOrNull() ?: return failed(
                LearningReadFailureKind.InvalidResponse,
                "XQ_PROJECTION_CONTRACT_INVALID",
            )
            return StudentLearningFocusResult.Loaded(snapshot)
        }

        return StudentLearningFocusResult.Failed(
            LearningReadProviderSupport.mapFailure(
                statusCode = response.statusCode,
                body = response.body,
                json = json,
            ),
        )
    }

    private fun failed(
        kind: LearningReadFailureKind,
        code: String,
    ) = StudentLearningFocusResult.Failed(LearningReadFailure(kind, code))

    private companion object {
        const val RPC_NAME = "get_student_learning_focus_v1"
        val ZERO_UUID: UUID = UUID(0L, 0L)
    }
}
