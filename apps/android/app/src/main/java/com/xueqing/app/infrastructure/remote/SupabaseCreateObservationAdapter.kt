package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.observation.CreateObservationReceipt
import com.xueqing.app.application.observation.CreateObservationRejection
import com.xueqing.app.application.observation.CreateObservationRequest
import com.xueqing.app.application.observation.ObservationCommandRemote
import com.xueqing.app.application.observation.ObservationCommandResult
import com.xueqing.app.application.observation.ObservationProtocolFailure
import com.xueqing.app.application.observation.ObservationUnknownReason
import java.time.Instant
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

class SupabaseCreateObservationAdapter(
    private val sessionTokenSource: SessionTokenSource,
    private val transport: RpcTransport,
    private val json: Json = Json { ignoreUnknownKeys = true },
) : ObservationCommandRemote {
    override fun createObservation(request: CreateObservationRequest): ObservationCommandResult {
        val accessToken = sessionTokenSource.currentAccessToken()
            ?.takeIf(String::isNotBlank)
            ?: return ObservationCommandResult.AuthenticationRequired(request.operationId)

        val call = RpcCall(
            functionName = RPC_NAME,
            accessToken = accessToken,
            bodyJson = encodeRequest(request),
        )

        return when (val result = transport.post(call)) {
            RpcTransportResult.Timeout -> ObservationCommandResult.UnknownResult(
                request.operationId,
                ObservationUnknownReason.Timeout,
            )

            RpcTransportResult.NetworkFailure -> ObservationCommandResult.UnknownResult(
                request.operationId,
                ObservationUnknownReason.NetworkFailure,
            )

            is RpcTransportResult.Response -> mapResponse(request, result)
        }
    }

    private fun mapResponse(
        request: CreateObservationRequest,
        response: RpcTransportResult.Response,
    ): ObservationCommandResult {
        if (response.statusCode in 200..299) {
            val receipt = decodeAndValidateReceipt(request, response.body)
                ?: return ObservationCommandResult.UnknownResult(
                    request.operationId,
                    ObservationUnknownReason.InvalidSuccessPayload,
                )
            return ObservationCommandResult.Accepted(request.operationId, receipt)
        }

        if (response.statusCode == 401) {
            return ObservationCommandResult.AuthenticationRequired(request.operationId)
        }

        if (response.statusCode >= 500) {
            return ObservationCommandResult.UnknownResult(
                request.operationId,
                ObservationUnknownReason.ServerFailure,
            )
        }

        val error = decodeProviderError(response.body)
            ?: return ObservationCommandResult.ProtocolFailure(
                request.operationId,
                ObservationProtocolFailure.UnrecognizedResponse,
            )

        if (error.code == "P0001") {
            val rejection = CreateObservationRejection.fromWireCode(error.message)
            if (rejection != null) {
                return ObservationCommandResult.Rejected(request.operationId, rejection)
            }
            return ObservationCommandResult.ProtocolFailure(
                request.operationId,
                ObservationProtocolFailure.UnknownDomainError,
            )
        }

        return ObservationCommandResult.ProtocolFailure(
            request.operationId,
            ObservationProtocolFailure.ProviderProtocolError,
        )
    }

    private fun encodeRequest(request: CreateObservationRequest): String =
        buildJsonObject {
            put("p_operation_id", request.operationId.toString())
            put("p_organization_id", request.organizationId.toString())
            put("p_student_id", request.studentId.toString())
            put("p_subject_profile_id", request.subjectProfileId.toString())
            put("p_assignment_id", request.assignmentId.toString())
            put("p_raw_text", request.rawText)
            request.clientCapturedAt?.let {
                put("p_client_captured_at", it.toString())
            } ?: put("p_client_captured_at", JsonNull)
            put(
                "p_client_capture_metadata",
                buildJsonObject {
                    request.clientCaptureMetadata.toSortedMap().forEach { (key, value) ->
                        put(key, value)
                    }
                },
            )
        }.toString()

    private fun decodeAndValidateReceipt(
        request: CreateObservationRequest,
        body: String,
    ): CreateObservationReceipt? = runCatching {
        val root = json.parseToJsonElement(body) as? JsonObject ?: return null
        val receipt = CreateObservationReceipt(
            command = root.requiredString("command"),
            operationId = UUID.fromString(root.requiredString("operation_id")),
            observationId = UUID.fromString(root.requiredString("observation_id")),
            actorAppUserId = UUID.fromString(root.requiredString("actor_app_user_id")),
            organizationId = UUID.fromString(root.requiredString("organization_id")),
            studentId = UUID.fromString(root.requiredString("student_id")),
            subjectProfileId = UUID.fromString(root.requiredString("subject_profile_id")),
            subjectKey = root.requiredString("subject_key"),
            serverCommittedAt = Instant.parse(root.requiredString("server_committed_at")),
        )

        require(receipt.command == COMMAND_NAME)
        require(receipt.operationId == request.operationId)
        require(receipt.organizationId == request.organizationId)
        require(receipt.studentId == request.studentId)
        require(receipt.subjectProfileId == request.subjectProfileId)
        require(receipt.subjectKey.isNotBlank())
        receipt
    }.getOrNull()

    private fun decodeProviderError(body: String): ProviderError? = runCatching {
        val root = json.parseToJsonElement(body) as? JsonObject ?: return null
        ProviderError(
            code = root.requiredString("code"),
            message = root.requiredString("message"),
        )
    }.getOrNull()

    private fun JsonObject.requiredString(name: String): String {
        val value = this[name] as? JsonPrimitive
            ?: error("Missing JSON string field: $name")
        require(value !== JsonNull) { "JSON string field is null: $name" }
        return value.content
    }

    private data class ProviderError(
        val code: String,
        val message: String,
    )

    private companion object {
        const val RPC_NAME = "create_observation"
        const val COMMAND_NAME = "create_observation_v1"
    }
}
