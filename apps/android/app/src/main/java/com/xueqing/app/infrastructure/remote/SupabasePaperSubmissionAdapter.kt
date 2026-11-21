package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.paper.CreatePaperSubmissionReceipt
import com.xueqing.app.application.paper.CreatePaperSubmissionRejection
import com.xueqing.app.application.paper.CreatePaperSubmissionRequest
import com.xueqing.app.application.paper.PaperKind
import com.xueqing.app.application.paper.PaperSubmissionProtocolFailure
import com.xueqing.app.application.paper.PaperSubmissionRemote
import com.xueqing.app.application.paper.PaperSubmissionResult
import com.xueqing.app.application.paper.PaperSubmissionUnknownReason
import java.time.Instant
import java.time.LocalDate
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

class SupabasePaperSubmissionAdapter(
    private val sessionTokenSource: SessionTokenSource,
    private val transport: RpcTransport,
    private val json: Json = Json { ignoreUnknownKeys = true },
) : PaperSubmissionRemote {
    override fun create(request: CreatePaperSubmissionRequest): PaperSubmissionResult {
        val accessToken = sessionTokenSource.currentAccessToken()
            ?.takeIf(String::isNotBlank)
            ?: return PaperSubmissionResult.AuthenticationRequired(request.operationId)

        val call = RpcCall(
            functionName = RPC_NAME,
            accessToken = accessToken,
            bodyJson = encodeRequest(request),
        )

        return when (val result = transport.post(call)) {
            RpcTransportResult.Timeout -> PaperSubmissionResult.UnknownResult(
                request.operationId,
                PaperSubmissionUnknownReason.Timeout,
            )

            RpcTransportResult.NetworkFailure -> PaperSubmissionResult.UnknownResult(
                request.operationId,
                PaperSubmissionUnknownReason.NetworkFailure,
            )

            is RpcTransportResult.Response -> mapResponse(request, result)
        }
    }

    private fun mapResponse(
        request: CreatePaperSubmissionRequest,
        response: RpcTransportResult.Response,
    ): PaperSubmissionResult {
        if (response.statusCode in 200..299) {
            val receipt = decodeAndValidateReceipt(request, response.body)
                ?: return PaperSubmissionResult.UnknownResult(
                    request.operationId,
                    PaperSubmissionUnknownReason.InvalidSuccessPayload,
                )
            return PaperSubmissionResult.Accepted(request.operationId, receipt)
        }

        if (response.statusCode == 401) {
            return PaperSubmissionResult.AuthenticationRequired(request.operationId)
        }

        if (response.statusCode in TRANSIENT_HTTP_STATUSES || response.statusCode >= 500) {
            return PaperSubmissionResult.UnknownResult(
                request.operationId,
                PaperSubmissionUnknownReason.ServerFailure,
            )
        }

        val error = decodeProviderError(response.body)
            ?: return PaperSubmissionResult.ProtocolFailure(
                request.operationId,
                PaperSubmissionProtocolFailure.UnrecognizedResponse,
            )

        if (error.code == "P0001") {
            val rejection = CreatePaperSubmissionRejection.fromWireCode(error.message)
            if (rejection != null) {
                return PaperSubmissionResult.Rejected(request.operationId, rejection)
            }
            return PaperSubmissionResult.ProtocolFailure(
                request.operationId,
                PaperSubmissionProtocolFailure.UnknownDomainError,
            )
        }

        return PaperSubmissionResult.ProtocolFailure(
            request.operationId,
            PaperSubmissionProtocolFailure.ProviderProtocolError,
        )
    }

    private fun encodeRequest(request: CreatePaperSubmissionRequest): String =
        buildJsonObject {
            put("p_operation_id", request.operationId.toString())
            put("p_organization_id", request.organizationId.toString())
            put("p_student_id", request.studentId.toString())
            put("p_subject_profile_id", request.subjectProfileId.toString())
            put("p_assignment_id", request.assignmentId.toString())
            put("p_paper_date", request.paperDate.toString())
            put("p_paper_kind", request.paperKind.wireValue)
            if (request.title != null) {
                put("p_title", request.title.trim())
            } else {
                put("p_title", JsonNull)
            }
            if (request.sourceObservationId != null) {
                put("p_source_observation_id", request.sourceObservationId.toString())
            } else {
                put("p_source_observation_id", JsonNull)
            }
            put(
                "p_pages",
                buildJsonArray {
                    request.pages.sortedBy { it.pageIndex }.forEach { page ->
                        add(
                            buildJsonObject {
                                put("page_index", page.pageIndex)
                                put("attachment_id", page.attachmentId.toString())
                                put("header_redacted", page.headerRedacted)
                            },
                        )
                    }
                },
            )
        }.toString()

    private fun decodeAndValidateReceipt(
        request: CreatePaperSubmissionRequest,
        body: String,
    ): CreatePaperSubmissionReceipt? = runCatching {
        val root = json.parseToJsonElement(body) as? JsonObject ?: return null
        val receipt = CreatePaperSubmissionReceipt(
            contract = root.requiredString("contract"),
            operationId = UUID.fromString(root.requiredString("operation_id")),
            paperSubmissionId = UUID.fromString(root.requiredString("paper_submission_id")),
            organizationId = UUID.fromString(root.requiredString("organization_id")),
            studentId = UUID.fromString(root.requiredString("student_id")),
            subjectProfileId = UUID.fromString(root.requiredString("subject_profile_id")),
            assignmentId = UUID.fromString(root.requiredString("assignment_id")),
            paperDate = LocalDate.parse(root.requiredString("paper_date")),
            paperKind = requireNotNull(PaperKind.fromWireValue(root.requiredString("paper_kind"))),
            title = root.optionalString("title"),
            confirmationState = root.requiredString("confirmation_state"),
            version = root.requiredLong("version"),
            pageCount = root.requiredInt("page_count"),
            serverCommittedAt = Instant.parse(root.requiredString("server_committed_at")),
        )

        require(receipt.contract == CONTRACT_NAME)
        require(receipt.operationId == request.operationId)
        require(receipt.organizationId == request.organizationId)
        require(receipt.studentId == request.studentId)
        require(receipt.subjectProfileId == request.subjectProfileId)
        require(receipt.assignmentId == request.assignmentId)
        require(receipt.paperDate == request.paperDate)
        require(receipt.paperKind == request.paperKind)
        require(receipt.pageCount == request.pages.size)
        require(receipt.version >= 1L)
        require(receipt.confirmationState.isNotBlank())
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

    private fun JsonObject.optionalString(name: String): String? {
        val value = this[name] ?: return null
        if (value === JsonNull) return null
        val primitive = value as? JsonPrimitive ?: error("Expected string field: $name")
        return primitive.content.takeIf(String::isNotBlank)
    }

    private fun JsonObject.requiredLong(name: String): Long =
        requiredString(name).toLong()

    private fun JsonObject.requiredInt(name: String): Int =
        requiredString(name).toInt()

    private data class ProviderError(
        val code: String,
        val message: String,
    )

    private companion object {
        const val RPC_NAME = "create_paper_submission_v1"
        const val CONTRACT_NAME = "create_paper_submission_v1"
        val TRANSIENT_HTTP_STATUSES = setOf(408, 425, 429)
    }
}
