package com.xueqing.app.application.paper

import java.time.Instant
import java.time.LocalDate
import java.util.UUID

enum class PaperKind(
    val wireValue: String,
    val displayLabel: String,
) {
    Homework("homework", "作业"),
    Quiz("quiz", "小测"),
    UnitTest("unit_test", "单元测"),
    Exam("exam", "大考"),
    ;

    companion object {
        private val byWireValue = entries.associateBy(PaperKind::wireValue)

        fun fromWireValue(wireValue: String): PaperKind? = byWireValue[wireValue]
    }
}

data class HeaderRedactionPolicy(
    val enabled: Boolean = true,
    val topBandRatio: Float = DEFAULT_TOP_BAND_RATIO,
) {
    init {
        require(topBandRatio in MIN_TOP_BAND_RATIO..MAX_TOP_BAND_RATIO) {
            "Header redaction ratio must be within $MIN_TOP_BAND_RATIO..$MAX_TOP_BAND_RATIO"
        }
    }

    fun effectiveRatio(): Float = if (enabled) topBandRatio else 0f

    companion object {
        const val DEFAULT_TOP_BAND_RATIO = 0.14f
        const val MIN_TOP_BAND_RATIO = 0.05f
        const val MAX_TOP_BAND_RATIO = 0.35f
    }
}

data class PaperCaptureMetadata(
    val enabled: Boolean = false,
    val paperDate: LocalDate,
    val paperKind: PaperKind = PaperKind.Homework,
    val title: String = "",
    val headerRedactionEnabled: Boolean = true,
) {
    fun toClientCaptureMetadata(
        pageCount: Int,
        redactedPageIndices: List<Int>,
    ): Map<String, String> {
        val result = linkedMapOf(
            "surface" to "quick_capture",
            "header_redaction_enabled" to headerRedactionEnabled.toString(),
        )
        if (pageCount > 0) {
            result["attachment_page_count"] = pageCount.toString()
        }
        if (redactedPageIndices.isNotEmpty()) {
            result["header_redacted_pages"] = redactedPageIndices.sorted().distinct().joinToString(",")
        }
        if (enabled) {
            result["capture_mode"] = "paper_evidence"
            result["paper_date"] = paperDate.toString()
            result["paper_kind"] = paperKind.wireValue
            val trimmedTitle = title.trim()
            if (trimmedTitle.isNotEmpty()) {
                result["paper_title"] = trimmedTitle.take(MAX_TITLE_LENGTH)
            }
        }
        return result
    }

    companion object {
        const val MAX_PAGES = 6
        const val MAX_TITLE_LENGTH = 200
    }
}

data class PaperPageInput(
    val pageIndex: Int,
    val attachmentId: UUID,
    val headerRedacted: Boolean,
) {
    init {
        require(pageIndex in 1..MAX_SERVER_PAGES) {
            "Paper page index must be between 1 and $MAX_SERVER_PAGES"
        }
    }

    companion object {
        const val MAX_SERVER_PAGES = 50
    }
}

data class CreatePaperSubmissionRequest(
    val operationId: UUID,
    val organizationId: UUID,
    val studentId: UUID,
    val subjectProfileId: UUID,
    val assignmentId: UUID,
    val paperDate: LocalDate,
    val paperKind: PaperKind,
    val title: String? = null,
    val sourceObservationId: UUID? = null,
    val pages: List<PaperPageInput>,
) {
    init {
        require(pages.isNotEmpty()) { "Paper submission must include at least one page" }
        require(pages.size <= PaperPageInput.MAX_SERVER_PAGES) {
            "Paper submission cannot exceed ${PaperPageInput.MAX_SERVER_PAGES} pages"
        }
        require(pages.map(PaperPageInput::pageIndex).distinct().size == pages.size) {
            "Paper submission page indices must be unique"
        }
        require(pages.map(PaperPageInput::attachmentId).distinct().size == pages.size) {
            "Paper submission attachment IDs must be unique"
        }
        if (title != null) {
            val trimmed = title.trim()
            require(trimmed.isNotEmpty() && trimmed.length <= PaperCaptureMetadata.MAX_TITLE_LENGTH) {
                "Paper title must be 1..${PaperCaptureMetadata.MAX_TITLE_LENGTH} chars when provided"
            }
        }
    }
}

data class CreatePaperSubmissionReceipt(
    val contract: String,
    val operationId: UUID,
    val paperSubmissionId: UUID,
    val organizationId: UUID,
    val studentId: UUID,
    val subjectProfileId: UUID,
    val assignmentId: UUID,
    val paperDate: LocalDate,
    val paperKind: PaperKind,
    val title: String?,
    val confirmationState: String,
    val version: Long,
    val pageCount: Int,
    val serverCommittedAt: Instant,
)

enum class CreatePaperSubmissionRejection(val wireCode: String) {
    OperationIdRequired("XQ_OPERATION_ID_REQUIRED"),
    TeachingContextRequired("XQ_TEACHING_CONTEXT_REQUIRED"),
    AuthRequired("XQ_AUTH_REQUIRED"),
    ActorNotFound("XQ_ACTOR_NOT_FOUND"),
    ActorDisabled("XQ_ACTOR_DISABLED"),
    MembershipRequired("XQ_MEMBERSHIP_REQUIRED"),
    MembershipDisabled("XQ_MEMBERSHIP_DISABLED"),
    TeachingCapabilityRequired("XQ_TEACHING_CAPABILITY_REQUIRED"),
    TeachingContextUnavailable("XQ_TEACHING_CONTEXT_UNAVAILABLE"),
    OperationReusedWithDifferentPayload("XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD"),
    InvalidPaperDate("XQ_INVALID_PAPER_DATE"),
    InvalidPaperKind("XQ_INVALID_PAPER_KIND"),
    InvalidPaperTitle("XQ_INVALID_PAPER_TITLE"),
    InvalidPaperPages("XQ_INVALID_PAPER_PAGES"),
    AttachmentScopeMismatch("XQ_PAPER_ATTACHMENT_SCOPE_MISMATCH"),
    ObservationScopeMismatch("XQ_PAPER_OBSERVATION_SCOPE_MISMATCH"),
    ;

    companion object {
        private val byWireCode = entries.associateBy(CreatePaperSubmissionRejection::wireCode)

        fun fromWireCode(wireCode: String): CreatePaperSubmissionRejection? =
            byWireCode[wireCode]
    }
}

enum class PaperSubmissionUnknownReason {
    Timeout,
    NetworkFailure,
    ServerFailure,
    InvalidSuccessPayload,
}

enum class PaperSubmissionProtocolFailure {
    UnrecognizedResponse,
    UnknownDomainError,
    ProviderProtocolError,
}

sealed interface PaperSubmissionResult {
    val operationId: UUID

    data class Accepted(
        override val operationId: UUID,
        val receipt: CreatePaperSubmissionReceipt,
    ) : PaperSubmissionResult

    data class Rejected(
        override val operationId: UUID,
        val rejection: CreatePaperSubmissionRejection,
    ) : PaperSubmissionResult

    data class AuthenticationRequired(
        override val operationId: UUID,
    ) : PaperSubmissionResult

    data class UnknownResult(
        override val operationId: UUID,
        val reason: PaperSubmissionUnknownReason,
    ) : PaperSubmissionResult

    data class ProtocolFailure(
        override val operationId: UUID,
        val failure: PaperSubmissionProtocolFailure,
    ) : PaperSubmissionResult
}

fun interface PaperSubmissionRemote {
    fun create(request: CreatePaperSubmissionRequest): PaperSubmissionResult
}
