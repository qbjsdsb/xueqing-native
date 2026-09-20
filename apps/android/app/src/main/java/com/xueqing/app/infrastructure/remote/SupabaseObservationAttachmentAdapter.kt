package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.attachment.AttachmentCommandRemote
import com.xueqing.app.application.attachment.AttachmentCommitProtocolFailure
import com.xueqing.app.application.attachment.AttachmentCommitResult
import com.xueqing.app.application.attachment.AttachmentCommitUnknownReason
import com.xueqing.app.application.attachment.AttachmentStorageRemote
import com.xueqing.app.application.attachment.AttachmentUploadRejection
import com.xueqing.app.application.attachment.AttachmentUploadResult
import com.xueqing.app.application.attachment.AttachmentUploadUnknownReason
import com.xueqing.app.application.attachment.CommitObservationAttachmentReceipt
import com.xueqing.app.application.attachment.CommitObservationAttachmentRejection
import com.xueqing.app.application.attachment.CommitObservationAttachmentRequest
import com.xueqing.app.application.attachment.ObservationAttachmentUploadRequest
import java.time.Instant
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

class SupabaseObservationAttachmentStorageAdapter(
    private val sessionTokenSource: SessionTokenSource,
    private val transport: StorageTransport,
    private val json: Json = Json { ignoreUnknownKeys = true },
) : AttachmentStorageRemote {
    override fun upload(
        request: ObservationAttachmentUploadRequest,
        body: ByteArray,
    ): AttachmentUploadResult {
        if (body.size.toLong() != request.byteSize || body.isEmpty()) {
            return AttachmentUploadResult.Rejected(AttachmentUploadRejection.LocalContentMismatch)
        }

        val accessToken = sessionTokenSource.currentAccessToken()
            ?.takeIf(String::isNotBlank)
            ?: return AttachmentUploadResult.AuthenticationRequired

        return when (
            val result = transport.upload(
                StorageUploadCall(
                    bucketId = BUCKET_ID,
                    objectName = request.objectName,
                    accessToken = accessToken,
                    contentType = request.contentType,
                    body = body,
                ),
            )
        ) {
            StorageTransportResult.Timeout ->
                AttachmentUploadResult.UnknownResult(AttachmentUploadUnknownReason.Timeout)

            StorageTransportResult.NetworkFailure ->
                AttachmentUploadResult.UnknownResult(AttachmentUploadUnknownReason.NetworkFailure)

            is StorageTransportResult.Response -> when {
                result.statusCode in 200..299 -> AttachmentUploadResult.Uploaded
                result.statusCode == 409 || storageErrorName(result.body) == "Duplicate" ->
                    AttachmentUploadResult.AlreadyPresent
                result.statusCode == 401 -> AttachmentUploadResult.AuthenticationRequired
                result.statusCode in TRANSIENT_HTTP_STATUSES ->
                    AttachmentUploadResult.UnknownResult(AttachmentUploadUnknownReason.ServerFailure)
                result.statusCode >= 500 ->
                    AttachmentUploadResult.UnknownResult(AttachmentUploadUnknownReason.ServerFailure)
                result.statusCode == 403 ->
                    AttachmentUploadResult.Rejected(AttachmentUploadRejection.AccessDenied)
                result.statusCode in 400..499 ->
                    AttachmentUploadResult.Rejected(AttachmentUploadRejection.InvalidRequest)
                else ->
                    AttachmentUploadResult.Rejected(AttachmentUploadRejection.UnexpectedResponse)
            }
        }
    }

    private fun storageErrorName(body: String): String? = runCatching {
        val root = json.parseToJsonElement(body) as? JsonObject ?: return null
        (root["error"] as? JsonPrimitive)?.content?.takeIf(String::isNotBlank)
    }.getOrNull()

    private companion object {
        const val BUCKET_ID = "teaching-attachments-v1"
        val TRANSIENT_HTTP_STATUSES = setOf(408, 425, 429)
    }
}

