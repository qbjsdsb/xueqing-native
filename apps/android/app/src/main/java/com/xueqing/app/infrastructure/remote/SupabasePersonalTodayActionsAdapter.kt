package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.learning.LearningReadFailure
import com.xueqing.app.application.learning.LearningReadFailureKind
import com.xueqing.app.application.learning.PersonalTodayActionsRemote
import com.xueqing.app.application.learning.PersonalTodayActionsResult
import java.util.UUID
import kotlinx.serialization.json.Json

class SupabasePersonalTodayActionsAdapter(
    private val sessionTokenSource: SessionTokenSource,
    private val transport: RpcTransport,
    private val json: Json = Json { ignoreUnknownKeys = true },
) : PersonalTodayActionsRemote {
    override fun fetch(expectedActorAppUserId: UUID): PersonalTodayActionsResult {
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

        return when (
            val result = transport.post(
                RpcCall(
                    functionName = RPC_NAME,
                    accessToken = accessToken,
                    bodyJson = "{}",
                ),
            )
        ) {
            RpcTransportResult.Timeout ->
                failed(LearningReadFailureKind.Transient, "XQ_NETWORK_TIMEOUT")

            RpcTransportResult.NetworkFailure ->
                failed(LearningReadFailureKind.Transient, "XQ_NETWORK_UNAVAILABLE")

            is RpcTransportResult.Response ->
                mapResponse(result, expectedActorAppUserId)
        }
    }

    private fun mapResponse(
        response: RpcTransportResult.Response,
        expectedActorAppUserId: UUID,
    ): PersonalTodayActionsResult {
        if (response.statusCode in 200..299) {
            val snapshot = runCatching {
                LearningProjectionJson.parsePersonalToday(
                    json = json,
                    body = response.body,
                    expectedActorAppUserId = expectedActorAppUserId,
                )
            }.getOrNull() ?: return failed(
                LearningReadFailureKind.InvalidResponse,
                "XQ_PROJECTION_CONTRACT_INVALID",
            )
            return PersonalTodayActionsResult.Loaded(snapshot)
        }

        return PersonalTodayActionsResult.Failed(
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
    ) = PersonalTodayActionsResult.Failed(LearningReadFailure(kind, code))

    private companion object {
        const val RPC_NAME = "get_personal_today_actions_v1"
        val ZERO_UUID: UUID = UUID(0L, 0L)
    }
}
