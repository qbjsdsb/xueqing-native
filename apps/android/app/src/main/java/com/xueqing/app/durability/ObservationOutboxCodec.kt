package com.xueqing.app.durability

import com.xueqing.app.application.observation.CreateObservationReceipt
import com.xueqing.app.application.observation.CreateObservationRequest
import java.time.Instant
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

object ObservationOutboxCodec {
    const val COMMAND_TYPE = "create_observation_v1"

    private val json = Json { ignoreUnknownKeys = false }

    fun encodeRequest(request: CreateObservationRequest): String =
        buildJsonObject {
            put("operation_id", request.operationId.toString())
            put("organization_id", request.organizationId.toString())
            put("student_id", request.studentId.toString())
            put("subject_profile_id", request.subjectProfileId.toString())
            put("assignment_id", request.assignmentId.toString())
            put("raw_text", request.rawText)
            request.clientCapturedAt?.let { put("client_captured_at", it.toString()) }
            put(
                "client_capture_metadata",
                buildJsonObject {
                    request.clientCaptureMetadata.toSortedMap().forEach { (key, value) ->
                        put(key, value)
                    }
                },
            )
        }.toString()

    fun decodeRequest(payloadJson: String): CreateObservationRequest =
        (json.parseToJsonElement(payloadJson) as? JsonObject ?: error("Observation outbox payload must be an object"))
            .let { root ->
                val metadata = root["client_capture_metadata"] as? JsonObject
                    ?: error("Observation outbox payload is missing metadata")
                CreateObservationRequest(
                    operationId = UUID.fromString(root.requiredString("operation_id")),
                    organizationId = UUID.fromString(root.requiredString("organization_id")),
                    studentId = UUID.fromString(root.requiredString("student_id")),
                    subjectProfileId = UUID.fromString(root.requiredString("subject_profile_id")),
                    assignmentId = UUID.fromString(root.requiredString("assignment_id")),
                    rawText = root.requiredString("raw_text"),
                    clientCapturedAt = root.optionalString("client_captured_at")?.let(Instant::parse),
                    clientCaptureMetadata = metadata.mapValues { (_, value) ->
                        (value as? JsonPrimitive)?.content
                            ?: error("Observation metadata values must be strings")
                    },
                )
            }

    fun encodeReceipt(receipt: CreateObservationReceipt): String =
        buildJsonObject {
            put("command", receipt.command)
            put("operation_id", receipt.operationId.toString())
            put("observation_id", receipt.observationId.toString())
            put("actor_app_user_id", receipt.actorAppUserId.toString())
            put("organization_id", receipt.organizationId.toString())
            put("student_id", receipt.studentId.toString())
            put("subject_profile_id", receipt.subjectProfileId.toString())
            put("subject_key", receipt.subjectKey)
            put("server_committed_at", receipt.serverCommittedAt.toString())
        }.toString()

    private fun JsonObject.requiredString(name: String): String =
        (this[name] as? JsonPrimitive)?.content
            ?.takeIf(String::isNotBlank)
            ?: error("Observation outbox payload has invalid field: $name")

    private fun JsonObject.optionalString(name: String): String? =
        (this[name] as? JsonPrimitive)?.content?.takeIf(String::isNotBlank)
}