class SupabaseCommitObservationAttachmentAdapter(
    private val sessionTokenSource: SessionTokenSource,
    private val transport: RpcTransport,
    private val json: Json = Json { ignoreUnknownKeys = true },
) : AttachmentCommandRemote {
    override fun commit(request: CommitObservationAttachmentRequest): AttachmentCommitResult {
        val accessToken = sessionTokenSource.currentAccessToken()
            ?.takeIf(String::isNotBlank)
            ?: return AttachmentCommitResult.AuthenticationRequired(request.operationId)

        val call = RpcCall(
            functionName = RPC_NAME,
            accessToken = accessToken,
            bodyJson = encodeRequest(request),
        )

        return when (val result = transport.post(call)) {
            RpcTransportResult.Timeout -> AttachmentCommitResult.UnknownResult(
                request.operationId,
                AttachmentCommitUnknownReason.Timeout,
            )

            RpcTransportResult.NetworkFailure -> AttachmentCommitResult.UnknownResult(
                request.operationId,
                AttachmentCommitUnknownReason.NetworkFailure,
            )

            is RpcTransportResult.Response -> mapResponse(request, result)
        }
    }

    private fun mapResponse(
        request: CommitObservationAttachmentRequest,
        response: RpcTransportResult.Response,
    ): AttachmentCommitResult {
        if (response.statusCode in 200..299) {
            val receipt = decodeAndValidateReceipt(request, response.body)
                ?: return AttachmentCommitResult.UnknownResult(
                    request.operationId,
                    AttachmentCommitUnknownReason.InvalidSuccessPayload,
                )
            return AttachmentCommitResult.Accepted(request.operationId, receipt)
        }

        if (response.statusCode == 401) {
            return AttachmentCommitResult.AuthenticationRequired(request.operationId)
        }

        if (response.statusCode in TRANSIENT_HTTP_STATUSES || response.statusCode >= 500) {
            return AttachmentCommitResult.UnknownResult(
                request.operationId,
                AttachmentCommitUnknownReason.ServerFailure,
            )
        }

        val error = decodeProviderError(response.body)
            ?: return AttachmentCommitResult.ProtocolFailure(
                request.operationId,
                AttachmentCommitProtocolFailure.UnrecognizedResponse,
            )

        if (error.code == "P0001") {
            val rejection = CommitObservationAttachmentRejection.fromWireCode(error.message)
            if (rejection != null) {
                return AttachmentCommitResult.Rejected(request.operationId, rejection)
            }
            return AttachmentCommitResult.ProtocolFailure(
                request.operationId,
                AttachmentCommitProtocolFailure.UnknownDomainError,
            )
        }

        return AttachmentCommitResult.ProtocolFailure(
            request.operationId,
            AttachmentCommitProtocolFailure.ProviderProtocolError,
        )
    }

    private fun encodeRequest(request: CommitObservationAttachmentRequest): String =
        buildJsonObject {
            put("p_operation_id", request.operationId.toString())
            put("p_organization_id", request.organizationId.toString())
            put("p_student_id", request.studentId.toString())
            put("p_subject_profile_id", request.subjectProfileId.toString())
            put("p_assignment_id", request.assignmentId.toString())
            put("p_observation_id", request.observationId.toString())
            put("p_attachment_id", request.attachmentId.toString())
        }.toString()

    private fun decodeAndValidateReceipt(
        request: CommitObservationAttachmentRequest,
        body: String,
    ): CommitObservationAttachmentReceipt? = runCatching {
        val root = json.parseToJsonElement(body) as? JsonObject ?: return null
        val receipt = CommitObservationAttachmentReceipt(
            command = root.requiredString("command"),
            operationId = UUID.fromString(root.requiredString("operation_id")),
            attachmentId = UUID.fromString(root.requiredString("attachment_id")),
            observationId = UUID.fromString(root.requiredString("observation_id")),
            actorAppUserId = UUID.fromString(root.requiredString("actor_app_user_id")),
            organizationId = UUID.fromString(root.requiredString("organization_id")),
            studentId = UUID.fromString(root.requiredString("student_id")),
            subjectProfileId = UUID.fromString(root.requiredString("subject_profile_id")),
            subjectKey = root.requiredString("subject_key"),
            assignmentId = UUID.fromString(root.requiredString("assignment_id")),
            bucketId = root.requiredString("bucket_id"),
            objectName = root.requiredString("object_name"),
            contentType = root.requiredString("content_type"),
            byteSize = root.requiredLong("byte_size"),
            serverCommittedAt = Instant.parse(root.requiredString("server_committed_at")),
        )

        require(receipt.command == COMMAND_NAME)
        require(receipt.operationId == request.operationId)
        require(receipt.attachmentId == request.attachmentId)
        require(receipt.observationId == request.observationId)
        require(receipt.organizationId == request.organizationId)
        require(receipt.studentId == request.studentId)
        require(receipt.subjectProfileId == request.subjectProfileId)
        require(receipt.assignmentId == request.assignmentId)
        require(receipt.bucketId == BUCKET_ID)
        require(receipt.objectName == canonicalObjectName(request))
        require(receipt.contentType in ALLOWED_CONTENT_TYPES)
        require(receipt.byteSize in 1..MAX_BYTES)
        require(receipt.subjectKey.isNotBlank())
        receipt
    }.getOrNull()

    private fun canonicalObjectName(request: CommitObservationAttachmentRequest): String =
        "v1/org/${request.organizationId}" +
            "/student/${request.studentId}" +
            "/profile/${request.subjectProfileId}" +
            "/observation/${request.observationId}" +
            "/attachment/${request.attachmentId}"

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

    private fun JsonObject.requiredLong(name: String): Long =
        requiredString(name).toLong()

    private data class ProviderError(
        val code: String,
        val message: String,
    )

    private companion object {
        const val RPC_NAME = "commit_observation_attachment"
        const val COMMAND_NAME = "commit_observation_attachment_v1"
        const val BUCKET_ID = "teaching-attachments-v1"
        const val MAX_BYTES = 6_291_456L
        val ALLOWED_CONTENT_TYPES = setOf("image/jpeg", "image/png", "image/webp")
        val TRANSIENT_HTTP_STATUSES = setOf(408, 425, 429)
    }
}
