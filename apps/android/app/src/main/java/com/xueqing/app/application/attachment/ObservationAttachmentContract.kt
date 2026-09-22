package com.xueqing.app.application.attachment

import java.time.Instant
import java.util.UUID

data class ObservationAttachmentUploadRequest(
    val objectName: String,
    val contentType: String,
    val byteSize: Long,
)

enum class AttachmentUploadUnknownReason {
    Timeout,
    NetworkFailure,
    ServerFailure,
}

enum class AttachmentUploadRejection {
    AccessDenied,
    InvalidRequest,
    UnexpectedResponse,
    LocalContentMismatch,
}

sealed interface AttachmentUploadResult {
    data object Uploaded : AttachmentUploadResult
    data object AlreadyPresent : AttachmentUploadResult
    data object AuthenticationRequired : AttachmentUploadResult
    data class UnknownResult(val reason: AttachmentUploadUnknownReason) : AttachmentUploadResult
    data class Rejected(val rejection: AttachmentUploadRejection) : AttachmentUploadResult
}

fun interface AttachmentStorageRemote {
    fun upload(
        request: ObservationAttachmentUploadRequest,
        body: ByteArray,
    ): AttachmentUploadResult
}

data class CommitObservationAttachmentRequest(
    val operationId: UUID,
    val organizationId: UUID,
    val studentId: UUID,
    val subjectProfileId: UUID,
    val assignmentId: UUID,
    val observationId: UUID,
    val attachmentId: UUID,
)

data class CommitObservationAttachmentReceipt(
    val command: String,
    val operationId: UUID,
    val attachmentId: UUID,
    val observationId: UUID,
    val actorAppUserId: UUID,
    val organizationId: UUID,
    val studentId: UUID,
    val subjectProfileId: UUID,
    val subjectKey: String,
    val assignmentId: UUID,
    val bucketId: String,
    val objectName: String,
    val contentType: String,
    val byteSize: Long,
    val serverCommittedAt: Instant,
)

enum class CommitObservationAttachmentRejection(val wireCode: String) {
    OperationIdRequired("XQ_OPERATION_ID_REQUIRED"),
    AttachmentContextRequired("XQ_ATTACHMENT_CONTEXT_REQUIRED"),
    AuthRequired("XQ_AUTH_REQUIRED"),
    ActorNotFound("XQ_ACTOR_NOT_FOUND"),
    ActorDisabled("XQ_ACTOR_DISABLED"),
    OperationReusedWithDifferentPayload("XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD"),
    MembershipRequired("XQ_MEMBERSHIP_REQUIRED"),
    MembershipDisabled("XQ_MEMBERSHIP_DISABLED"),
    TeachingCapabilityRequired("XQ_TEACHING_CAPABILITY_REQUIRED"),
    StudentNotInOrganization("XQ_STUDENT_NOT_IN_ORG"),
    SubjectProfileRequired("XQ_SUBJECT_PROFILE_REQUIRED"),
    TeacherAssignmentRequired("XQ_TEACHER_ASSIGNMENT_REQUIRED"),
    ObservationParentRequired("XQ_OBSERVATION_ATTACHMENT_PARENT_REQUIRED"),
    AttachmentObjectRequired("XQ_ATTACHMENT_OBJECT_REQUIRED"),
    ContentTypeInvalid("XQ_ATTACHMENT_CONTENT_TYPE_INVALID"),
    StorageMetadataInvalid("XQ_ATTACHMENT_STORAGE_METADATA_INVALID"),
    SizeInvalid("XQ_ATTACHMENT_SIZE_INVALID"),
    AlreadyCommitted("XQ_ATTACHMENT_ALREADY_COMMITTED"),
    ;

    companion object {
        private val byWireCode = entries.associateBy(CommitObservationAttachmentRejection::wireCode)

        fun fromWireCode(wireCode: String): CommitObservationAttachmentRejection? =
            byWireCode[wireCode]
    }
}

enum class AttachmentCommitUnknownReason {
    Timeout,
    NetworkFailure,
    ServerFailure,
    InvalidSuccessPayload,
}

enum class AttachmentCommitProtocolFailure {
    UnrecognizedResponse,
    UnknownDomainError,
    ProviderProtocolError,
}

sealed interface AttachmentCommitResult {
    val operationId: UUID

    data class Accepted(
        override val operationId: UUID,
        val receipt: CommitObservationAttachmentReceipt,
    ) : AttachmentCommitResult

    data class Rejected(
        override val operationId: UUID,
        val rejection: CommitObservationAttachmentRejection,
    ) : AttachmentCommitResult

    data class AuthenticationRequired(
        override val operationId: UUID,
    ) : AttachmentCommitResult

    data class UnknownResult(
        override val operationId: UUID,
        val reason: AttachmentCommitUnknownReason,
    ) : AttachmentCommitResult

    data class ProtocolFailure(
        override val operationId: UUID,
        val failure: AttachmentCommitProtocolFailure,
    ) : AttachmentCommitResult
}

fun interface AttachmentCommandRemote {
    fun commit(request: CommitObservationAttachmentRequest): AttachmentCommitResult
}


enum class AttachmentReadUnknownReason {
    Timeout,
    NetworkFailure,
    ServerFailure,
}

enum class AttachmentReadRejection {
    AccessDenied,
    InvalidMetadata,
    UnexpectedResponse,
}

sealed interface AttachmentReadResult {
    data class Loaded(
        val contentType: String,
        val bytes: ByteArray,
    ) : AttachmentReadResult

    data object AuthenticationRequired : AttachmentReadResult

    data class UnknownResult(
        val reason: AttachmentReadUnknownReason,
    ) : AttachmentReadResult

    data class Rejected(
        val rejection: AttachmentReadRejection,
    ) : AttachmentReadResult
}

fun interface AttachmentReadRemote {
    /**
     * Reads only an Attachment already represented by an authoritative commit
     * receipt. Implementations must revalidate the canonical locator before
     * issuing a provider request.
     */
    fun read(receipt: CommitObservationAttachmentReceipt): AttachmentReadResult
}
